#ifndef CLOUD_RAY_MARCH_INCLUDED
#define CLOUD_RAY_MARCH_INCLUDED

#include "Assets/Art/VFX/VolumetricCloud/CloudHeightProfile.hlsl"
#include "Assets/Art/VFX/VolumetricCloud/CloudNoiseSampling.hlsl"

void CloudRayMarch_float(
    UnityTexture3D ShapeNoiseTex,
    UnityTexture3D DetailNoiseTex,

    float3 RayOrigin,
    float3 RayDir,

    float Hit,
    float TMin,
    float TMax,

    float CloudBottom,
    float CloudTop,

    float TopType,
    float BottomType,
    float Coverage,

    float ShapeNoiseScale,
    float ShapeMip,

    float DetailNoiseScale,
    float DetailMip,

    float StepCount,
    float Extinction,

    out float CloudAlpha,
    out float AverageDensity,
    out float DetailPreview
)
{
    CloudAlpha = 0.0;
    AverageDensity = 0.0;
    DetailPreview = 0.0;

#if defined(SHADERGRAPH_PREVIEW)

    return;

#else

    if (Hit < 0.5 || TMax <= TMin)
    {
        return;
    }

    RayDir = normalize(RayDir);

    int steps = max(
        1,
        min((int)StepCount, 128)
    );

    float startT = max(TMin, 0.0);
    float marchLength = TMax - startT;

    if (marchLength <= 0.0)
    {
        return;
    }

    float stepSize =
        marchLength / (float)steps;

    float earthRadius = 6371000.0;

    float3 earthCenter = float3(
        RayOrigin.x,
        -earthRadius,
        RayOrigin.z
    );

    float light_absorption = 0.0;

    float accumulatedDensity = 0.0;
    float accumulatedDetail = 0.0;

    [loop]
        for (int i = 0; i < 128; i++)
        {
            if (i >= steps)
            {
                break;
            }

            float currentT =
                startT +
                ((float)i + 0.5) * stepSize;

            float3 samplePos =
                RayOrigin +
                RayDir * currentT;

            float distToCenter =
                length(samplePos - earthCenter);

            float heightFraction = saturate(
                (
                    distToCenter -
                    (earthRadius + CloudBottom)
                    ) /
                max(
                    CloudTop - CloudBottom,
                    0.0001
                )
            );

            float dimensional_profile = 0.0;

            CloudHeightProfile_float(
                heightFraction,
                TopType,
                BottomType,
                Coverage,
                dimensional_profile
            );

            float baseCloud = 0.0;
            float baseCloudDensity = 0.0;
            float detailModifier = 0.0;

            CloudNoiseSampling_float(
                ShapeNoiseTex,
                DetailNoiseTex,

                samplePos,
                samplePos,

                ShapeNoiseScale,
                ShapeMip,

                DetailNoiseScale,
                DetailMip,

                dimensional_profile,
                heightFraction,

                baseCloud,
                baseCloudDensity,
                detailModifier
            );

            float density = 
                saturate(baseCloudDensity);

            accumulatedDensity += density;
            accumulatedDetail += detailModifier;

            // 作者文章中的吸收率累积写法
            float d =
                density *
                stepSize *
                Extinction;

            light_absorption +=
                d *
                (1.0 - light_absorption);

            float transmittance =
                1.0 - light_absorption;

            if (transmittance < 0.01)
            {
                break;
            }
        }

    CloudAlpha =
        saturate(light_absorption);

    AverageDensity =
        saturate(
            accumulatedDensity *
            stepSize *
            Extinction
        );

    DetailPreview =
        saturate(
            accumulatedDetail /
            (float)steps
        );

#endif
}

#endif