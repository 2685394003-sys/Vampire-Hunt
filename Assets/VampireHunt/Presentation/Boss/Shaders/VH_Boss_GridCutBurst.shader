Shader "VampireHunt/Boss/GridCutBurst"
{
    Properties
    {
        [HDR] _LineColor("Blood Slash Color", Color) = (7,0.025,0.035,1)
        [HDR] _CoreColor("White Hot Core", Color) = (12,1.2,0.85,1)
        _PulseProgress("Pulse Progress", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+64" "RenderPipeline"="UniversalPipeline" }
        Blend One One
        ZWrite Off
        Cull Off
        ZTest LEqual

        Pass
        {
            Name "GridStripBurst"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            float4 _LineColor;
            float4 _CoreColor;
            float _PulseProgress;

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float progress = saturate(_PulseProgress);
                float fade = 1.0 - smoothstep(0.12, 1.0, progress);
                float distanceFromCenter = abs(input.uv.y - 0.5) * 2.0;
                float broadSlash = 1.0 - smoothstep(0.45, 1.0, distanceFromCenter);
                float hotCore = 1.0 - smoothstep(0.02, 0.22, distanceFromCenter);
                float travelFlash = 1.0 - smoothstep(0.0, 0.2,
                    abs(input.uv.x - saturate(progress * 1.25)));
                float flicker = 0.86 + 0.14 * sin(_Time.y * 31.0 + input.uv.x * 19.0);
                half3 color = _LineColor.rgb * broadSlash * fade * flicker;
                color += _CoreColor.rgb * hotCore * fade * (1.15 - progress);
                color += _CoreColor.rgb * broadSlash * travelFlash * fade * 0.35;
                return half4(color, saturate((broadSlash + hotCore) * fade));
            }
            ENDHLSL
        }
    }
}
