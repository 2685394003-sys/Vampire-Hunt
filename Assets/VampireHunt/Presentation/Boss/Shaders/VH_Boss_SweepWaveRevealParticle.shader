Shader "VampireHunt/Boss/SweepWaveRevealParticle"
{
    Properties
    {
        [MainTexture] _BaseMap("Sweep Texture", 2D) = "white" {}
        [HDR] [MainColor] _BaseColor("Tint", Color) = (3.2,0.018,0.055,1)
        _Intensity("Emission Intensity", Range(0,12)) = 6
        _Progress("Directional Reveal", Range(0,1)) = 0
        _Reverse("Right To Left", Range(0,1)) = 0
        _EdgeWidth("Reveal Edge", Range(0.005,0.2)) = 0.055
        _SlideAmount("Texture Slide", Range(0,0.4)) = 0.16
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+72" "RenderPipeline"="UniversalPipeline" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "DirectionalSweepReveal"
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
            float _Progress;
            float _Reverse;
            float _EdgeWidth;
            float _SlideAmount;

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
                float reverse = step(.5, _Reverse);
                float axis = lerp(input.uv.x, 1.0 - input.uv.x, reverse);
                float waveOffset = sin(input.uv.y * 15.0 - _Time.y * 9.0) *
                                   .025 * (1.0 - _Progress);
                float front = saturate(_Progress + waveOffset);
                float reveal = 1.0 - smoothstep(front, front + .018, axis);
                float directionSign = lerp(1.0, -1.0, reverse);
                float2 sampleUv = input.uv;
                sampleUv.x += (1.0 - _Progress) * _SlideAmount * directionSign;
                half4 textureValue = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, sampleUv);
                float edge = 1.0 - smoothstep(_EdgeWidth, _EdgeWidth * 1.8, abs(axis - front));
                float mask = textureValue.a * reveal;
                clip(mask - .012);
                half3 core = textureValue.rgb * _BaseColor.rgb * input.color.rgb * _Intensity;
                half3 frontGlow = _BaseColor.rgb * edge * 3.2;
                return half4((core + frontGlow) * mask, mask * input.color.a);
            }
            ENDHLSL
        }
    }
}
