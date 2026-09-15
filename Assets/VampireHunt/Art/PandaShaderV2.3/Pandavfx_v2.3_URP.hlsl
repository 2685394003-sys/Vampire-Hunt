#ifndef PANDA_VFX_V23_URP_INCLUDED
#define PANDA_VFX_V23_URP_INCLUDED

// URP runtime implementation for VFX/Pandavfx_v2.3.
// Property names intentionally match the original Built-in/Amplify shader so
// existing materials and the Panda custom inspector keep working unchanged.

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

TEXTURE2D(_MainTex);              SAMPLER(sampler_MainTex);
TEXTURE2D(_AddTex);               SAMPLER(sampler_AddTex);
TEXTURE2D(_MaskTex);              SAMPLER(sampler_MaskTex);
TEXTURE2D(_MaskPlusTex);          SAMPLER(sampler_MaskPlusTex);
TEXTURE2D(_DissloveTex);          SAMPLER(sampler_DissloveTex);
TEXTURE2D(_DissloveTexPlus);      SAMPLER(sampler_DissloveTexPlus);
TEXTURE2D(_DistortTex);           SAMPLER(sampler_DistortTex);
TEXTURE2D(_DistortMaskTex);       SAMPLER(sampler_DistortMaskTex);
TEXTURE2D(_NormalTex);            SAMPLER(sampler_NormalTex);
TEXTURE2D(_ParaTex);              SAMPLER(sampler_ParaTex);
TEXTURE2D(_VTOTex);               SAMPLER(sampler_VTOTex);
TEXTURE2D(_VTOMaskTex);           SAMPLER(sampler_VTOMaskTex);
TEXTURE2D(_VATPositionTex);       SAMPLER(sampler_VATPositionTex);
TEXTURE2D(_VATNormalTex);         SAMPLER(sampler_VATNormalTex);
TEXTURE2D(_texcoord);
TEXTURE2D(_texcoord2);
TEXTURE2D(_texcoord3);
TEXTURE2D(_texcoord4);
TEXTURECUBE(_CubeMap);            SAMPLER(sampler_CubeMap);

CBUFFER_START(UnityPerMaterial)
    float _Cullmode;
    float _Ztest;
    float _Scr;
    float _Dst;
    float _Zwrite;
    float _Reference;
    float _Pass;
    float _Comparison;
    float _Fail;
    float _StencilStyle;
    float __dirty;

    float4 _MainTex_ST;
    float4 _AddTex_ST;
    float4 _MaskTex_ST;
    float4 _MaskPlusTex_ST;
    float4 _DissloveTex_ST;
    float4 _DissloveTexPlus_ST;
    float4 _DistortTex_ST;
    float4 _DistortMaskTex_ST;
    float4 _NormalTex_ST;
    float4 _ParaTex_ST;
    float4 _VTOTex_ST;
    float4 _VTOMaskTex_ST;
    float4 _VATPositionTex_TexelSize;

    float4 _MainColor;
    float4 _AddTexColor;
    float4 _DIssloveColor;
    float4 _fnl_color;
    float4 _DepthColor;
    float4 _BackFaceColor;
    float4 _MainTexRefine;
    float4 _AddTexRefine;
    float4 _TexCenter;
    float4 _Dir;

    float _MainAlpha;
    float _MainTexUVS;
    float _Face;
    float _CenterU;
    float _CenterV;
    float _MainTex_Uspeed;
    float _MainTex_Vspeed;
    float _MainTex_rotat;
    float _MainTexAC;
    float _MainTex_ar;
    float _MaintexC;
    float _MaintexCV;
    float _ScreenAsMain;
    float _CustomdataMainTexUV;
    float _MainOffsetUC1;
    float _MainOffsetUC2;
    float _MainOffsetVC1;
    float _MainOffsetVC2;

    float _IfAddTex;
    float _AddTexUspeed;
    float _AddTexVspeed;
    float _AddTexRo;
    float _AddTexAC;
    float _AddTexAR;
    float _AddTexC;
    float _AddTexCV;
    float _AddTexUVS;
    float _AddTexBlend;
    float _AddTexBlendMode;
    float _IfAddTexAlpha;
    float _IfAddTexColor;
    float _AlphaAdd;
    float _CAddTexUV;
    float _CAddTexUVT;
    float _DistortAddTex;

    float _Mask_scale;
    float _MaskAlphaRA;
    float _Mask_Uspeed;
    float _Mask_Vspeed;
    float _Mask_rotat;
    float _MaskC;
    float _MaskCV;
    float _MaskTexUVS;
    float _IfMaskColor;
    float _CustomdataMaskUV;
    float _MaskOffsetUC1;
    float _MaskOffsetUC2;
    float _MaskOffsetVC1;
    float _MaskOffsetVC2;
    float _DistortMask;
    float _IfMaskPlusTex;
    float _MaskPlusAR;
    float _MaskPlusC;
    float _MaskPlusCV;
    float _MaskPlusUspeed;
    float _MaskPlusVspeed;
    float _MaskPlusR;

    float _DIssloveFactor;
    float _DIssloveWide;
    float _DIssloveSoft;
    float _DisTex_Uspeed;
    float _DisTex_Vspeed;
    float _DIssolve_rotat;
    float _DissolveAR;
    float _DissolveC;
    float _DissolveCV;
    float _DissolveTexUVS2;
    float _DissolveTexExp;
    float _DissolveTexDivide;
    float _IfDissolvePlus;
    float _DissolvePlusAR;
    float _DissolvePlusC;
    float _DissolvePlusCV;
    float _DissolvePlusR;
    float _IfDissolveColor;
    float _sot_sting_A;
    float _soft_sting;
    float _CustomdataDis;
    float _CustomdataDisT;
    float _DissolveFactorC1;
    float _DissolveFactorC2;
    float _IfDissolveOffsetC;
    float _DissolveOffsetUC1;
    float _DissolveOffsetUC2;
    float _DissolveOffsetVC1;
    float _DissolveOffsetVC2;
    float _DistortDisTex;

    float _DistortFactor;
    float _DistortTex_Uspeed;
    float _DistortTex_Vspeed;
    float _DistortTexAR;
    float _DistortMaskTexAR;
    float _DistortMaskTexR;
    float _DistortMaskTexC;
    float _DistortMaskTexCV;
    float _DistortRemap;
    float _IfFlowmap;
    float _IfNormalDistort;
    float _CustomDistort;
    float _DistortFactorC1;
    float _DistortFactorC2;
    float _DistortMainTex;
    float _DistortNormalTex;

    float _fnl_power;
    float _fnl_sacle;
    float _softFacotr;
    float _FNLfanxiangkaiguan;
    float _IfFNLAlpha;
    float _softback;
    float _Depthfadeon;
    float _DepthfadeFactor;
    float _DepthF;
    float _qubaohedu;

    float _IfPara;
    float _Parallax;
    float _IfCustomLight;
    float _LightScale;
    float _NormalScale;
    float _NormalTex_Uspeed;
    float _NormalTex_Vspeed;
    float _NormalTex_Rotat;
    float _NormalTexC;
    float _NormalTexCV;
    float _IfStaticNormal;
    float _StaticNormalScale;
    float _StaticNormalOffset;
    float _IfCubemap;
    float _CubemapScale;

    float _VTOFactor;
    float _VTOTex_Uspeed;
    float _VTOTex_Vspeed;
    float _VTOR;
    float _VTOAR;
    float _VTOC;
    float _VTOCV;
    float _VTOTexExp;
    float _VTOMaskAR;
    float _VTOMaskC;
    float _VTOMaskCV;
    float _VTOMaskR;
    float _VTORemap;
    float _ToggleSwitch0;
    float _VTOFactorC1;
    float _VTOFactorC2;
    float _screenVTOon;

    float _IfVAT;
    float _VATFrameFactor;
    float _VATTime;
    float _CustomVAT;
    float _ParticleVAT;
    float _VATFrameC1;
    float _VATFrameC2;
CBUFFER_END

struct PandaAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float4 tangentOS  : TANGENT;
    float4 color      : COLOR;
    float4 texcoord0  : TEXCOORD0;
    float4 texcoord1  : TEXCOORD1;
    float4 texcoord2  : TEXCOORD2;
    float4 texcoord3  : TEXCOORD3;
    float4 texcoord4  : TEXCOORD4;
    float2 texcoord7  : TEXCOORD7;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct PandaVaryings
{
    float4 positionCS : SV_POSITION;
    float4 color : COLOR;
    float4 uv0AndCustom : TEXCOORD0;
    float4 customOneTailAndTwoHead : TEXCOORD1;
    float4 customTwoTailAndUv2 : TEXCOORD2;
    float3 positionWS : TEXCOORD3;
    half3 normalWS : TEXCOORD4;
    half4 tangentWS : TEXCOORD5;
    float3 positionOS : TEXCOORD6;
    float4 screenPos : TEXCOORD7;
    float eyeDepth : TEXCOORD8;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

static const float PANDA_TWO_PI = 6.28318530718;

// Supplied by URP while rendering the ShadowCaster pass.
float3 _LightDirection;
float3 _LightPosition;

float PandaSelectCustom(float4 custom1, float4 custom2, float source, float component)
{
    int channel = (int)round(component);
    if (channel < 0 || channel > 3)
        return 0.0;

    float4 data = source < 0.5 ? custom1 : custom2;
    return channel == 0 ? data.x : (channel == 1 ? data.y : (channel == 2 ? data.z : data.w));
}

float2 PandaRotateUv(float2 uv, float degrees)
{
    float angle = radians(degrees);
    float s;
    float c;
    sincos(angle, s, c);
    uv -= 0.5;
    uv = mul(uv, float2x2(c, -s, s, c));
    return uv + 0.5;
}

float2 PandaClampUv(float2 uv, float clampU, float clampV)
{
    if (clampU > 0.5) uv.x = saturate(uv.x);
    if (clampV > 0.5) uv.y = saturate(uv.y);
    return uv;
}

float PandaSafeRatio(float numerator, float denominator)
{
    float safeDenominator = abs(denominator) < 1e-5 ? (denominator < 0.0 ? -1e-5 : 1e-5) : denominator;
    return numerator / safeDenominator;
}

float2 PandaCylinderUv(float3 positionOS)
{
    float3 p = positionOS + _TexCenter.xyz;
    float angular;
    float axial;
    if (_Face < 0.5)
    {
        angular = atan(PandaSafeRatio(p.y, p.z));
        axial = p.x;
    }
    else if (_Face < 1.5)
    {
        angular = atan(PandaSafeRatio(p.x, p.z));
        axial = p.y;
    }
    else
    {
        angular = atan(PandaSafeRatio(p.x, p.y));
        axial = p.z;
    }
    return float2(angular / PI + 0.5, axial);
}

float2 PandaBuildUv(float2 uv0, float2 uv2, float3 positionOS, float4 st, float mode)
{
    if (mode > 2.5)
        return uv2 * st.xy + st.zw;

    if (mode > 1.5)
        return PandaCylinderUv(positionOS) * st.xy + st.zw;

    if (mode > 0.5)
    {
        float2 centered = uv0 - float2(_CenterU, _CenterV);
        return float2(length(centered) * st.x * 2.0,
                      atan2(centered.x, centered.y) / PANDA_TWO_PI * st.y) + st.zw;
    }

    return uv0 * st.xy + st.zw;
}

float4 PandaChromaticSample(TEXTURE2D_PARAM(tex, samp), float2 uv, float amount)
{
    float4 center = SAMPLE_TEXTURE2D(tex, samp, uv);
    float4 positive = SAMPLE_TEXTURE2D(tex, samp, uv + amount.xx);
    float4 negative = SAMPLE_TEXTURE2D(tex, samp, uv - amount.xx);
    return float4(positive.r, center.g, negative.b, (positive.a + center.a + negative.a) / 3.0);
}

float3 PandaRefine(float3 color, float alpha, float4 refine, float multiplyAlpha)
{
    float3 inputColor = multiplyAlpha > 0.5 ? color * alpha : color;
    return lerp(inputColor * refine.x,
                pow(max(inputColor, 0.0), max(refine.z, 0.01).xxx) * refine.y,
                refine.w);
}

float2 PandaParallaxUv(float2 uv, float3 viewDirTS)
{
    if (_IfPara < 0.5 || _Parallax <= 0.0001)
        return uv;

    const int stepCount = 64;
    float safeViewZ = abs(viewDirTS.z) < 0.05 ? (viewDirTS.z < 0.0 ? -0.05 : 0.05) : viewDirTS.z;
    float2 plane = (_Parallax * 0.1) * viewDirTS.xy / safeViewZ;
    float layerHeight = rcp((float)stepCount);
    float2 deltaUv = -plane * layerHeight;
    float2 currentOffset = deltaUv;
    float rayHeight = 1.0 - layerHeight;
    float sampledHeight = 0.0;

    [loop]
    for (int stepIndex = 0; stepIndex < stepCount; ++stepIndex)
    {
        sampledHeight = SAMPLE_TEXTURE2D_GRAD(_ParaTex, sampler_ParaTex,
                                              uv + currentOffset, ddx(uv), ddy(uv)).r;
        if (sampledHeight > rayHeight)
            break;
        currentOffset += deltaUv;
        rayHeight -= layerHeight;
    }

    return uv + currentOffset;
}

float3 PandaRotateAroundAxis(float3 value, float3 axis, float angle)
{
    axis = normalize(axis);
    float s;
    float c;
    sincos(angle, s, c);
    return value * c + cross(axis, value) * s + axis * dot(axis, value) * (1.0 - c);
}

void PandaApplyVertexAnimation(in PandaAttributes input, inout float3 positionOS, inout float3 normalOS)
{
    float4 custom1 = float4(input.texcoord0.zw, input.texcoord1.xy);
    float4 custom2 = float4(input.texcoord1.zw, input.texcoord2.xy);

    if (_IfVAT > 0.5)
    {
        float frame = _CustomVAT > 0.5
            ? PandaSelectCustom(custom1, custom2, _VATFrameC1, _VATFrameC2)
            : _VATFrameFactor;
        float frameCount = max(_VATTime, 1.0);
        float2 vatUv = float2(_VATPositionTex_TexelSize.x * (frameCount - 1.0) * saturate(frame) + input.texcoord7.x,
                              input.texcoord7.y);
        float3 vatPosition = SAMPLE_TEXTURE2D_LOD(_VATPositionTex, sampler_VATPositionTex, vatUv, 0).rgb;
        float3 vatNormal = SAMPLE_TEXTURE2D_LOD(_VATNormalTex, sampler_VATNormalTex, vatUv, 0).rgb;

        if (_ParticleVAT > 0.5)
        {
            float3 radial = float3(input.texcoord2.z, 0.0, input.texcoord3.x);
            float radialLength = max(length(radial), 1e-5);
            float yaw = atan(PandaSafeRatio(input.texcoord2.z, input.texcoord3.x));
            float3 rotated = PandaRotateAroundAxis(vatPosition, float3(1, 0, 0), input.texcoord3.y);
            float3 localAxis = PandaRotateAroundAxis(float3(0, 0, 1), float3(1, 0, 0), input.texcoord3.y);
            rotated = PandaRotateAroundAxis(rotated, localAxis, input.texcoord3.w);
            rotated = PandaRotateAroundAxis(rotated, float3(0, 1, 0), input.texcoord3.x >= 0.0 ? yaw : yaw + PI);
            float3 tiltAxis = PandaRotateAroundAxis(radial, float3(0, 1, 0), 0.5 * PI);
            rotated = PandaRotateAroundAxis(rotated, tiltAxis, -atan(input.texcoord2.w / radialLength));
            vatPosition = input.texcoord4.xyz * rotated;
        }

        if (_screenVTOon < 0.5)
            positionOS += vatPosition;
        normalOS = normalize(vatNormal * 2.0 - 1.0);
        return;
    }

    float2 vtoUv = PandaBuildUv(input.texcoord0.xy, input.texcoord3.xy, positionOS, _VTOTex_ST, _MainTexUVS);
    vtoUv += _Time.y * float2(_VTOTex_Uspeed, _VTOTex_Vspeed);
    vtoUv = PandaClampUv(PandaRotateUv(vtoUv, _VTOR), _VTOC, _VTOCV);
    float4 vtoSample = SAMPLE_TEXTURE2D_LOD(_VTOTex, sampler_VTOTex, vtoUv, 0);
    float vtoValue = saturate(pow(max(_VTOAR < 0.5 ? vtoSample.a : vtoSample.r, 0.0), _VTOTexExp));
    if (_VTORemap > 0.5)
        vtoValue -= 0.5;

    float2 maskUv = PandaBuildUv(input.texcoord0.xy, input.texcoord3.xy, positionOS, _VTOMaskTex_ST, _MainTexUVS);
    maskUv = PandaClampUv(PandaRotateUv(maskUv, _VTOMaskR), _VTOMaskC, _VTOMaskCV);
    float4 maskSample = SAMPLE_TEXTURE2D_LOD(_VTOMaskTex, sampler_VTOMaskTex, maskUv, 0);
    float mask = _VTOMaskAR < 0.5 ? maskSample.a : maskSample.r;
    float factor = _ToggleSwitch0 > 0.5
        ? PandaSelectCustom(custom1, custom2, _VTOFactorC1, _VTOFactorC2)
        : _VTOFactor;

    if (_screenVTOon < 0.5)
        positionOS += normalOS * (vtoValue * mask * factor);
}

PandaVaryings PandaVfxVert(PandaAttributes input)
{
    PandaVaryings output = (PandaVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    float3 positionOS = input.positionOS.xyz;
    float3 normalOS = input.normalOS;
    PandaApplyVertexAnimation(input, positionOS, normalOS);

    VertexPositionInputs positionInputs = GetVertexPositionInputs(positionOS);
    VertexNormalInputs normalInputs = GetVertexNormalInputs(normalOS, input.tangentOS);

    output.positionCS = positionInputs.positionCS;
    output.positionWS = positionInputs.positionWS;
    output.positionOS = positionOS;
    output.normalWS = normalInputs.normalWS;
    output.tangentWS = half4(normalInputs.tangentWS, input.tangentOS.w * GetOddNegativeScale());
    output.color = input.color;
    output.uv0AndCustom = input.texcoord0;
    output.customOneTailAndTwoHead = input.texcoord1;
    output.customTwoTailAndUv2 = float4(input.texcoord2.xy, input.texcoord3.xy);
    output.screenPos = ComputeScreenPos(positionInputs.positionCS);
    output.eyeDepth = -TransformWorldToView(positionInputs.positionWS).z;
    return output;
}

PandaVaryings PandaVfxShadowVert(PandaAttributes input)
{
    PandaVaryings output = PandaVfxVert(input);
    float3 lightDirectionWS = _LightDirection;
#if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
    lightDirectionWS = normalize(_LightPosition - output.positionWS);
#endif
    output.positionCS = TransformWorldToHClip(
        ApplyShadowBias(output.positionWS, normalize(output.normalWS), lightDirectionWS));
#if UNITY_REVERSED_Z
    output.positionCS.z = min(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
#else
    output.positionCS.z = max(output.positionCS.z, UNITY_NEAR_CLIP_VALUE);
#endif
    return output;
}

float2 PandaGetDistortion(PandaVaryings input, float3 viewDirTS, float4 custom1, float4 custom2)
{
    float2 uv0 = input.uv0AndCustom.xy;
    float2 uv2 = input.customTwoTailAndUv2.zw;

    float2 maskUv = PandaBuildUv(uv0, uv2, input.positionOS, _DistortMaskTex_ST, _MainTexUVS);
    maskUv = PandaClampUv(PandaRotateUv(maskUv, _DistortMaskTexR), _DistortMaskTexC, _DistortMaskTexCV);
    float4 distortionMaskSample = SAMPLE_TEXTURE2D(_DistortMaskTex, sampler_DistortMaskTex, maskUv);
    float distortionMask = _DistortMaskTexAR < 0.5 ? distortionMaskSample.a : distortionMaskSample.r;
    float factor = _CustomDistort < 0.5
        ? _DistortFactor
        : PandaSelectCustom(custom1, custom2, _DistortFactorC1, _DistortFactorC2);
    factor *= distortionMask;

    float2 distortUv = PandaBuildUv(uv0, uv2, input.positionOS, _DistortTex_ST, _MainTexUVS);
    distortUv += _Time.y * float2(_DistortTex_Uspeed, _DistortTex_Vspeed);
    float4 distortionSample = SAMPLE_TEXTURE2D(_DistortTex, sampler_DistortTex, distortUv);

    if (_IfFlowmap > 0.5)
        return factor * (distortionSample.rg - uv0);

    if (_IfNormalDistort > 0.5)
        return (distortionSample.rg * 2.0 - 1.0) * factor;

    float scalar = _DistortTexAR < 0.5 ? distortionSample.a : distortionSample.r;
    if (_DistortRemap > 0.5)
        scalar -= 0.5;
    return (factor * scalar).xx;
}

float PandaDissolveNoise(PandaVaryings input, float3 viewDirTS, float2 distortion, float4 custom1, float4 custom2,
                         out float dissolveSmooth, out float dissolveHard, out float edgeBand, out float2 dissolveUv)
{
    float2 uv0 = input.uv0AndCustom.xy;
    float2 uv2 = input.customTwoTailAndUv2.zw;
    float2 customOffset = _IfDissolveOffsetC > 0.5
        ? float2(PandaSelectCustom(custom1, custom2, _DissolveOffsetUC1, _DissolveOffsetUC2),
                 PandaSelectCustom(custom1, custom2, _DissolveOffsetVC1, _DissolveOffsetVC2))
        : 0.0.xx;

    dissolveUv = PandaBuildUv(uv0, uv2, input.positionOS, _DissloveTex_ST, _DissolveTexUVS2);
    dissolveUv = PandaParallaxUv(dissolveUv, viewDirTS);
    dissolveUv += _Time.y * float2(_DisTex_Uspeed, _DisTex_Vspeed) + customOffset;
    if (_DistortDisTex > 0.5)
        dissolveUv += distortion;
    dissolveUv = PandaClampUv(PandaRotateUv(dissolveUv, _DIssolve_rotat), _DissolveC, _DissolveCV);

    float4 dissolveSample = SAMPLE_TEXTURE2D(_DissloveTex, sampler_DissloveTex, dissolveUv);
    float baseNoise = saturate(pow(max(_DissolveAR < 0.5 ? dissolveSample.a : dissolveSample.r, 0.0), _DissolveTexExp));

    float2 plusUv = PandaBuildUv(uv0, uv2, input.positionOS, _DissloveTexPlus_ST, _DissolveTexUVS2);
    plusUv = PandaParallaxUv(plusUv, viewDirTS);
    plusUv = PandaClampUv(PandaRotateUv(plusUv, _DissolvePlusR), _DissolvePlusC, _DissolvePlusCV);
    float4 plusSample = SAMPLE_TEXTURE2D(_DissloveTexPlus, sampler_DissloveTexPlus, plusUv);
    float plusNoise = _DissolvePlusAR < 0.5 ? plusSample.a : plusSample.r;

    float combinedNoise = saturate((baseNoise / max(_DissolveTexDivide, 0.0001)
                                  + (_IfDissolvePlus > 0.5 ? plusNoise : baseNoise)) * 0.5);
    float dissolveFactor = _CustomdataDis < 0.5
        ? _DIssloveFactor + 0.001
        : (_CustomdataDisT < 0.5
            ? PandaSelectCustom(custom1, custom2, _DissolveFactorC1, _DissolveFactorC2)
            : 1.0 - input.color.a);
    float threshold = (1.0 + _DIssloveSoft) * dissolveFactor;
    dissolveSmooth = smoothstep(threshold - _DIssloveSoft, threshold, combinedNoise);
    dissolveHard = step(dissolveFactor * (1.0 + _DIssloveWide) - _DIssloveWide, combinedNoise);
    edgeBand = max(0.0, dissolveHard - step(dissolveFactor * (1.0 + _DIssloveWide), combinedNoise));
    return combinedNoise;
}

float3 PandaStaticDissolveNormal(float2 dissolveUv, float centerAlpha)
{
    if (_IfStaticNormal < 0.5 || _StaticNormalScale <= 0.0001)
        return float3(0, 0, 1);

    float offset = pow(_StaticNormalOffset, 3.0) * 0.1;
    float4 sampleX = SAMPLE_TEXTURE2D(_DissloveTex, sampler_DissloveTex, dissolveUv + float2(offset, 0));
    float4 sampleY = SAMPLE_TEXTURE2D(_DissloveTex, sampler_DissloveTex, dissolveUv + float2(0, offset));
    float x = (_DissolveAR < 0.5 ? sampleX.a : sampleX.r) - centerAlpha;
    float y = (_DissolveAR < 0.5 ? sampleY.a : sampleY.r) - centerAlpha;
    return normalize(float3(-x * _StaticNormalScale * 10.0, -y * _StaticNormalScale * 10.0, 1.0));
}

half4 PandaVfxFrag(PandaVaryings input, FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    float4 custom1 = float4(input.uv0AndCustom.zw, input.customOneTailAndTwoHead.xy);
    float4 custom2 = float4(input.customOneTailAndTwoHead.zw, input.customTwoTailAndUv2.xy);
    float2 uv0 = input.uv0AndCustom.xy;
    float2 uv2 = input.customTwoTailAndUv2.zw;

    half3 normalWS = normalize(input.normalWS);
    half3 tangentWS = normalize(input.tangentWS.xyz);
    half3 bitangentWS = normalize(cross(normalWS, tangentWS) * input.tangentWS.w);
    float3 viewDirWS = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
    float3 viewDirTS = float3(dot(viewDirWS, tangentWS), dot(viewDirWS, bitangentWS), dot(viewDirWS, normalWS));

    float2 distortion = PandaGetDistortion(input, viewDirTS, custom1, custom2);

    float2 maskUv = PandaBuildUv(uv0, uv2, input.positionOS, _MaskTex_ST, _MaskTexUVS);
    maskUv = PandaParallaxUv(maskUv, viewDirTS);
    if (_DistortMask > 0.5) maskUv += distortion;
    if (_CustomdataMaskUV > 0.5)
    {
        maskUv += float2(PandaSelectCustom(custom1, custom2, _MaskOffsetUC1, _MaskOffsetUC2),
                         PandaSelectCustom(custom1, custom2, _MaskOffsetVC1, _MaskOffsetVC2));
    }
    maskUv += _Time.y * float2(_Mask_Uspeed, _Mask_Vspeed);
    maskUv = PandaClampUv(PandaRotateUv(maskUv, _Mask_rotat), _MaskC, _MaskCV);
    float4 maskSample = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, maskUv);
    float maskAlpha = _MaskAlphaRA < 0.5 ? maskSample.a : maskSample.r;

    float2 maskPlusUv = PandaBuildUv(uv0, uv2, input.positionOS, _MaskPlusTex_ST, _MaskTexUVS);
    if (_DistortMask > 0.5) maskPlusUv += distortion;
    maskPlusUv += _Time.y * float2(_MaskPlusUspeed, _MaskPlusVspeed);
    maskPlusUv = PandaClampUv(PandaRotateUv(maskPlusUv, _MaskPlusR), _MaskPlusC, _MaskPlusCV);
    float4 maskPlusSample = SAMPLE_TEXTURE2D(_MaskPlusTex, sampler_MaskPlusTex, maskPlusUv);
    float maskPlus = _MaskPlusAR < 0.5 ? maskPlusSample.a : maskPlusSample.r;
    maskAlpha *= _Mask_scale * (_IfMaskPlusTex > 0.5 ? maskPlus : 1.0);

    float2 customMainOffset = _CustomdataMainTexUV > 0.5
        ? float2(PandaSelectCustom(custom1, custom2, _MainOffsetUC1, _MainOffsetUC2),
                 PandaSelectCustom(custom1, custom2, _MainOffsetVC1, _MainOffsetVC2))
        : 0.0.xx;
    float2 mainUv = PandaBuildUv(uv0, uv2, input.positionOS, _MainTex_ST, _MainTexUVS);
    mainUv = PandaParallaxUv(mainUv, viewDirTS) + customMainOffset;
    if (_DistortMainTex > 0.5) mainUv += distortion;
    mainUv += _Time.y * float2(_MainTex_Uspeed, _MainTex_Vspeed);
    mainUv = PandaClampUv(PandaRotateUv(mainUv, _MainTex_rotat), _MaintexC, _MaintexCV);
    float4 mainSample = PandaChromaticSample(TEXTURE2D_ARGS(_MainTex, sampler_MainTex), mainUv, _MainTexAC);

    float2 screenUv = GetNormalizedScreenSpaceUV(input.positionCS);
    float2 sceneUv = screenUv * _MainTex_ST.xy + _MainTex_ST.zw + customMainOffset;
    if (_DistortMainTex > 0.5) sceneUv += distortion;
    float3 sceneColor = SampleSceneColor(saturate(sceneUv));
    float4 mainSource = _ScreenAsMain > 0.5 ? float4(sceneColor, 1.0) : mainSample;
    float mainTextureAlpha = _MainTex_ar < 0.5 ? mainSource.a : mainSource.r;
    float vertexAlpha = _CustomdataDisT < 0.5 ? input.color.a : 1.0;
    float mainAlpha = vertexAlpha * mainTextureAlpha * _MainColor.a;

    float2 addUv = PandaBuildUv(uv0, uv2, input.positionOS, _AddTex_ST, _AddTexUVS);
    addUv = PandaParallaxUv(addUv, viewDirTS);
    if (_DistortAddTex > 0.5) addUv += distortion;
    if (_CAddTexUV > 0.5)
    {
        float2 scale = _CAddTexUVT > 0.5
            ? _AddTex_ST.xy / max(abs(_MainTex_ST.xy), 0.0001.xx)
            : 1.0.xx;
        addUv += customMainOffset * scale;
    }
    addUv += _Time.y * float2(_AddTexUspeed, _AddTexVspeed);
    addUv = PandaClampUv(PandaRotateUv(addUv, _AddTexRo), _AddTexC, _AddTexCV);
    float4 addSample = PandaChromaticSample(TEXTURE2D_ARGS(_AddTex, sampler_AddTex), addUv, _AddTexAC);
    float addAlpha = _AddTexAR < 0.5 ? addSample.a : addSample.r;

    if (_IfAddTexAlpha > 0.5)
    {
        if (_AddTexBlendMode > 1.5) mainAlpha *= addAlpha;
        else if (_AddTexBlendMode > 0.5) mainAlpha += addAlpha;
        else mainAlpha = lerp(mainAlpha, addAlpha, _AddTexBlend);
    }
    mainAlpha *= _MainAlpha;

    float dissolveSmooth;
    float dissolveHard;
    float edgeBand;
    float2 dissolveUv;
    float dissolveNoise = PandaDissolveNoise(input, viewDirTS, distortion, custom1, custom2,
                                             dissolveSmooth, dissolveHard, edgeBand, dissolveUv);
    float dissolveAlpha = _sot_sting_A > 0.5 ? dissolveHard : dissolveSmooth;

    float facing = dot(viewDirWS, normalWS);
    float softEdge = pow(saturate(_softback > 0.5 ? facing : abs(facing)), _softFacotr);
    float fresnel = saturate(pow(1.0 - saturate(abs(dot(normalWS, SafeNormalize(viewDirWS + _Dir.xyz)))),
                                 _fnl_power) * _fnl_sacle);

    float sceneRawDepth = SampleSceneDepth(screenUv);
    float sceneEyeDepth = LinearEyeDepth(sceneRawDepth, _ZBufferParams);
    float depthFade = saturate((sceneEyeDepth - input.eyeDepth) / max(_DepthfadeFactor, 0.0001));
    float intersection = 1.0 - depthFade;

    float alpha = maskAlpha * mainAlpha * dissolveAlpha;
    if (_FNLfanxiangkaiguan > 0.5) alpha *= softEdge;
    if (_Depthfadeon > 0.5 && _DepthF < 0.5) alpha *= depthFade;
    if (_IfFNLAlpha > 0.5) alpha *= fresnel;
    if (_DepthF > 0.5) alpha += intersection;
    alpha = saturate(alpha);

    float3 mainRgb = PandaRefine(mainSource.rgb, mainTextureAlpha, _MainTexRefine, _AlphaAdd);
    float4 mainColor = _MainColor * float4(mainRgb, 1.0);
    if (_IfMaskColor > 0.5) mainColor *= maskSample;

    if (_IfAddTex > 0.5 && _IfAddTexColor > 0.5)
    {
        float3 addRgb = PandaRefine(addSample.rgb, addAlpha, _AddTexRefine, 1.0);
        float4 addColor = _AddTexColor * float4(addRgb, 1.0);
        if (_AddTexBlendMode > 1.5) mainColor *= addColor;
        else if (_AddTexBlendMode > 0.5) mainColor += addColor;
        else mainColor = lerp(mainColor, addColor, _AddTexBlend);
    }

    float4 dissolveTint = lerp(mainColor, _DIssloveColor, _DIssloveColor.a);
    float4 dissolveColor;
    if (_soft_sting > 0.5)
        dissolveColor = lerp(mainColor, dissolveTint * edgeBand, edgeBand);
    else
        dissolveColor = lerp(dissolveTint,
                             (_IfDissolveColor < 0.5 ? input.color : 1.0.xxxx) * mainColor,
                             dissolveSmooth);
    dissolveColor *= _IfDissolveColor < 0.5 ? 1.0.xxxx : input.color;

    float2 normalUv = PandaBuildUv(uv0, uv2, input.positionOS, _NormalTex_ST, _MainTexUVS);
    normalUv = PandaParallaxUv(normalUv, viewDirTS);
    if (_DistortNormalTex > 0.5) normalUv += distortion;
    normalUv += _Time.y * float2(_NormalTex_Uspeed, _NormalTex_Vspeed);
    normalUv = PandaClampUv(PandaRotateUv(normalUv, _NormalTex_Rotat), _NormalTexC, _NormalTexCV);
    float3 sampledNormalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalTex, sampler_NormalTex, normalUv), _NormalScale);
    float3 staticNormalTS = PandaStaticDissolveNormal(dissolveUv, dissolveNoise);
    float3 combinedNormalTS = normalize(float3(sampledNormalTS.xy + staticNormalTS.xy,
                                               sampledNormalTS.z * staticNormalTS.z));
    float3 shadedNormalWS = normalize(combinedNormalTS.x * tangentWS
                                   + combinedNormalTS.y * bitangentWS
                                   + combinedNormalTS.z * normalWS);

    float3 cubeColor = 0.0;
    if (_IfCubemap > 0.5)
    {
        float3 reflection = reflect(-viewDirWS, shadedNormalWS);
        cubeColor = SAMPLE_TEXTURECUBE(_CubeMap, sampler_CubeMap, reflection).rgb * _CubemapScale * _LightScale;
    }

    float3 color = dissolveColor.rgb + fresnel * _fnl_color.rgb * input.color.rgb + cubeColor;
    if (_DepthF > 0.5) color += intersection * _DepthColor.rgb;
    if (_AlphaAdd > 0.5) color *= alpha;

    if (_IfCustomLight > 0.5)
    {
        Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
        float ndotl = saturate(dot(shadedNormalWS, mainLight.direction));
        float3 halfDirection = SafeNormalize(viewDirWS + mainLight.direction);
        float specular = pow(saturate(dot(shadedNormalWS, halfDirection)), 12.8);
        float3 ambient = SampleSH(shadedNormalWS);
        float3 lighting = ambient + mainLight.color * mainLight.shadowAttenuation * mainLight.distanceAttenuation
                                    * (ndotl + specular);
        color *= max(lighting * _LightScale, 0.0);
    }

    bool isFrontFace = IS_FRONT_VFACE(frontFace, true, false);
    if (!isFrontFace) color *= _BackFaceColor.rgb;

    float luminance = dot(color, float3(0.299, 0.587, 0.114));
    color = lerp(color, luminance.xxx, _qubaohedu);
    return half4(color, alpha);
}

half4 PandaVfxShadowFrag(PandaVaryings input) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    float2 uv = PandaBuildUv(input.uv0AndCustom.xy, input.customTwoTailAndUv2.zw,
                             input.positionOS, _MainTex_ST, _MainTexUVS);
    uv += _Time.y * float2(_MainTex_Uspeed, _MainTex_Vspeed);
    uv = PandaClampUv(PandaRotateUv(uv, _MainTex_rotat), _MaintexC, _MaintexCV);
    float4 sampleValue = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
    float textureAlpha = _MainTex_ar < 0.5 ? sampleValue.a : sampleValue.r;
    clip(textureAlpha * input.color.a * _MainColor.a * _MainAlpha - 0.001);
    return 0;
}

#endif
