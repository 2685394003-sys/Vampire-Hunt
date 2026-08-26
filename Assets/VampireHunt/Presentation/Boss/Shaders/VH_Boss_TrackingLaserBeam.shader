Shader "VampireHunt/Boss/TrackingLaserBeam"
{
    Properties
    {
        [HDR] _OuterColor("Outer Color", Color) = (3.6,0.008,0.025,.72)
        [HDR] _CoreColor("Core Color", Color) = (8,.3,.34,1)
        _FlowSpeed("Flow Speed", Float) = 5
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+72" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off
        ZTest LEqual

        Pass
        {
            Name "TrackingLaserVolume"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionOS : TEXCOORD0; };

            float4 _OuterColor;
            float4 _CoreColor;
            float _FlowSpeed;

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.positionOS = input.positionOS.xyz;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 crossSection = abs(input.positionOS.xy) * 2.0;
                float core = 1.0 - smoothstep(.08, .72, max(crossSection.x, crossSection.y));
                float edge = smoothstep(.55, .98, max(crossSection.x, crossSection.y));
                float longitudinal = input.positionOS.z + .5;
                float bands = .72 + .28 * sin(longitudinal * 50.0 - _Time.y * _FlowSpeed * 8.0);
                float pulse = .82 + .18 * sin(_Time.y * 11.0);
                half3 color = _OuterColor.rgb * (.35 + .65 * bands) + _CoreColor.rgb * core * pulse;
                float alpha = saturate(_OuterColor.a * (.32 + .42 * bands + .2 * edge) + core * .48);
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
}
