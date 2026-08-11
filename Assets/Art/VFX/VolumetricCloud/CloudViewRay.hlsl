#ifndef CLOUD_VIEW_RAY_INCLUDED
#define CLOUD_VIEW_RAY_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

void CloudViewRay_float(
    float2 UV,
    out float3 RayOrigin,
    out float3 RayDir
)
{
    RayOrigin = float3(0.0, 0.0, 0.0);
    RayDir = float3(0.0, 0.0, 1.0);

#if defined(SHADERGRAPH_PREVIEW)

    float2 previewPosition = UV * 2.0 - 1.0;

    RayDir = normalize(
        float3(
            previewPosition.x,
            previewPosition.y,
            1.0
        )
    );

#else

    RayOrigin = _WorldSpaceCameraPos;

#if UNITY_REVERSED_Z
    float farDepth = 0.0;
#else
    float farDepth = 1.0;
#endif

    float3 farPositionWS = ComputeWorldSpacePosition(
        UV,
        farDepth,
        UNITY_MATRIX_I_VP
    );

    RayDir = normalize(
        farPositionWS - RayOrigin
    );

#endif
}

#endif