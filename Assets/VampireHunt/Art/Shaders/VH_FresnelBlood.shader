// 复刻 Blender 材质 'Material' 的完整节点图（不用任何贴图，纯数学重算）。
// 节点链（按 Blender 的实际连接与参数）：
//   链1 凹凸： TEX_COORD(UV) → MAPPING(scale 1,0.9,2.4 loc 0,0.152,0) → NOISE(13.7/3.2/0.7071/1.5)
//              → BUMP(strength 1, distance 0.401) → Principled.Normal
//   链2 自发光： TEX_COORD(Generated) + NOISE.001(3.4/2.0/0.5/2.0) → MIX(0.07)
//              → MAPPING.001(identity) → VORONOI(F1, 9.8, rand 1.0)
//              → COLORRAMP(0.291黑 → 0.795白) → MATH MINIMUM(0.96) → MATH LOGARITHM(base 0.98)
//              → EMISSION(strength 1.5)
//   合成：     MIX_SHADER(factor 0.9417) = 0.9417*Emission + 0.0583*Principled
//   透明度：   LAYER_WEIGHT(Fresnel, blend 0.62).Fresnel → Principled.Alpha
Shader "VampireHunt/FresnelBlood"
{
    Properties
    {
        [MainColor] _BaseColor ("Base Color（线性，同 Blender）", Color) = (0.8, 0.0014, 0, 1)
        _Metallic ("Metallic", Range(0, 1)) = 0
        _Smoothness ("Smoothness", Range(0, 1)) = 1
        _SpecularStrength ("高光强度", Range(0, 4)) = 1

        _FresnelPower ("Fresnel Power", Range(0.2, 8)) = 4
        _FresnelBias ("Fresnel Bias（正面不透明度）", Range(0, 1)) = 0.38
        _Alpha ("整体透明度", Range(0, 1)) = 1

        [HDR] _EmissionColor ("Emission Color", Color) = (1, 0.12, 0.06, 1)
        _EmissionStrength ("Emission 强度（Blender=1.5）", Range(0, 8)) = 1.5
        _EmissionMix ("Mix Shader 系数（Blender=0.9417）", Range(0, 1)) = 0.9417
        _VoronoiScale ("Voronoi Scale（Blender=9.8）", Float) = 9.8
        _VoronoiRandomness ("Voronoi Randomness", Range(0, 1)) = 1.0
        _EmNoiseScale ("Noise.001 Scale（3.4）", Float) = 3.4
        _EmNoiseDetail ("Noise.001 Detail（2.0）", Float) = 2.0
        _EmNoiseRoughness ("Noise.001 Roughness（0.5）", Range(0.01, 1)) = 0.5
        _EmNoiseLacunarity ("Noise.001 Lacunarity（2.0）", Float) = 2.0
        _MixFactor ("Mix 系数（0.07）", Range(0, 1)) = 0.07
        _RampColor0 ("ColorRamp 色标0（0.291）", Color) = (0, 0, 0, 1)
        _RampColor1 ("ColorRamp 色标1（0.795）", Color) = (1, 1, 1, 1)
        _RampPos0 ("ColorRamp Pos0", Range(0, 1)) = 0.291
        _RampPos1 ("ColorRamp Pos1", Range(0, 1)) = 0.795
        _MathMinClamp ("Math MINIMUM 常数（0.96）", Float) = 0.96
        _MathLogValue ("Math LOGARITHM 常数（0.98）", Float) = 0.98

        _BumpStrength ("Bump Strength（1.0）", Range(0, 4)) = 1.0
        _BumpDistance ("Bump Distance（0.401）", Range(0, 2)) = 0.401
        _BumpNoiseScale ("Noise Scale（13.7）", Float) = 13.7
        _BumpNoiseDetail ("Noise Detail（3.2）", Float) = 3.2
        _BumpNoiseRoughness ("Noise Roughness（0.7071）", Range(0.01, 1)) = 0.7071
        _BumpNoiseLacunarity ("Noise Lacunarity（1.5）", Float) = 1.5
        _BumpMapLoc ("Mapping Location", Vector) = (0, 0.152, 0, 0)
        _BumpMapScale ("Mapping Scale", Vector) = (1, 0.9, 2.4, 0)

        _BoundsMin ("物体包围盒 Min（Generated 坐标用）", Vector) = (-0.5,-0.5,-0.5,0)
        _BoundsSize ("物体包围盒 Size", Vector) = (1,1,1,0)

        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.5
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "VH_BlenderNodes.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float3 positionOS : TEXCOORD3;
                float  fogFactor  : TEXCOORD4;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _Metallic;
                float  _Smoothness;
                float  _SpecularStrength;
                float  _FresnelPower;
                float  _FresnelBias;
                float  _Alpha;
                float4 _EmissionColor;
                float  _EmissionStrength;
                float  _EmissionMix;
                float  _VoronoiScale;
                float  _VoronoiRandomness;
                float  _EmNoiseScale;
                float  _EmNoiseDetail;
                float  _EmNoiseRoughness;
                float  _EmNoiseLacunarity;
                float  _MixFactor;
                float4 _RampColor0;
                float4 _RampColor1;
                float  _RampPos0;
                float  _RampPos1;
                float  _MathMinClamp;
                float  _MathLogValue;
                float  _BumpStrength;
                float  _BumpDistance;
                float  _BumpNoiseScale;
                float  _BumpNoiseDetail;
                float  _BumpNoiseRoughness;
                float  _BumpNoiseLacunarity;
                float4 _BumpMapLoc;
                float4 _BumpMapScale;
                float4 _BoundsMin;
                float4 _BoundsSize;
                float  _Cull;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS   = GetVertexNormalInputs(IN.normalOS).normalWS;
                OUT.uv         = IN.uv;
                OUT.positionOS = IN.positionOS.xyz;
                OUT.fogFactor  = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 normalWS  = normalize(IN.normalWS);
                float3 viewDirWS = normalize(GetWorldSpaceViewDir(IN.positionWS));

                // ================= 链1：凹凸（UV 坐标 → Mapping → Noise → Bump） =================
                float3 pUv   = VH_MappingPoint(float3(IN.uv, 0.0), _BumpMapLoc.xyz, _BumpMapScale.xyz);
                float  hBump = VH_NoiseTexture(pUv, _BumpNoiseScale, _BumpNoiseDetail,
                                               _BumpNoiseRoughness, _BumpNoiseLacunarity);
                // Bump：用高度场的屏幕空间梯度扰动法线（等价于 Blender Bump 节点）
                float3 dpdx = ddx(IN.positionWS);
                float3 dpdy = ddy(IN.positionWS);
                float  dhdx = ddx(hBump);
                float  dhdy = ddy(hBump);
                float3 surfGrad = cross(dpdy, normalWS) * dhdx + cross(normalWS, dpdx) * dhdy;
                float  gradLen  = length(surfGrad);
                if (gradLen > 1e-6)
                {
                    float3 bumpDir = surfGrad / gradLen;
                    normalWS = normalize(normalWS - bumpDir * saturate(gradLen * _BumpDistance * _BumpStrength * 60.0));
                }

                // ================= 链2：自发光（Generated 坐标 → Voronoi → Ramp → Math → Emission） ==========
                float3 gen   = (IN.positionOS - _BoundsMin.xyz) / max(_BoundsSize.xyz, 1e-4);
                float  n2    = VH_NoiseTexture(gen, _EmNoiseScale, _EmNoiseDetail,
                                               _EmNoiseRoughness, _EmNoiseLacunarity);
                float3 mixed = lerp(gen, n2.xxx, _MixFactor);            // MIX(RGBA, factor 0.07)
                float3 emMap = VH_MappingPoint(mixed, 0, 1);             // MAPPING.001 identity
                float  vor   = VH_VoronoiF1(emMap * _VoronoiScale, _VoronoiRandomness);
                float3 ramp  = VH_ColorRamp2(vor, _RampColor0.rgb, _RampColor1.rgb, _RampPos0, _RampPos1);
                float  m1    = min(ramp.r, _MathMinClamp);               // MATH: MINIMUM(0.96)
                float  m2    = saturate(log(max(_MathLogValue, 1e-4)) / log(max(m1, 1e-4))); // MATH: LOGARITHM
                float3 emission = _EmissionColor.rgb * m2 * _EmissionStrength;

                // ================= 光照 =================
                Light  mainLight = GetMainLight();
                float  ndl       = saturate(dot(normalWS, mainLight.direction));
                float3 halfDir   = normalize(mainLight.direction + viewDirWS);
                float  ndh       = saturate(dot(normalWS, halfDir));
                float  specPower = exp2(_Smoothness * 10.0 + 2.0);
                float3 specular  = pow(ndh, specPower) * _Smoothness * _SpecularStrength;
                float3 ambient   = SampleSH(normalWS);
                float3 surface   = _BaseColor.rgb * (mainLight.color * ndl + ambient) + specular * mainLight.color;

                // ================= MIX SHADER =================
                float3 color = lerp(surface, emission, _EmissionMix);

                // ================= 透明度：Layer Weight(Fresnel) → Alpha =================
                float fres  = 1.0 - saturate(dot(normalWS, viewDirWS));
                fres = pow(fres, max(0.2, _FresnelPower));
                float alpha = lerp(_FresnelBias, 1.0, fres) * _BaseColor.a * _Alpha;

                color = MixFog(color, IN.fogFactor);
                return half4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
