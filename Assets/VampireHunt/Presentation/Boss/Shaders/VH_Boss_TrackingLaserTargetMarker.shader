Shader "VampireHunt/Boss/TrackingLaserTargetMarker"
{
    Properties
    {
        [HDR] _MarkerColor("Marker Color", Color) = (4.5,0.01,0.025,1)
        _PulseSpeed("Pulse Speed", Float) = 6
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+85" "RenderPipeline"="UniversalPipeline" }
        Blend SrcAlpha One
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            Name "LockedTargetTriangle"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            float4 _MarkerColor;
            float _PulseSpeed;

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float edge = saturate(min(min(input.uv.x, 1.0 - input.uv.x), input.uv.y) * 12.0);
                float pulse = .62 + .38 * sin(_Time.y * _PulseSpeed);
                float scan = .75 + .25 * sin((input.uv.y - _Time.y * 1.8) * 24.0);
                float alpha = saturate((.65 + .35 * edge) * pulse * scan);
                return half4(_MarkerColor.rgb * (1.0 + edge), alpha * _MarkerColor.a);
            }
            ENDHLSL
        }
    }
}
