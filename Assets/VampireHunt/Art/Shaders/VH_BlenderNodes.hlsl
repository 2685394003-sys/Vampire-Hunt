#ifndef VH_BLENDER_NODES_INCLUDED
#define VH_BLENDER_NODES_INCLUDED

// ============================================================================
// Blender 程序化纹理节点的 HLSL 复刻
// 对应：Voronoi Texture(F1/Euclidean) / Noise Texture(3D/4D) / ColorRamp /
//       Mapping(Point) / Math(MINIMUM, LOGARITHM, MULTIPLY, ADD) / Mix(RGBA)
// 全部按节点实际参数实现，不用贴图。
// ============================================================================

float3 VH_Hash33(float3 p)
{
    p = float3(dot(p, float3(127.1, 311.7,  74.7)),
               dot(p, float3(269.5, 183.3, 246.1)),
               dot(p, float3(113.5, 271.9, 124.6)));
    return frac(sin(p) * 43758.5453123);
}

// ---- Voronoi Texture: Feature=F1, Distance=EUCLIDEAN, normalize=false ----
// randomness = 节点上的 Randomness（0=规则网格, 1=完全随机）
float VH_VoronoiF1(float3 p, float randomness)
{
    float3 base = floor(p);
    float3 f    = frac(p);
    float  best = 1e9;
    for (int x = -1; x <= 1; ++x)
    for (int y = -1; y <= 1; ++y)
    for (int z = -1; z <= 1; ++z)
    {
        float3 c   = float3(x, y, z);
        float3 rnd = VH_Hash33(base + c);
        float3 pt  = c + lerp(0.5, rnd, randomness);
        best = min(best, distance(f, pt));
    }
    return best;
}

// ---- Perlin 梯度噪声（Blender Noise 的底层） ----
float3 VH_Fade(float3 t) { return t * t * t * (t * (t * 6.0 - 15.0) + 10.0); }

float VH_Perlin(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    float3 u = VH_Fade(f);

    float n000 = dot(VH_Hash33(i + float3(0,0,0)) * 2 - 1, f - float3(0,0,0));
    float n100 = dot(VH_Hash33(i + float3(1,0,0)) * 2 - 1, f - float3(1,0,0));
    float n010 = dot(VH_Hash33(i + float3(0,1,0)) * 2 - 1, f - float3(0,1,0));
    float n110 = dot(VH_Hash33(i + float3(1,1,0)) * 2 - 1, f - float3(1,1,0));
    float n001 = dot(VH_Hash33(i + float3(0,0,1)) * 2 - 1, f - float3(0,0,1));
    float n101 = dot(VH_Hash33(i + float3(1,0,1)) * 2 - 1, f - float3(1,0,1));
    float n011 = dot(VH_Hash33(i + float3(0,1,1)) * 2 - 1, f - float3(0,1,1));
    float n111 = dot(VH_Hash33(i + float3(1,1,1)) * 2 - 1, f - float3(1,1,1));

    float nx00 = lerp(n000, n100, u.x);
    float nx10 = lerp(n010, n110, u.x);
    float nx01 = lerp(n001, n101, u.x);
    float nx11 = lerp(n011, n111, u.x);
    return lerp(lerp(nx00, nx10, u.y), lerp(nx01, nx11, u.y), u.z);
}

// ---- Noise Texture: 输出 Fac(0~1)，normalize=true ----
// Blender 的 Noise = fBm(Perlin, octaves=Detail, persistence=Roughness, lacunarity=Lacunarity)
float VH_NoiseTexture(float3 p, float scale, float detail, float roughness, float lacunarity)
{
    float3 q = p * scale;
    float  sum = 0.0, amp = 1.0, norm = 0.0, freq = 1.0;
    int    oct = (int)clamp(detail, 1.0, 12.0);
    for (int i = 0; i < oct; ++i)
    {
        sum  += amp * VH_Perlin(q * freq);
        norm += amp;
        amp  *= roughness;
        freq *= lacunarity;
    }
    float n = sum / max(norm, 1e-4);   // -1..1
    return saturate(n * 0.5 + 0.5);    // 0..1
}

// ---- Mapping (vector_type = POINT)：p * scale + location ----
float3 VH_MappingPoint(float3 p, float3 location, float3 scale)
{
    return p * scale + location;
}

// ---- ColorRamp (LINEAR)：两个色标之间线性插值 ----
float3 VH_ColorRamp2(float t, float3 c0, float3 c1, float pos0, float pos1)
{
    float k = saturate((t - pos0) / max(1e-4, pos1 - pos0));
    return lerp(c0, c1, k);
}

// ---- 简易 3D 噪声梯度（给 Bump 用） ----
// Blender 的 Bump 用 Height 的梯度扰动法线；这里用屏幕空间导数做等价近似（见 shader）。

#endif
