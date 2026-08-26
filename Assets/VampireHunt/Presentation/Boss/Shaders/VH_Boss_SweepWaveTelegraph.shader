Shader "VampireHunt/Boss/SweepWaveTelegraph"
{
    Properties
    {
        [HDR] _FillColor("Fill Color", Color) = (1.7,0.008,0.02,1)
        [HDR] _WaveColor("Wave Front Color", Color) = (4.5,0.06,0.025,1)
        _Progress("Fill Progress", Range(0,1)) = 0
        _Reverse("Right To Left", Range(0,1)) = 0
        _WaveWidth("Wave Width", Range(0.005,0.2)) = 0.065
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+61" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off
        ZTest LEqual

        Pass
        {
            Name "DirectionalSweepWarning"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            float4 _FillColor;
            float4 _WaveColor;
            float _Progress;
            float _Reverse;
            float _WaveWidth;

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float axis = lerp(input.uv.x, 1.0 - input.uv.x, step(.5, _Reverse));
                float waveOffset = sin(input.uv.y * 17.0 + _Time.y * 3.5) *
                                   .045 * (1.0 - _Progress);
                float waveFront = saturate(_Progress + waveOffset);
                float revealed = 1.0 - smoothstep(waveFront, waveFront + .025, axis);
                float front = 1.0 - smoothstep(_WaveWidth, _WaveWidth * 1.8, abs(axis - waveFront));
                float longPulse = .78 + .22 * sin(input.uv.y * 24.0 - _Time.y * 7.0);
                float fillAlpha = revealed * _Progress * (.22 + .34 * longPulse);
                float frontAlpha = front * saturate(_Progress * 4.0) * .9;
                half3 color = _FillColor.rgb * fillAlpha + _WaveColor.rgb * frontAlpha;
                return half4(color, saturate(fillAlpha + frontAlpha));
            }
            ENDHLSL
        }
    }
}
