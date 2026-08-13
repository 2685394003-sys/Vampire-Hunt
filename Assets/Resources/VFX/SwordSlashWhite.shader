Shader "Vampire Hunt/VFX/White Sword Slash"
{
    Properties
    {
        [HDR] _Color("Color", Color) = (1, 1, 1, 1)
        _Opacity("Opacity", Range(0, 1)) = 1
        _Progress("Reveal Progress", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent+50"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "WhiteSwordSlash"
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
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

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Opacity;
                half _Progress;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half radial = saturate(1.0h - abs(input.uv.y * 2.0h - 1.0h));
                half softRibbon = pow(radial, 0.35h);
                half hotCore = pow(radial, 7.0h);
                half angularFade = smoothstep(0.0h, 0.07h, input.uv.x) *
                                   smoothstep(0.0h, 0.07h, 1.0h - input.uv.x);
                half revealed = 1.0h - smoothstep(_Progress, _Progress + 0.055h, input.uv.x);
                half alpha = (softRibbon * 0.58h + hotCore * 0.42h) *
                             angularFade * revealed * _Opacity * _Color.a;
                half3 color = _Color.rgb * (1.0h + hotCore * 2.5h);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
