Shader "VampireHunt/Boss/AdditiveVFX"
{
    Properties
    {
        [MainTexture] _BaseMap("VFX Texture", 2D) = "white" {}
        [HDR] [MainColor] _BaseColor("Tint", Color) = (1,0.02,0.08,1)
        _Intensity("Emission Intensity", Range(0,12)) = 4
        _FlowSpeed("Flow Speed", Range(-4,4)) = 0.15
        _EdgePower("Edge Power", Range(0.25,4)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+40" "RenderPipeline"="UniversalPipeline" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "BossBloodMagic"
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
            float _FlowSpeed;
            float _EdgePower;

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
                float2 flowUv = input.uv + float2(_Time.y * _FlowSpeed * 0.01, 0);
                half4 sampleValue = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, flowUv);
                half mask = pow(saturate(sampleValue.a), _EdgePower);
                half3 emission = sampleValue.rgb * _BaseColor.rgb * input.color.rgb * _Intensity;
                return half4(emission * mask, mask * _BaseColor.a * input.color.a);
            }
            ENDHLSL
        }
    }
}
