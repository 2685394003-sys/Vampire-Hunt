#ifndef CLOUD_HEIGHT_PROFILE_INCLUDED
#define CLOUD_HEIGHT_PROFILE_INCLUDED

float remap(
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

void CloudHeightProfile_float(
    float heightFraction,
    float topType,
    float bottomType,
    float coverage,
    out float Density
)
{

    // 层云 Stratus: 从 0.05 开始淡入，0.1 达到最浓；0.15 开始淡出，0.22 彻底消失（很薄）
    float stratusBottomFade = remap(heightFraction, 0.05, 0.1, 0.0, 1.0);
    float stratusTopFade = remap(heightFraction, 0.15, 0.22, 1.0, 0.0);

    // 层积云 Stratocumulus: 从 0.05 开始淡入，0.15 达到最浓；从 0.35 开始淡出，0.5 彻底消失（中等）
    float stratocumulusBottomFade = remap(heightFraction, 0.05, 0.15, 0.0, 1.0);
    float stratocumulusTopFade = remap(heightFraction, 0.35, 0.5, 1.0, 0.0);

    // 积云 Cumulus: 从 0.05 开始淡入，0.2 达到最浓；从 0.6 开始淡出，0.85 彻底消失（高耸）
    float cumulusBottomFade = remap(heightFraction, 0.05, 0.2, 0.0, 1.0);
    float cumulusTopFade = remap(heightFraction, 0.6, 0.85, 1.0, 0.0);

    // blend topGradient
    float heightGradient = 0.0;
    float t1 = saturate(topType * 2.0);
    float t2 = saturate((topType - 0.5) * 2.0);
    // float topGradient = lerp(stratusTopFade, max(stratusTopFade, stratocumulusTopFade), t1);
    // topGradient = lerp(topGradient, max(topGradient, cumulusTopFade), t2);
    float topGradient = lerp(
        lerp(stratusTopFade, stratocumulusTopFade, t1),
        cumulusTopFade,
        t2
    );

    // blend bottomGradient
    float b1 = saturate(bottomType * 2.0);
    float b2 = saturate((bottomType - 0.5) * 2.0);
    // float bottomGradient = lerp(stratusBottomFade, max(stratusBottomFade, stratocumulusBottomFade), b1);
    // bottomGradient = lerp(bottomGradient, max(bottomGradient, cumulusBottomFade), b2);
    float bottomGradient = lerp(
        lerp(stratusBottomFade, stratocumulusBottomFade, b1),
        cumulusBottomFade,
        b2
    );
    heightGradient = saturate(topGradient) * saturate(bottomGradient);
    heightGradient = saturate(heightGradient);


    float dimensional_profile = saturate(heightGradient * coverage);

    Density = dimensional_profile;
}

#endif