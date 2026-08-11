#ifndef CLOUD_NOISE_SAMPLING_INCLUDED
#define CLOUD_NOISE_SAMPLING_INCLUDED


float CloudSamplingRemap(
    float value,
    float originalMin,
    float originalMax,
    float newMin,
    float newMax
)
{
    return newMin +
        (value - originalMin) *
        (newMax - newMin) /
        (originalMax - originalMin);
}


#define remap CloudSamplingRemap


void CloudNoiseSampling_float(
    UnityTexture3D ShapeNoiseTex,
    UnityTexture3D DetailNoiseTex,

    float3 animatedPos,
    float3 detailAnimatedPos,

    float ShapeNoiseScale,
    float shapeMip,

    float DetailNoiseScale,
    float detailMip,

    float dimensional_profile,
    float heightFraction,

    out float BaseCloud,
    out float BaseCloudDensity,
    out float DetailModifier
)
{
    BaseCloud = 0.0;
    BaseCloudDensity = 0.0;
    DetailModifier = 0.0;

    float _ShapeNoiseScale = ShapeNoiseScale;
    float _DetailNoiseScale = DetailNoiseScale;


#define _ShapeNoiseTex ShapeNoiseTex.tex
#define sampler_ShapeNoiseTex ShapeNoiseTex.samplerstate

#define _DetailNoiseTex DetailNoiseTex.tex
#define sampler_DetailNoiseTex DetailNoiseTex.samplerstate


    float4 shape = SAMPLE_TEXTURE3D_LOD(_ShapeNoiseTex, sampler_ShapeNoiseTex, animatedPos * _ShapeNoiseScale, shapeMip);
    float worleyFBM = shape.g * 0.625 + shape.b * 0.25 + shape.a * 0.125;
    float baseCloud = remap(shape.r, worleyFBM - 1.0, 1.0, 0.0, 1.0);
    baseCloud = saturate(baseCloud);


    // 乘上高度梯度
    float base_cloud_density = saturate(baseCloud - (1.0 - dimensional_profile));


    float3 detail = SAMPLE_TEXTURE3D_LOD(_DetailNoiseTex, sampler_DetailNoiseTex, detailAnimatedPos * _DetailNoiseScale, detailMip).rgb;
    float detailFBM = detail.r * 0.625 + detail.g * 0.25 + detail.b * 0.125;
    float detailModifier = lerp(detailFBM, 1.0 - detailFBM, saturate(heightFraction * 5.0));
    //detailModifier = lerp(detailFBM, 1.0 - detailFBM, saturate(dimensional_profile));


    BaseCloud = baseCloud;
    BaseCloudDensity = base_cloud_density;
    DetailModifier = detailModifier;


#undef _ShapeNoiseTex
#undef sampler_ShapeNoiseTex

#undef _DetailNoiseTex
#undef sampler_DetailNoiseTex
}


#undef remap

#endif