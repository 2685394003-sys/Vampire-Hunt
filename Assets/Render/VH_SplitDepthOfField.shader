// 径向景深（前后 + 左右）· Shader
//
// 结构与 URP 内置的 DepthOfField / GaussianDepthOfField.shader 同构（5 个 pass：
// CoC -> 降采样 -> 水平模糊 -> 垂直模糊 -> 合成），被替换的只有 CoC 的计算方式：
//
//   内置：coc = (viewZ - start) / (end - start)          —— 单一标量对，不含任何方向信息
//   本件：把「离玩家多远」拆成两条互相独立的轴，最后取二者较大值：
//
//     · 深度轴（前后）—— 以玩家脚底所在的相机深度为原点。
//         画面在分界线（玩家脚底那条屏幕水平线）上方走「前带」Start/End，
//         下方走「后带」Start/End。两组起止点分开填（身前可见 8.2m、身后只有 5.7m，
//         虚化节奏本来就不同），但 **共用一个强度**（Depth Strength）。
//
//     · 横轴（左右）—— 以玩家所在的那条屏幕竖线为原点，换算成世界横向距离（米），
//         再套 Lateral Start / End / Strength。
//
//   最终 coc = max(深度轴, 横轴) ⇒ 玩家周围是一块清晰区，前方/后方/左侧/右侧渐糊。
//
// 左右为什么能用「米」：相机只有 yaw + pitch、没有 roll，相机的 right 向量始终躺在
// 世界水平面内 ⇒ 相机空间的横向距离就是玩家的「左右」距离。换算（透视投影）：
//     ndcX    = x_cam / (tanHalfFovX * z_cam)
//     lateral = |uv.x - 中心x| * 2 * tanHalfFovX * viewZ      // viewZ 即 z_cam
// 相机偏正交时会退化，本项目是透视（FOV 60），成立。
//
// 为什么必须自研：内置景深的 CoC 是各向同性的单组参数，数学上不含任何方向信息，
// 调多少数值都无法让「身前」「身后」「左右」用不同的起止点。
//
// ⚠ 使用前提：Volume（SampleSceneProfile）里内置的 Depth of Field 必须保持 Off，
//    否则会与本 Feature 叠加成双重虚化。

Shader "VH/SplitDepthOfField"
{
    HLSLINCLUDE

    #pragma target 3.5

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Filtering.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

    // 模糊结果（半分辨率，RGB）
    TEXTURE2D_X(_VH_BlurTexture);
    // CoC 图（全分辨率，单通道 R16）
    TEXTURE2D_X(_VH_FullCoCTexture);

    // (w, h, 1/w, 1/h) —— 源图尺寸
    float4 _VH_SourceSize;
    // (1/ds, 1/ds, ds, ds)，ds = 降采样倍率
    float4 _VH_DownSampleScaleFactor;

    // x = 分界的相机深度（米）  y = 最大模糊半径  z = uv.y 原点是否向下增大  w = 是否用采样的兜底分界
    float4 _VH_DofParams;
    // x = 前带起点  y = 前带终点  z/w = 未用
    float4 _VH_FrontBand;
    // x = 后带起点  y = 后带终点  z/w = 未用
    float4 _VH_BackBand;
    // x = 左右带起点(米)  y = 左右带终点(米)  z = 左右带强度  w = 未用
    float4 _VH_LateralBand;
    // x = 分界屏幕高度(0=下 1=上)  y = 过渡半宽  z = 调试视图  w = 深度轴强度（前后共用）
    float4 _VH_SplitParams;
    // x = 玩家所在屏幕横坐标(0=左 1=右)  y = tan(半水平FOV)  z/w = 未用
    float4 _VH_CenterParams;

    #define VH_SPLIT_VIEWZ        _VH_DofParams.x
    #define VH_MAX_RADIUS         _VH_DofParams.y
    #define VH_UV_TOP_DOWN        _VH_DofParams.z
    #define VH_USE_SAMPLED_SPLIT  _VH_DofParams.w

    #define VH_FRONT_START        _VH_FrontBand.x
    #define VH_FRONT_END          _VH_FrontBand.y
    #define VH_BACK_START         _VH_BackBand.x
    #define VH_BACK_END           _VH_BackBand.y

    #define VH_LATERAL_START      _VH_LateralBand.x
    #define VH_LATERAL_END        _VH_LateralBand.y
    #define VH_LATERAL_STRENGTH   _VH_LateralBand.z

    #define VH_SPLIT_Y            _VH_SplitParams.x
    #define VH_SPLIT_SOFT         _VH_SplitParams.y
    #define VH_DEBUG_MODE         _VH_SplitParams.z
    #define VH_DEPTH_STRENGTH     _VH_SplitParams.w

    #define VH_CENTER_X           _VH_CenterParams.x
    #define VH_TAN_HALF_FOV_X     _VH_CenterParams.y

    // 可分离高斯核：5 抽头（双线性取样）= 9 抽头等价。系数取自 URP 内置景深，保证手感一致。
    const static int   kTapCount  = 5;
    const static float kOffsets[] = { -3.23076923, -1.38461538, 0.00000000, 1.38461538, 3.23076923 };
    const static half  kCoeffs[]  = { 0.07027027, 0.31621622, 0.22702703, 0.31621622, 0.07027027 };

    // ------------------------------------------------------------------
    // 屏幕方向归一到「下 = 0，上 = 1」
    //
    // ⚠ 这里的方向已经过实机标定（D3D12 + URP RenderGraph 的 Blitter 全屏三角形）：
    //    shader 里 uv.y 的 0 就是画面「下」边缘、1 是画面「上」边缘，与 UnityEngine 的
    //    视口坐标（WorldToViewportPoint）同向，因此 VH_UV_TOP_DOWN 常态就是 0、不翻转。
    //
    //    曾经踩过的坑：照 UNITY_UV_STARTS_AT_TOP（D3D 下为真）去推，会得出「uv.y = 0 是画面上边缘」
    //    而多做一次 1 - uv.y，结果整个前/后分区上下颠倒 —— 分界之上本该是玩家身前，却被判成了身后。
    //    注意该宏描述的是「纹理 V 轴起点在顶部」，与 Blitter 实际给出的 texcoord 不是一回事。
    //
    //    C# 侧保留一个手动标定开关（Invert Screen Axis），换图形 API 且发现前后反了时勾上即可。
    //    横向（uv.x）不需要这套：左右两侧用 |uv.x - 中心x|，天然对称，翻转也不影响。
    // ------------------------------------------------------------------
    float VHScreenUp(float uvY)
    {
        return lerp(uvY, 1.0 - uvY, saturate(VH_UV_TOP_DOWN));
    }

    // 分界线对应的原始 uv.y
    float VHSplitUvY()
    {
        return lerp(VH_SPLIT_Y, 1.0 - VH_SPLIT_Y, saturate(VH_UV_TOP_DOWN));
    }

    // ------------------------------------------------------------------
    // 两条轴的权重，各自 0 = 完全清晰、1 = 完全模糊
    // ------------------------------------------------------------------

    // 深度轴（前后）：以玩家脚底所在深度为原点，线上方走前带、下方走后带，共用一个强度
    half VHDepthWeight(float uvY, float viewZ, float splitViewZ)
    {
        // 相对玩家脚底的相机深度差：> 0 表示在玩家身前（画面靠上），< 0 表示在玩家身后
        float d = viewZ - splitViewZ;

        // 前带：越往前越糊
        float frontDen = max(1e-4, VH_FRONT_END - VH_FRONT_START);
        half  wFront   = saturate((d - VH_FRONT_START) / frontDen);

        // 后带：深度差翻正后套同一条公式，变成「越往后越糊」
        float backDen = max(1e-4, VH_BACK_END - VH_BACK_START);
        half  wBack   = saturate((-d - VH_BACK_START) / backDen);

        // 按玩家脚底那条水平线分区：线以上走前带，线以下走后带；soft 段内平滑过渡
        float soft      = max(1e-5, VH_SPLIT_SOFT);
        half  sideFront = smoothstep(VH_SPLIT_Y - soft, VH_SPLIT_Y + soft, VHScreenUp(uvY));

        return saturate(lerp(wBack, wFront, sideFront) * VH_DEPTH_STRENGTH);
    }

    // 横轴（左右）：把「离玩家那条竖线多少屏幕距离」换算成世界横向米数
    half VHLateralWeight(float uvX, float viewZ)
    {
        float lateralMeters = abs(uvX - VH_CENTER_X) * 2.0 * VH_TAN_HALF_FOV_X * viewZ;

        float den = max(1e-4, VH_LATERAL_END - VH_LATERAL_START);
        return saturate((lateralMeters - VH_LATERAL_START) / den) * VH_LATERAL_STRENGTH;
    }

    // 两条轴取较大值：任一条轴判定该糊，就糊
    half VHComputeCoC(float2 uv, float viewZ, float splitViewZ)
    {
        half depthW   = VHDepthWeight(uv.y, viewZ, splitViewZ);
        half lateralW = VHLateralWeight(uv.x, viewZ);
        return saturate(max(depthW, lateralW));
    }

    half FragCoC(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);

        float rawDepth = LOAD_TEXTURE2D_X(_CameraDepthTexture, _VH_SourceSize.xy * uv).x;
        float viewZ    = LinearEyeDepth(rawDepth, _ZBufferParams);

        float splitViewZ = VH_SPLIT_VIEWZ;
        if (VH_USE_SAMPLED_SPLIT > 0.5)
        {
            // 兜底：没接上玩家时（主菜单 / 玩家尚未生成），
            // 直接取分界线上那一点的实际相机深度当分界。全场像素读同一个纹素，开销可忽略。
            float raw = LOAD_TEXTURE2D_X(_CameraDepthTexture, _VH_SourceSize.xy * float2(0.5, VHSplitUvY())).x;
            splitViewZ = LinearEyeDepth(raw, _ZBufferParams);
        }

        return VHComputeCoC(uv, viewZ, splitViewZ);
    }

    // 降采样到半分辨率（双线性自带平滑）
    half4 FragDownsample(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);
        return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
    }

    half4 VHBlur(Varyings input, float2 dir)
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);

        // 用中心像素的 CoC 决定模糊半径：越糊的地方半径越大
        half   centerCoC = SAMPLE_TEXTURE2D_X(_VH_FullCoCTexture, sampler_LinearClamp, uv).x;
        float2 offset    = _VH_SourceSize.zw * _VH_DownSampleScaleFactor.zw * dir * centerCoC * VH_MAX_RADIUS;

        half4 acc  = 0.0;
        half  wsum = 0.0;

        UNITY_UNROLL
        for (int i = 0; i < kTapCount; i++)
        {
            float2 tapUv    = uv + kOffsets[i] * offset;
            half   tapCoC   = SAMPLE_TEXTURE2D_X(_VH_FullCoCTexture, sampler_LinearClamp, tapUv).x;
            half4  tapColor = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, tapUv);

            // 邻域比中心清晰得多就压低它的权重 —— 抑制清晰前景渗进模糊区（同 URP 做法）
            half w = kCoeffs[i] * saturate(1.0 - (centerCoC - tapCoC));

            acc  += tapColor * w;
            wsum += w;
        }

        return acc / max(wsum, 1e-4);
    }

    half4 FragBlurH(Varyings input) : SV_Target
    {
        return VHBlur(input, float2(1.0, 0.0));
    }

    half4 FragBlurV(Varyings input) : SV_Target
    {
        return VHBlur(input, float2(0.0, 1.0));
    }

    half4 FragComposite(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);

        half  coc        = LOAD_TEXTURE2D_X(_VH_FullCoCTexture, _VH_SourceSize.xy * uv).x;
        half4 sharpColor = LOAD_TEXTURE2D_X(_BlitTexture, _VH_SourceSize.xy * uv);
        half4 blurColor  = SAMPLE_TEXTURE2D_X(_VH_BlurTexture, sampler_LinearClamp, uv);

        // 平滑过渡（同 URP 内置景深："CryEngine 3 Graphics Gems" [Sousa13]）
        half  blend    = saturate(sqrt(coc * TWO_PI));
        half3 outColor = lerp(sharpColor.rgb, blurColor.rgb, blend);

        // 调试视图：
        //   0 = 关闭
        //   1 = 只画中心线（红 = 深度轴分界，绿 = 左右轴中心竖线），用来确认中心位置
        //   2 = CoC 场可视化（黑 = 清晰，白 = 完全模糊）叠加中心线，
        //       单帧就能看出两条轴的起止点与方向是否正确
        //   3 = 轴诊断（见下）
        bool onSplitLine  = abs(VHScreenUp(uv.y) - VH_SPLIT_Y) < 1.5 * _VH_SourceSize.w;
        bool onCenterLine = abs(uv.x - VH_CENTER_X) < 1.5 * _VH_SourceSize.z;

        if (VH_DEBUG_MODE > 1.5)
        {
            outColor = half3(coc, coc, coc);
        }

        if (VH_DEBUG_MODE > 0.5)
        {
            if (onSplitLine)  outColor = half3(1.0, 0.0, 0.0);
            if (onCenterLine) outColor = half3(0.0, 1.0, 0.0);
        }

        if (VH_DEBUG_MODE > 2.5)
        {
            // 轴诊断：只用饱和色，单帧即可判定「哪条轴在起作用」，
            // 不依赖对 8 位截图的数值解码（截图会过 ACES 色调映射，数值不可信）。
            //   深度轴拉满    → 红      左右轴拉满    → 蓝
            //   两轴都拉满    → 白      都还没到      → 绿
            //   相机深度 > 50m（深度图多半没接上）→ 品红
            float rawD = LOAD_TEXTURE2D_X(_CameraDepthTexture, _VH_SourceSize.xy * uv).x;
            float vZ   = LinearEyeDepth(rawD, _ZBufferParams);

            half dW = VHDepthWeight(uv.y, vZ, VH_SPLIT_VIEWZ);
            half lW = VHLateralWeight(uv.x, vZ);

            bool dHot = dW > 0.95;
            bool lHot = lW > 0.95;

            outColor = half3(0.0, 1.0, 0.0);
            if (dHot)          outColor = half3(1.0, 0.0, 0.0);
            if (lHot)          outColor = half3(0.0, 0.0, 1.0);
            if (dHot && lHot)  outColor = half3(1.0, 1.0, 1.0);
            if (vZ > 50.0)     outColor = half3(1.0, 0.0, 1.0);
        }

        return half4(outColor, sharpColor.a);
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        LOD 100
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "VH Split DoF CoC"

            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment FragCoC
            ENDHLSL
        }

        Pass
        {
            Name "VH Split DoF Downsample"

            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment FragDownsample
            ENDHLSL
        }

        Pass
        {
            Name "VH Split DoF Blur Horizontal"

            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment FragBlurH
            ENDHLSL
        }

        Pass
        {
            Name "VH Split DoF Blur Vertical"

            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment FragBlurV
            ENDHLSL
        }

        Pass
        {
            Name "VH Split DoF Composite"

            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment FragComposite
            ENDHLSL
        }
    }

    Fallback Off
}
