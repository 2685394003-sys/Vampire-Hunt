// 复刻 Blender 材质 '材质.001' 的节点图（内核 / 无人机核心 / 立方体.003 在用）。
// 节点链：
//   MATH(MULTIPLY: 5.0 × 0.8 = 4.0) → VORONOI(Vector 未连接 ⇒ 用 Generated 坐标)
//   VORONOI(Feature=F1, Distance=EUCLIDEAN, normalize=false, Randomness=1.0)
//     → COLORRAMP(LINEAR: (0.9745,0.1865,0.2044)@0.209 → (1.0,0.005,0.0002)@0.927)
//     → Principled.Base Color
//   Principled: Metallic 0.8318 / Roughness 0.8818 / Transmission Weight 0.4591 / Alpha 1.0
//   注意：这个材质的 Alpha 是固定 1.0，半透感来自 Transmission ⇒ 这里用“整体透明度”近似。
Shader "VampireHunt/BloodCore"
{
    Properties
    {
        _BaseColor ("Base Color（当 Voronoi 关闭时使用）", Color) = (0.99, 0.10, 0.10, 1)
        _UseVoronoi ("使用 Voronoi 驱动 BaseColor（Blender 里是开的）", Range(0, 1)) = 1
        _Metallic ("Metallic（Blender=0.8318）", Range(0, 1)) = 0.8318
        _Smoothness ("Smoothness（Blender=1-0.8818）", Range(0, 1)) = 0.1182
        _SpecularStrength ("高光强度", Range(0, 4)) = 0.6

        _VoronoiScale ("Voronoi Scale（Blender: 5.0 × 0.8 = 4.0）", Float) = 4.0
        _VoronoiRandomness ("Voronoi Randomness", Range(0, 1)) = 1.0
        _RampColor0 ("ColorRamp 色标0（0.209）", Color) = (0.9745, 0.1865, 0.2044, 1)
        _RampColor1 ("ColorRamp 色标1（0.927）", Color) = (1.0, 0.005, 0.0002, 1)
        _RampPos0 ("ColorRamp Pos0", Range(0, 1)) = 0.209
        _RampPos1 ("ColorRamp Pos1", Range(0, 1)) = 0.927

        _FresnelPower ("Fresnel Power", Range(0.2, 8)) = 6
        _FresnelBias ("Fresnel Bias（正面不透明度）", Range(0, 1)) = 0.85
        _Alpha ("整体透明度（近似 Transmission 0.459）", Range(0, 1)) = 0.92

        _BoundsMin ("物体包围盒 Min", Vector) = (-0.5,-0.5,-0.5,0)
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
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                float  fogFactor  : TEXCOORD3;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float  _UseVoronoi;
                float  _Metallic;
                float  _Smoothness;
                float  _SpecularStrength;
                float  _VoronoiScale;
                float  _VoronoiRandomness;
                float4 _RampColor0;
                float4 _RampColor1;
                float  _RampPos0;
                float  _RampPos1;
                float  _FresnelPower;
                float  _FresnelBias;
                float  _Alpha;
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
                OUT.positionOS = IN.positionOS.xyz;
                OUT.fogFactor  = ComputeFogFactor(pos.positionCS.z);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float3 normalWS  = normalize(IN.normalWS);
                float3 viewDirWS = normalize(GetWorldSpaceViewDir(IN.positionWS));

                // === Voronoi（Generated 坐标）→ ColorRamp → BaseColor ===
                float3 gen  = (IN.positionOS - _BoundsMin.xyz) / max(_BoundsSize.xyz, 1e-4);
                float  vor  = VH_VoronoiF1(gen * _VoronoiScale, _VoronoiRandomness);
                float3 ramp = VH_ColorRamp2(vor, _RampColor0.rgb, _RampColor1.rgb, _RampPos0, _RampPos1);
                float3 albedo = lerp(_BaseColor.rgb, ramp, saturate(_UseVoronoi));

                // === 光照 ===
                Light  mainLight = GetMainLight();
                float  ndl       = saturate(dot(normalWS, mainLight.direction));
                float3 halfDir   = normalize(mainLight.direction + viewDirWS);
                float  ndh       = saturate(dot(normalWS, halfDir));
                float  specPower = exp2(_Smoothness * 10.0 + 2.0);
                float3 specular  = pow(ndh, specPower) * _Smoothness * _SpecularStrength;
                float3 ambient   = SampleSH(normalWS);
                float3 color     = albedo * (mainLight.color * ndl + ambient) + specular * mainLight.color * lerp(1.0, _Metallic, 0.7);

                // === Fresnel alpha ===
                float fres  = 1.0 - saturate(dot(normalWS, viewDirWS));
                fres = pow(fres, max(0.2, _FresnelPower));
                float alpha = lerp(_FresnelBias, 1.0, fres) * _Alpha;

                color = MixFog(color, IN.fogFactor);
                return half4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }
    FallBack "Universal Render Pipeline/Lit"
}
