Shader "Custom/VolumetricCloud/RayMarch"
{
    Properties
    {
        [NoScaleOffset]_WeatherMap("Weather Map", 2D) = "white" {}
        [NoScaleOffset]_ShapeNoiseTex("Shape Noise", 3D) = "" {}
        [NoScaleOffset]_DetailNoiseTex("Detail Noise", 3D) = "" {}

        _CloudColor("Cloud Color", Color) = (1,1,1,1)

        _EarthRadius("Earth Radius", Float) = 6371000
        _CloudBottom("Cloud Bottom", Float) = 1500
        _CloudTop("Cloud Top", Float) = 6500

        _Coverage("Coverage", Range(0,1)) = 1
        _DensityMultiplier("Density Multiplier", Float) = 1
        _Extinction("Extinction", Float) = 0.001
        _StepCount("Step Count", Float) = 64

        _ShapeNoiseScale("Shape Noise Scale", Float) = 0.00008
        _DetailNoiseScale("Detail Noise Scale", Float) = 0.0006
        _DetailErodeStrength("Detail Erode Strength", Range(0,1)) = 0.35

        _WeatherMapScale("Weather Map Scale", Float) = 80000

        _WindDirection("Wind Direction", Vector) = (1,0,0,0)
        _WindSpeed("Wind Speed", Float) = 30

        _WeatherMoveDirection("Weather Move Direction", Vector) = (1,0,0,0)
        _WeatherMoveSpeed("Weather Move Speed", Float) = 0.0001
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent"
            "RenderPipeline"="UniversalPipeline"
        }

        Pass
        {
            Name "VolumetricCloudRayMarch"
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Front

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            //#define PI 3.14159265

            TEXTURE2D(_WeatherMap);
            SAMPLER(sampler_WeatherMap);

            TEXTURE3D(_ShapeNoiseTex);
            SAMPLER(sampler_ShapeNoiseTex);

            TEXTURE3D(_DetailNoiseTex);
            SAMPLER(sampler_DetailNoiseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _CloudColor;

                float _EarthRadius;
                float _CloudBottom;
                float _CloudTop;

                float _Coverage;
                float _DensityMultiplier;
                float _Extinction;
                float _StepCount;

                float _ShapeNoiseScale;
                float _DetailNoiseScale;
                float _DetailErodeStrength;

                float _WeatherMapScale;

                float4 _WindDirection;
                float _WindSpeed;

                float4 _WeatherMoveDirection;
                float _WeatherMoveSpeed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                return OUT;
            }

            float remap(float value, float inMin, float inMax, float outMin, float outMax)
            {
                return outMin + (value - inMin) / max(inMax - inMin, 1e-5) * (outMax - outMin);
            }

            bool raySphereIntersect(float3 rayOrigin, float3 rayDir, float3 center, float radius, out float t0, out float t1)
            {
                float3 oc = rayOrigin - center;
                float b = dot(rayDir, oc);
                float c = dot(oc, oc) - radius * radius;
                float disc = b * b - c;

                if (disc < 0)
                {
                    t0 = -1;
                    t1 = -1;
                    return false;
                }

                float sqrtDisc = sqrt(disc);
                t0 = -b - sqrtDisc;
                t1 = -b + sqrtDisc;
                return true;
            }

            float GetHeightGradient(float heightFraction, float cloudType)
            {
                float stratusBottomFade = remap(heightFraction, 0.05, 0.10, 0.0, 1.0);
                float stratusTopFade = remap(heightFraction, 0.15, 0.22, 1.0, 0.0);

                float stratocumulusBottomFade = remap(heightFraction, 0.05, 0.15, 0.0, 1.0);
                float stratocumulusTopFade = remap(heightFraction, 0.35, 0.50, 1.0, 0.0);

                float cumulusBottomFade = remap(heightFraction, 0.05, 0.20, 0.0, 1.0);
                float cumulusTopFade = remap(heightFraction, 0.60, 0.85, 1.0, 0.0);

                float t1 = saturate(cloudType * 2.0);
                float t2 = saturate((cloudType - 0.5) * 2.0);

                float topGradient = lerp(
                    lerp(stratusTopFade, stratocumulusTopFade, t1),
                    cumulusTopFade,
                    t2
                );

                float bottomGradient = lerp(
                    lerp(stratusBottomFade, stratocumulusBottomFade, t1),
                    cumulusBottomFade,
                    t2
                );

                float heightGradient = saturate(topGradient) * saturate(bottomGradient);
                return saturate(heightGradient);
            }

            float SampleCloudDensity(float3 p, float3 earthCenter)
            {
                float distToCenter = length(p - earthCenter);
                float heightFraction = saturate((distToCenter - (_EarthRadius + _CloudBottom)) / (_CloudTop - _CloudBottom));

                float2 wUV = p.xz / _WeatherMapScale + 0.5 + _WeatherMoveDirection.xz * _WeatherMoveSpeed * _Time.y;
                wUV = frac(wUV);
                float4 w = SAMPLE_TEXTURE2D_LOD(_WeatherMap, sampler_WeatherMap, wUV, 0);

                float dimensional_profile = GetHeightGradient(heightFraction, w.g) * (w.r * _Coverage);

                float3 windOffset = _WindDirection.xyz * _Time.y * _WindSpeed;
                float3 animatedPos = p + windOffset;

                float3 shapeUVW = frac(animatedPos * _ShapeNoiseScale);
                float4 shape = SAMPLE_TEXTURE3D_LOD(_ShapeNoiseTex, sampler_ShapeNoiseTex, shapeUVW, 0);

                float worleyFBM = shape.g * 0.625 + shape.b * 0.25 + shape.a * 0.125;
                float baseCloud = saturate(remap(shape.r, worleyFBM, 1.0, 0.0, 1.0));
                float base_cloud_density = saturate(baseCloud - (1.0 - dimensional_profile));

                float3 detailUVW = frac(animatedPos * _DetailNoiseScale);
                float3 detail = SAMPLE_TEXTURE3D_LOD(_DetailNoiseTex, sampler_DetailNoiseTex, detailUVW, 0).rgb;
                float detailFBM = detail.r * 0.625 + detail.g * 0.25 + detail.b * 0.125;
                float detailModifier = lerp(detailFBM, 1.0 - detailFBM, saturate(heightFraction * 5.0));

                float density = saturate(base_cloud_density - detailModifier * _DetailErodeStrength);
                return density * _DensityMultiplier;
            }

            float4 frag(Varyings IN) : SV_Target
            {
                float3 rayOrigin = _WorldSpaceCameraPos;
                float3 rayDir = normalize(IN.positionWS - rayOrigin);

                float3 earthCenter = float3(_WorldSpaceCameraPos.x, -_EarthRadius, _WorldSpaceCameraPos.z);

                float innerRadius = _EarthRadius + _CloudBottom;
                float outerRadius = _EarthRadius + _CloudTop;

                float t0Inner, t1Inner, t0Outer, t1Outer;
                bool hitInner = raySphereIntersect(rayOrigin, rayDir, earthCenter, innerRadius, t0Inner, t1Inner);
                bool hitOuter = raySphereIntersect(rayOrigin, rayDir, earthCenter, outerRadius, t0Outer, t1Outer);

                if (!hitOuter)
                    return float4(0, 0, 0, 0);

                float camDist = length(rayOrigin - earthCenter);

                float tMin = 0;
                float tMax = 0;

                if (camDist < innerRadius)
                {
                    tMin = t1Inner;
                    tMax = t1Outer;
                }
                else if (camDist > outerRadius)
                {
                    tMin = t0Outer;
                    tMax = (hitInner && t0Inner > 0) ? t0Inner : t1Outer;
                }
                else
                {
                    tMin = 0;
                    tMax = (hitInner && t1Inner > 0) ? t1Inner : t1Outer;
                }

                if (tMax <= tMin)
                    return float4(0, 0, 0, 0);

                int steps = max(1, (int)_StepCount);
                float stepSize = (tMax - tMin) / steps;
                float t = tMin;

                float transmittance = 1.0;
                float alpha = 0.0;
                float3 color = 0.0;

                [loop]
                for (int i = 0; i < 256; i++)
                {
                    if (i >= steps) break;

                    float3 samplePos = rayOrigin + rayDir * (t + stepSize * 0.5);
                    float density = SampleCloudDensity(samplePos, earthCenter);

                    if (density > 0.0001)
                    {
                        float d = density * stepSize * _Extinction;
                        float stepTransmittance = saturate(1.0 - d);

                        float sampleAlpha = (1.0 - stepTransmittance) * transmittance;

                        color += sampleAlpha * _CloudColor.rgb;
                        alpha += sampleAlpha;

                        transmittance *= stepTransmittance;

                        if (transmittance < 0.01)
                            break;
                    }

                    t += stepSize;
                }

                return float4(color, saturate(alpha));
            }
            ENDHLSL
        }
    }
}