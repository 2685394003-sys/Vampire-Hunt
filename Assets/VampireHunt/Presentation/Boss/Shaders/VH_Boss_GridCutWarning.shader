Shader "VampireHunt/Boss/GridCutWarning"
{
    Properties
    {
        [HDR] _LineColor("Blood Warning Color", Color) = (3.4,0.008,0.018,1)
        [HDR] _HotColor("Fully Charged Color", Color) = (8,0.18,0.12,1)
        _Progress("Charge Progress", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+62" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off
        ZTest LEqual

        Pass
        {
            Name "GridStripWarning"
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
            float4 _HotColor;
            float _Progress;

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float progress = saturate(_Progress);
                float edgeX = smoothstep(0.0, 0.08, input.uv.x) *
                              (1.0 - smoothstep(0.92, 1.0, input.uv.x));
                float edgeY = smoothstep(0.0, 0.18, input.uv.y) *
                              (1.0 - smoothstep(0.82, 1.0, input.uv.y));
                float softRectangle = saturate(edgeX * edgeY + 0.2);
                float breathing = 0.88 + 0.12 * sin(_Time.y * 8.0 + input.uv.x * 11.0);
                float charge = progress * progress;
                float alpha = softRectangle * breathing * lerp(0.015, 0.82, progress);
                half3 color = lerp(_LineColor.rgb, _HotColor.rgb, charge);
                return half4(color, alpha * _LineColor.a);
            }
            ENDHLSL
        }
    }
}
