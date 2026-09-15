// 假云 · 地面云影（Cloud Shadow）· Shader
//
// 为什么是"地面云影"而不是天上的云：
//   本项目相机 CinemachineFollow.FollowOffset = (0, 10.5, -3.18)、FOV 60、俯角 73.15°，
//   即使被 CinemachineDeoccluder 拉到最平（俯角 56.3°），画面上缘仍留 31°~43° 俯角 ——
//   **天空盒 / 地平线 / 天上云层永远不在画面里**。所以"云"只能画在可见地面带内
//   （相对玩家：身前 ≤ 18.4m / 身后 ≤ 7.2m / 横向 ≤ ±26.1m）。
//
// 做法：
//   1) 用 _CameraDepthTexture + unity_MatrixInvVP 把每个屏幕像素反投影回世界坐标；
//   2) 取世界 XZ 当 UV，叠加两层程序化 value-noise fbm（不引任何贴图）；
//   3) 结果作为掩码，对画面做乘法压暗 —— 物理上等价于地上的云影；
//   4) 同一层噪声取反（1 − cloud）再阈值化，做成**云隙透光**：云缝处对画面做加法提亮。
//      「云挡光 ⇒ 地面有影」和「云缝透光 ⇒ 地面有光斑」本来就是同一个物理量的两面，
//      所以**不新增 pass、不新增 Feature**，两个通道在同一次全屏 blit 里出。
//      用途：阴郁氛围（冷色、低强度的"云缝漏光"）。
//
// 为什么不需要遮罩（不像"分层虚化"那样要区分角色/背景）：
//   现实里的云影本来就同时压暗地面、角色和特效，所以全屏相乘是**物理正确**的，
//   不需要任何 layer 遮罩。这直接绕开了锐利层遮罩那套工程。
//
// 为什么多人下天然一致：
//   云图案锚在**世界坐标**里（UV 来自 worldPos），各客户端反投影出的图案完全相同，
//   不存在"以玩家为中心的可见半径"那种同步问题。
//   ⚠ 各客户端的漂移相位来自各自的 _VH_CloudParams2.z，可能不同步 —— 属纯视觉差异
//     （和地面雾一样"各客户端各渲一份"），不影响玩法。
//
// ⚠ 成立前提：相机只有 yaw + pitch、**没有 roll**（否则世界坐标反投影的横向会歪）。
//    本项目是 Cinemachine 俯视机架，满足。若以后加 roll 需改公式。
//
// ⚠ 参数布局约定：所有数值从 C# 侧推（不含 _Time），方便以后换成局内同步时间。
//
// ⚠ Blit 的 uv 约定：URP RenderGraph 的 Blitter 全屏三角形在 D3D12 上，
//    shader 里 uv.y = 0 就是画面「下」边缘。采样深度与 ComputeWorldSpacePosition
//    必须用**同一个 uv**，否则反投影结果会上下错位。

Shader "VH/CloudShadow"
{
    HLSLINCLUDE

    #pragma target 3.5

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
    #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

    // x = 强度  y = 云尺度(米)  z = 覆盖率阈值  w = 边缘软度
    float4 _VH_CloudParams0;
    // x = 第二层权重  y = 第二层尺度比  z = 第二层速度比  w = 调试档位
    float4 _VH_CloudParams1;
    // xy = 漂移速度(米/秒)  z = 时间(秒，由 C# 推)  w = 对比度
    float4 _VH_CloudParams2;
    // rgb = 云影颜色（线性空间）  a = 未用
    float4 _VH_CloudColor;
    // (w, h, 1/w, 1/h) —— 用于把调试线宽换算成像素
    float4 _VH_CloudSourceSize;

    // ── 云隙透光（Cloud Gap Light）──────────────────────────────────
    // x = 强度  y = 阈值  z = 边缘软度  w = 对比度
    float4 _VH_GapParams0;
    // x = 沿光方向拉伸倍数  y = 光的地面投影方向(弧度)  z = 相对云影的噪声尺度比  w = 预留
    float4 _VH_GapParams1;
    // rgb = 透光颜色（线性空间）  a = 未用
    float4 _VH_GapColor;

    #define VH_CLOUD_STRENGTH      _VH_CloudParams0.x
    #define VH_CLOUD_SCALE         _VH_CloudParams0.y
    #define VH_CLOUD_THRESHOLD     _VH_CloudParams0.z
    #define VH_CLOUD_SOFTNESS      _VH_CloudParams0.w

    #define VH_CLOUD_LAYER2_WEIGHT _VH_CloudParams1.x
    #define VH_CLOUD_LAYER2_SCALE  _VH_CloudParams1.y
    #define VH_CLOUD_LAYER2_SPEED  _VH_CloudParams1.z
    #define VH_CLOUD_DEBUG         _VH_CloudParams1.w

    #define VH_CLOUD_DRIFT         _VH_CloudParams2.xy
    #define VH_CLOUD_TIME          _VH_CloudParams2.z
    #define VH_CLOUD_CONTRAST      _VH_CloudParams2.w

    #define VH_GAP_STRENGTH        _VH_GapParams0.x
    #define VH_GAP_THRESHOLD       _VH_GapParams0.y
    #define VH_GAP_SOFTNESS        _VH_GapParams0.z
    #define VH_GAP_CONTRAST        _VH_GapParams0.w

    #define VH_GAP_STRETCH         _VH_GapParams1.x
    #define VH_GAP_ANGLE           _VH_GapParams1.y
    #define VH_GAP_SCALE_RATIO     _VH_GapParams1.z

    #define VH_CLOUD_DEBUG_OFF       0.0
    #define VH_CLOUD_DEBUG_MASK      1.0
    #define VH_CLOUD_DEBUG_WORLDGRID 2.0
    #define VH_CLOUD_DEBUG_GAPMASK   3.0
    #define VH_CLOUD_DEBUG_GAPRAW    4.0

    // ------------------------------------------------------------------
    // 程序化噪声（不引贴图 ⇒ 无 tiling 接缝、无美术资源依赖）
    // ------------------------------------------------------------------

    float VHHash21(float2 p)
    {
        p = frac(p * float2(123.34, 456.21));
        p += dot(p, p + 45.32);
        return frac(p.x * p.y);
    }

    // 二维 value noise：格子内双线性 + 五次平滑，值域 0~1
    float VHValueNoise(float2 p)
    {
        float2 i = floor(p);
        float2 f = frac(p);
        f = f * f * (3.0 - 2.0 * f);

        float a = VHHash21(i);
        float b = VHHash21(i + float2(1.0, 0.0));
        float c = VHHash21(i + float2(0.0, 1.0));
        float d = VHHash21(i + float2(1.0, 1.0));

        return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
    }

    // 4 阶 fbm，归一化到 0~1（均值约 0.5）
    // 每阶都加一个偏移，避免各阶在原点附近同相叠加出规则纹路
    float VHFbm(float2 p)
    {
        float sum = 0.0;
        float amp = 0.5;
        float norm = 0.0;

        UNITY_UNROLL
        for (int i = 0; i < 4; i++)
        {
            sum  += amp * VHValueNoise(p);
            norm += amp;
            p = p * 2.03 + float2(17.7, 31.3);
            amp *= 0.5;
        }

        return sum / max(norm, 1e-5);
    }

    // ------------------------------------------------------------------
    // 云量：以世界 XZ（米）为 UV，两层不同尺度/速度的 fbm 混合，输出连续值 0~1
    // ------------------------------------------------------------------
    float VHCloudDensity(float2 worldXz, float scaleMeters)
    {
        float scale = max(1e-3, scaleMeters);

        float2 baseXz = worldXz / scale;
        float2 drift  = VH_CLOUD_DRIFT * VH_CLOUD_TIME;

        float2 uv1 = baseXz + drift;
        float2 uv2 = baseXz / max(1e-3, VH_CLOUD_LAYER2_SCALE) + drift * VH_CLOUD_LAYER2_SPEED;

        float cloud = lerp(VHFbm(uv1), VHFbm(uv2), saturate(VH_CLOUD_LAYER2_WEIGHT));

        // 🔴 关键一步：4 阶 fbm 的实际取值范围只有约 [0.21, 0.79]（实测 p1~p99），
        //    中段还高度集中在 0.5 附近（全局 std ≈ 0.13，单屏内 std ≈ 0.10），
        //    直接拿去跟 0~1 的阈值比 ⇒ 阈值稍微一抬就整屏全黑、稍微一降就整屏全白。
        //    这里以 0.5 为轴线性拉开，让 Contrast / Threshold / Softness 变成好用的旋钮。
        //    （对比度的实测影响：gain 1.0 → 覆盖率约 12%；gain 2.2 → 约 30%。）
        return saturate((cloud - 0.5) * max(1.0, VH_CLOUD_CONTRAST) + 0.5);
    }

    // 云影掩码：1 = 云最厚处（要压暗），0 = 云缝
    float VHCloudMask(float3 worldPos)
    {
        float cloud = VHCloudDensity(worldPos.xz, VH_CLOUD_SCALE);
        float soft  = max(1e-3, VH_CLOUD_SOFTNESS);
        return smoothstep(VH_CLOUD_THRESHOLD, VH_CLOUD_THRESHOLD + soft, cloud);
    }

    // ------------------------------------------------------------------
    // 云隙透光掩码：1 = 云缝（要被照亮），0 = 云所在处
    //
    // 与云影共用同一层噪声 —— 物理上"云挡光⇒地面有影"和"云缝透光⇒地面有光斑"
    // 本来就是同一个量的两面。区别只在：
    //   ① 阈值开在另一端（gap = 1 − cloud）；
    //   ② 采样坐标沿**光的地面投影方向**做了各向异性压缩 ⇒ 图案在该方向被拉长。
    //      🔎 Stretch = 1 时就是严格的物理互补（恰好等于云影的反相）；
    //         > 1 是**刻意的艺术偏移**，用来让光斑不与云影严格重合 ——
    //         否则整片地面会像"印上去的对称图案"，反而失真。
    // ------------------------------------------------------------------
    float VHGapRaw(float3 worldPos)
    {
        float2 d = float2(cos(VH_GAP_ANGLE), sin(VH_GAP_ANGLE));
        float  k = max(1.0, VH_GAP_STRETCH);

        // 把采样坐标沿 d 方向压缩 k 倍 ⇒ 采样出的图案在 d 方向被拉长 k 倍
        float2 xz = worldPos.xz;
        float  t  = dot(xz, d);
        float2 folded = xz - d * (t * (1.0 - 1.0 / k));

        // 光的噪声用**更细的尺度**（默认 0.4×云影尺度）—— 这是稳定性开关，不是风格选项。
        // 理由：① 云的边缘/薄处本来就比云体本体碎，透光的光斑因此更细更碎；
        //      ② 更关键 —— 可见地面带只有约 31m 宽，若和云影同样用 30m 尺度，
        //         一帧只采得到 ~1 个噪声团，覆盖率会在 0%~90% 之间乱跳
        //         （实测：阈值固定时相邻 8 个位置的覆盖率 min 0.0% / max 89.9%）。
        //         取 0.4× ⇒ 约 2.5 个团/帧，覆盖率显著稳定。
        float gapScale = VH_CLOUD_SCALE * max(0.05, VH_GAP_SCALE_RATIO);

        float gap = 1.0 - VHCloudDensity(folded, gapScale);
        return saturate((gap - 0.5) * max(1.0, VH_GAP_CONTRAST) + 0.5);
    }

    float VHGapLightMask(float3 worldPos)
    {
        float gap  = VHGapRaw(worldPos);
        float soft = max(1e-3, VH_GAP_SOFTNESS);
        return smoothstep(VH_GAP_THRESHOLD, VH_GAP_THRESHOLD + soft, gap);
    }

    // 每 10m 一条的世界网格，用来验证反投影是否正确：
    // 网格必须**贴在地面上、随世界移动**，而不是"贴在屏幕上不动"。
    float VHWorldGrid(float3 worldPos)
    {
        const float kCellMeters = 10.0;

        // 到最近一条网格线的距离（米）
        float2 cell     = worldPos.xz / kCellMeters;
        float2 distM    = abs(frac(cell) - 0.5) * kCellMeters;

        // 屏幕像素在世界上覆盖多大范围 → 换算成线宽，保证远近都不闪烁
        float2 pixelWs  = max(fwidth(worldPos.xz), 1e-4) * 1.5;

        // ⚠ 变量名不能用 line —— 它是 HLSL 保留字（几何着色器图元名），会报
        //    "syntax error: unexpected token 'line'"。
        float2 lineMask = 1.0 - saturate(distM / pixelWs);
        return max(lineMask.x, lineMask.y);
    }

    half4 FragCloudShadow(Varyings input) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
        float2 uv = UnityStereoTransformScreenSpaceTex(input.texcoord);

        half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

        float rawDepth = SampleSceneDepth(uv);
        float viewZ    = LinearEyeDepth(rawDepth, _ZBufferParams);

        // 天空 / 远裁剪保护：这些像素没有地面可投云影，直接原样返回。
        // UNITY_RAW_FAR_CLIP_VALUE：反向 Z 平台为 0，其余为 1。
        if (abs(rawDepth - UNITY_RAW_FAR_CLIP_VALUE) < 1e-6 || viewZ > 900.0)
        {
            return source;
        }

        // 屏幕像素 → 世界坐标（与深度采样共用同一个 uv）
        float3 worldPos = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);

        float mask = VHCloudMask(worldPos);

        if (VH_CLOUD_DEBUG > VH_CLOUD_DEBUG_MASK - 0.5 && VH_CLOUD_DEBUG < VH_CLOUD_DEBUG_MASK + 0.5)
        {
            // 云掩码灰度：黑 = 无云，白 = 满云。单帧即可判断覆盖率与图案尺度。
            return half4(mask, mask, mask, 1.0);
        }

        // 调试档位：WorldGrid 这一档必须加上界，否则 GapMask(3) 会被它抢先拦下。
        if (VH_CLOUD_DEBUG > VH_CLOUD_DEBUG_WORLDGRID - 0.5
            && VH_CLOUD_DEBUG < VH_CLOUD_DEBUG_GAPMASK - 0.5)
        {
            // 世界网格：青色线。用于验证反投影与"相机无 roll"前提。
            float g = VHWorldGrid(worldPos);
            half3 gridColor = lerp(source.rgb, half3(0.0, 1.0, 1.0), g);
            return half4(gridColor, source.a);
        }

        if (VH_CLOUD_DEBUG > VH_CLOUD_DEBUG_GAPMASK - 0.5 && VH_CLOUD_DEBUG < VH_CLOUD_DEBUG_GAPRAW - 0.5)
        {
            // 透光掩码灰度：黑 = 无光，白 = 最亮。单帧即可判断光斑的覆盖率与拉长方向。
            float gm = VHGapLightMask(worldPos);
            return half4(gm, gm, gm, 1.0);
        }

        if (VH_CLOUD_DEBUG > VH_CLOUD_DEBUG_GAPRAW - 0.5)
        {
            // 透光**连续值**（未经阈值/软度处理）：一次截图就能读出整条覆盖率曲线，
            // 用来校准 Threshold / Softness，不用反复改参数再截图。
            float gr = VHGapRaw(worldPos);
            return half4(gr, gr, gr, 1.0);
        }

        // ① 云影：云所在处 → 对画面做**乘法压暗**。
        // ⚠ _VH_CloudColor 是线性空间颜色，禁止给 >1 的 HDR 亮值（ACES + Bloom 会整屏发灰）。
        half3 shadow = lerp(1.0, _VH_CloudColor.rgb, mask * saturate(VH_CLOUD_STRENGTH));
        half3 result = source.rgb * shadow;

        // ② 云隙透光：云缝处 → 对画面做**加法提亮**。
        // ⚠ 同样禁给 >1 的 HDR 亮值；强度也要小，否则 ACES + Bloom 3.0 会把亮部糊开、整屏发灰。
        float gap = VHGapLightMask(worldPos);
        result += _VH_GapColor.rgb * (gap * max(0.0, VH_GAP_STRENGTH));

        return half4(result, source.a);
    }

    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }
        LOD 100
        ZTest Always ZWrite Off Cull Off

        Pass
        {
            Name "VH Cloud Shadow"

            HLSLPROGRAM
                #pragma vertex Vert
                #pragma fragment FragCloudShadow
            ENDHLSL
        }
    }

    Fallback Off
}
