Shader "VampireHunt/Boss/ChargeSlashTelegraph"
{
    Properties
    {
        [HDR] _FillColor("Fill Color", Color) = (1.6,0.01,0.025,1)
        [HDR] _EdgeColor("Edge Color", Color) = (4,0.08,0.04,1)
        _Progress("Charge Progress", Range(0,1)) = 0
        _BorderWidth("Border Width", Range(0.005,0.2)) = 0.045
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+60" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off
        ZTest LEqual

        Pass
        {
            Name "FixedChargeLane"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            float4 _FillColor;
            float4 _EdgeColor;
            float _Progress;
            float _BorderWidth;

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float edgeDistance = min(min(input.uv.x, 1.0 - input.uv.x),
                                         min(input.uv.y, 1.0 - input.uv.y));
                float border = 1.0 - smoothstep(_BorderWidth, _BorderWidth * 1.8, edgeDistance);
                float lanePulse = 0.75 + 0.25 * sin((input.uv.y * 12.0 - _Time.y * 5.0) * 3.14159);
                float fillAlpha = _Progress * _Progress * (0.18 + 0.34 * lanePulse);
                float edgeAlpha = _Progress * border;
                half3 color = _FillColor.rgb * fillAlpha + _EdgeColor.rgb * edgeAlpha;
                return half4(color, saturate(fillAlpha + edgeAlpha));
            }
            ENDHLSL
        }
    }
}
