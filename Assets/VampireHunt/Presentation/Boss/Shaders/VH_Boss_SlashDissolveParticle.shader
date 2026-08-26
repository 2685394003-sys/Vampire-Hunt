Shader "VampireHunt/Boss/SlashDissolveParticle"
{
    Properties
    {
        [MainTexture] _BaseMap("Sword Qi Texture", 2D) = "white" {}
        [HDR] [MainColor] _BaseColor("Tint", Color) = (2.8,0.025,0.08,1)
        _Intensity("Emission Intensity", Range(0,12)) = 5.5
        _Dissolve("Endpoint Dissolve", Range(0,1)) = 0
        _DissolveEdge("Dissolve Edge", Range(0.005,0.25)) = 0.09
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+70" "RenderPipeline"="UniversalPipeline" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "TravellingSwordQi"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            float4 _BaseMap_ST;
            float4 _BaseColor;
            float _Intensity;
            float _Dissolve;
            float _DissolveEdge;

            float Hash21(float2 value)
            {
                value = frac(value * float2(123.34, 456.21));
                value += dot(value, value + 45.32);
                return frac(value.x * value.y);
            }

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 textureValue = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                float noise = Hash21(floor(input.uv * float2(52.0, 28.0)));
                float dissolveMask = smoothstep(_Dissolve - _DissolveEdge, _Dissolve + _DissolveEdge, noise);
                float edge = saturate(1.0 - abs(noise - _Dissolve) / max(_DissolveEdge, .001));
                float alphaMask = textureValue.a * dissolveMask;
                clip(alphaMask - .015);
                half3 core = textureValue.rgb * _BaseColor.rgb * input.color.rgb * _Intensity;
                half3 dissolveGlow = _BaseColor.rgb * edge * 3.5 * step(.001, _Dissolve);
                return half4((core + dissolveGlow) * alphaMask, alphaMask * input.color.a);
            }
            ENDHLSL
        }
    }
}
