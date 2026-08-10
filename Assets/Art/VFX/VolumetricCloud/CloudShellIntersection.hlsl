#ifndef CLOUD_SHELL_INTERSECTION_INCLUDED
#define CLOUD_SHELL_INTERSECTION_INCLUDED

// 射线和球求交
bool raySphereIntersect(float3 rayOrigin, float3 rayDir, float3 center, float radius, out float t0, out float t1)
{
    float3 oc = rayOrigin - center;
    float b = dot(rayDir, oc);
    float c = dot(oc, oc) - radius * radius;
    float disc = b * b - c;
    if (disc < 0) { t0 = -1; t1 = -1; return false; }
    float sqrtDisc = sqrt(disc);
    t0 = -b - sqrtDisc;
    t1 = -b + sqrtDisc;
    return true;
}

void CloudShellRange_float(
    float3 RayOrigin,
    float3 RayDir,
    float CloudBottom,
    float CloudTop,
    out float Hit,
    out float TMin,
    out float TMax
)
{
    Hit = 0.0;
    TMin = 0.0;
    TMax = 0.0;

    // 地球中心在相机正下方
    float earthRadius = 6371000.0;
    float3 earthCenter = float3(
        RayOrigin.x,
        -earthRadius,
        RayOrigin.z
    );

    float t0Inner;
    float t1Inner;
    float t0Outer;
    float t1Outer;

    bool hitInner = raySphereIntersect(
        RayOrigin,
        RayDir,
        earthCenter,
        earthRadius + CloudBottom,
        t0Inner,
        t1Inner
    );

    bool hitOuter = raySphereIntersect(
        RayOrigin,
        RayDir,
        earthCenter,
        earthRadius + CloudTop,
        t0Outer,
        t1Outer
    );

    // 没打到外球，不渲染
    if (!hitOuter)
    {
        return;
    }

    // 相机位于云层下方并且射线向下，不渲染
    if (RayOrigin.y < CloudBottom && RayDir.y < -0.001)
    {
        return;
    }

    if (RayOrigin.y < CloudBottom)
    {
        // 相机在云层下方
        TMin = t1Inner;
        TMax = t1Outer;
    }
    else if (RayOrigin.y > CloudTop)
    {
        // 相机在云层上方
        TMin = t0Outer;
        TMax = t0Inner > 0.0 ? t0Inner : t1Outer;
    }
    else
    {
        // 相机在云层内部
        TMin = 0.0;

        if (RayDir.y >= 0.0)
        {
            TMax = t1Outer;
        }
        else
        {
            TMax =
                hitInner && t0Inner > 0.0
                ? t0Inner
                : t1Outer;
        }
    }

    if (TMax > TMin && TMax > 0.0)
    {
        Hit = 1.0;
    }
}

#endif