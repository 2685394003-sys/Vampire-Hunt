Shader "VampireHunt/Boss/AdditiveVFX"
{
    Properties
    {
        [MainTexture] _BaseMap("VFX Texture", 2D) = "white" {}
        [HDR] [MainColor] _BaseColor("Tint", Color) = (1,0.02,0.08,1)
        _Intensity("Emission Intensity", Range(0,12)) = 4
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
            float4 _BaseMap_TexelSize;
            float4 _BaseColor;
            float _Intensity;
            float _EdgePower;

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                // Keep the mesh UV in the local 0-1 range. The atlas transform and
                // cell-edge protection are applied together in the fragment shader.
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // This texture is a 3x3 atlas of independent, non-tileable VFX art.
                // Scrolling the atlas itself will eventually reveal another cell.
                // Keep the artwork fixed and inset the sample by one texel so
                // bilinear filtering cannot bleed across atlas-cell boundaries.
                float2 cellPadding = min(_BaseMap_TexelSize.xy, _BaseMap_ST.xy * 0.05);
                float2 cellMin = _BaseMap_ST.zw + cellPadding;
                float2 cellMax = _BaseMap_ST.zw + _BaseMap_ST.xy - cellPadding;
                float2 atlasUv = lerp(cellMin, cellMax, saturate(input.uv));
                half4 sampleValue = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, atlasUv);
                half mask = pow(saturate(sampleValue.a), _EdgePower);
                half3 emission = sampleValue.rgb * _BaseColor.rgb * input.color.rgb * _Intensity;
                return half4(emission * mask, mask * _BaseColor.a * input.color.a);
            }
            ENDHLSL
        }
    }
}
