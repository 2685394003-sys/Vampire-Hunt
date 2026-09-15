using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace VampireHunt.Presentation.Combat
{
    /// <summary>
    /// 撞击血爆的可调参数。
    /// </summary>
    /// <remarks>
    /// 除时间（秒）外，**所有尺寸都写成「<c>impactRadius</c> 的倍数」**。
    /// 这样策划改 <c>impactRadius</c> 时整套特效跟着缩放，不会出现
    /// 「剑扎到了却没火花」或「没扎到却炸了」的观感错位。
    /// </remarks>
    [Serializable]
    public struct BloodImpactParams
    {
        // ── ① 命中脉冲：伪造「顿」的那一下 ──────────────────────
        [Tooltip("脉冲球 起始直径 = impactRadius × 此值。")]
        public float pulseStartDiameter;
        [Tooltip("脉冲球 结束直径 = impactRadius × 此值。")]
        public float pulseEndDiameter;
        [Tooltip("脉冲时长（秒）。越短越「脆」，0.08~0.14 之间调整。")]
        public float pulseDuration;

        // ── ② 冲击波：水平扩散的血压环 ─────────────────────────
        [Tooltip("冲击波 起始直径 = impactRadius × 此值。")]
        public float waveStartDiameter;
        [Tooltip("冲击波 结束直径 = impactRadius × 此值。")]
        public float waveEndDiameter;
        [Tooltip("冲击波时长（秒）。")]
        public float waveDuration;
        [Tooltip("冲击波厚度（垂直方向），= impactRadius × 此值。太大就变成饼了。")]
        public float waveThickness;

        // ── ③ 血刺：随机方向的凝结血锥 ─────────────────────────
        [Tooltip("血刺数量。0 = 关闭这一层。")]
        public int spikeCount;
        [Tooltip("血刺长度 = impactRadius × 此值。")]
        public float spikeLength;
        [Tooltip("血刺粗度 = impactRadius × 此值。")]
        public float spikeThickness;
        [Tooltip("血刺伸出用时（秒）。要极短，才有「刺出来」的爆发感。")]
        public float spikeGrowTime;
        [Tooltip("血刺总时长（秒），伸出后回缩到消失。")]
        public float spikeDuration;

        // ── ④ 血雾：受重力的血滴 ───────────────────────────────
        [Tooltip("血滴数量。0 = 关闭这一层。")]
        public int dropCount;
        [Tooltip("血滴直径 = impactRadius × 此值。")]
        public float dropDiameter;
        [Tooltip("血滴初速下限（米/秒）。")]
        public float dropSpeedMin;
        [Tooltip("血滴初速上限（米/秒）。")]
        public float dropSpeedMax;
        [Tooltip("血滴寿命（秒）。")]
        public float dropLifetime;
        [Tooltip("血滴重力（米/秒²）。9.81 写实，11~14 更「重」更爽。")]
        public float dropGravity;
        [Tooltip("血滴空气阻尼（1/秒）。越大飞得越近。")]
        public float dropDrag;

        // ── ⑤ 地面血渍 ─────────────────────────────────────────
        [Tooltip("是否在命中点下方地面留一片血渍。")]
        public bool groundStain;
        [Tooltip("血渍直径 = impactRadius × 此值。")]
        public float stainDiameter;
        [Tooltip("血渍寿命（秒）。")]
        public float stainLifetime;
        [Tooltip("血渍不透明度（0~1）。")]
        public float stainOpacity;
        [Tooltip("地面检测层。血渍靠向下打射线找到地面，命中点高度 = 敌人胸口（transform.position + 1）。")]
        public LayerMask groundMask;
        [Tooltip("地面检测射线长度 = impactRadius × 此值。")]
        public float groundProbeScale;

        /// <summary>默认参数（按 impactRadius = 0.9 m 调的，与判定球 1.8 m 对齐）。</summary>
        public static BloodImpactParams Default => new BloodImpactParams
        {
            pulseStartDiameter = 0.35f,
            pulseEndDiameter = 1.35f,
            pulseDuration = 0.10f,

            waveStartDiameter = 0.35f,
            waveEndDiameter = 1.70f,
            waveDuration = 0.18f,
            waveThickness = 0.05f,

            spikeCount = 4,
            spikeLength = 0.60f,
            spikeThickness = 0.075f,
            spikeGrowTime = 0.06f,
            spikeDuration = 0.22f,

            dropCount = 16,
            dropDiameter = 0.07f,
            dropSpeedMin = 3f,
            dropSpeedMax = 6f,
            dropLifetime = 0.5f,
            dropGravity = 11.8f,
            dropDrag = 1.2f,

            groundStain = true,
            stainDiameter = 0.9f,
            stainLifetime = 1.5f,
            stainOpacity = 0.5f,
            groundMask = ~0,
            groundProbeScale = 4f
        };
    }

    /// <summary>
    /// 撞击特效的<b>程序化</b>播放入口。所有视觉元素在运行时用代码生成
    /// （几何体 + 材质都现造），不依赖任何美术资源 —— 与 <see cref="StatusEffectVfxDriver"/>
    /// 同一思路，属于占位实现，正式粒子 / VFX Graph 资源到位后替换本类即可。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>各向同性</b>：<c>DamagePresentationPayload</c> 不携带命中法线，
    /// 所以整套特效不做方向依赖（脉冲球 / 随机向血刺 / 水平冲击波 / 球形血雾），
    /// 放在任何命中点上读感都成立。
    /// </para>
    /// <para>
    /// <b>可重叠播放</b>：撞击使魔无穿透上限，一次冲刺穿过一堆怪时同一帧会结算多次。
    /// <see cref="Play(Vector3, float, Color)"/> 每次都生成独立的 GameObject，
    /// 天然支持同时播多份；每次播放结束后自毁，不驻留。
    /// </para>
    /// </remarks>
    public static class BloodImpactVfx
    {
        /// <summary>血的主流颜色（暗红）。</summary>
        public static readonly Color BloodColor = new Color(0.62f, 0.035f, 0.035f, 1f);
        /// <summary>高速血雾 / 火花用的亮红（配合自发光）。</summary>
        public static readonly Color BloodHot = new Color(1f, 0.18f, 0.10f, 1f);
        /// <summary>地面血渍颜色（比血雾更暗，避免地面一整片发亮）。</summary>
        public static readonly Color StainColor = new Color(0.26f, 0.012f, 0.016f, 1f);

        internal static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        internal static readonly int ColorId = Shader.PropertyToID("_Color");
        internal static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        private static Material s_SolidMaterial;
        private static Material s_LineMaterial;
        private static Mesh s_SphereMesh;
        private static Mesh s_PlaneMesh;

        private static readonly MaterialPropertyBlock s_Block = new MaterialPropertyBlock();

        /// <summary>实体件（脉冲球 / 血刺 / 血滴）材质：URP Lit + 自发光 + 透明。</summary>
        internal static Material SolidMaterial =>
            s_SolidMaterial != null ? s_SolidMaterial : (s_SolidMaterial = CreateSolidMaterial());

        /// <summary>无光照件（冲击波 / 地面血渍）材质：URP Unlit + 透明，颜色不被场景光照影响。</summary>
        internal static Material LineMaterial =>
            s_LineMaterial != null ? s_LineMaterial : (s_LineMaterial = CreateLineMaterial());

        /// <summary>共用的球 mesh（单位直径 1 m，程序化生成，不碰场景）。</summary>
        internal static Mesh SphereMesh
        {
            get
            {
                if (s_SphereMesh != null) return s_SphereMesh;
                s_SphereMesh = BuildSphereMesh(16, 12);
                return s_SphereMesh;
            }
        }

        /// <summary>共用的圆盘 mesh（XZ 平面、单位直径 1 m、法线朝上，用于地面血渍）。</summary>
        internal static Mesh PlaneMesh
        {
            get
            {
                if (s_PlaneMesh != null) return s_PlaneMesh;
                s_PlaneMesh = BuildDiscMesh(24);
                return s_PlaneMesh;
            }
        }

        /// <summary>
        /// 在世界坐标 <paramref name="position"/> 播一次撞击血爆（用默认参数）。
        /// </summary>
        /// <param name="position">命中点世界坐标。</param>
        /// <param name="radius">撞击判定半径（米），与 <c>impactRadius</c> 同源；0.9 ⇒ 整套特效按 1.8 m 判定球缩放。</param>
        /// <param name="color">血的颜色覆盖，传 default 用 <see cref="BloodColor"/>。</param>
        public static void Play(Vector3 position, float radius, Color color = default)
        {
            Play(position, radius, BloodImpactParams.Default, color);
        }

        /// <summary>
        /// 在世界坐标 <paramref name="position"/> 播一次撞击血爆。
        /// </summary>
        /// <param name="position">命中点世界坐标。</param>
        /// <param name="radius">撞击判定半径（米）。</param>
        /// <param name="parameters">分层可调参数（由场景上的驱动组件提供）。</param>
        /// <param name="color">血的颜色覆盖，传 default 用 <see cref="BloodColor"/>。</param>
        public static void Play(Vector3 position, float radius, in BloodImpactParams parameters, Color color = default)
        {
            if (color == default) color = BloodColor;
            if (radius <= 0.001f) radius = 0.9f;

            var go = new GameObject("BloodImpact");
            go.transform.position = position;
            go.AddComponent<BloodImpactBurst>().Build(radius, color, parameters);
        }

        /// <summary>把一个渲染器的颜色（含自发光）按 BaseColor + 自发光比例写入，实例间互不影响。</summary>
        internal static void SetRendererColor(Renderer renderer, Color color, float emissionBoost)
        {
            if (renderer == null) return;
            s_Block.Clear();
            s_Block.SetColor(BaseColorId, color);
            s_Block.SetColor(ColorId, color);          // Sprites/Default 兜底时使用
            if (emissionBoost > 0f)
            {
                s_Block.SetColor(EmissionColorId, new Color(
                    color.r * emissionBoost, color.g * emissionBoost, color.b * emissionBoost, 1f));
            }
            renderer.SetPropertyBlock(s_Block);
        }

        /// <summary>VFX 渲染器统一关阴影：占位几何体投出影子会很明显，且没有信息量。</summary>
        internal static void DisableShadows(Renderer renderer)
        {
            if (renderer == null) return;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        internal static void DestroyObject(UnityEngine.Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(obj);
            else UnityEngine.Object.DestroyImmediate(obj);
        }

        // ── 程序化网格 ──────────────────────────────────────────
        // 不用 GameObject.CreatePrimitive：它会在当前场景里生成临时物体（还会带一个 Collider），
        // 在编辑器里会弄脏用户打开的场景，运行时也白白多一次物理注册。

        private static Mesh BuildSphereMesh(int segments, int rings)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();

            verts.Add(new Vector3(0f, 0.5f, 0f));                       // 北极
            for (int r = 1; r < rings; r++)
            {
                float phi = Mathf.PI * r / rings;
                float y = Mathf.Cos(phi) * 0.5f;
                float radius = Mathf.Sin(phi) * 0.5f;
                for (int s = 0; s < segments; s++)
                {
                    float theta = Mathf.PI * 2f * s / segments;
                    verts.Add(new Vector3(Mathf.Cos(theta) * radius, y, Mathf.Sin(theta) * radius));
                }
            }
            verts.Add(new Vector3(0f, -0.5f, 0f));                      // 南极

            int north = 0;
            int south = verts.Count - 1;
            for (int s = 0; s < segments; s++)
            {
                int s1 = (s + 1) % segments;
                tris.Add(north); tris.Add(1 + s1); tris.Add(1 + s);
            }
            for (int r = 0; r < rings - 2; r++)
            {
                int rowA = 1 + r * segments;
                int rowB = 1 + (r + 1) * segments;
                for (int s = 0; s < segments; s++)
                {
                    int s1 = (s + 1) % segments;
                    tris.Add(rowA + s); tris.Add(rowB + s1); tris.Add(rowB + s);
                    tris.Add(rowA + s); tris.Add(rowA + s1); tris.Add(rowB + s1);
                }
            }
            int lastRow = 1 + (rings - 2) * segments;
            for (int s = 0; s < segments; s++)
            {
                int s1 = (s + 1) % segments;
                tris.Add(south); tris.Add(lastRow + s); tris.Add(lastRow + s1);
            }

            return FinishMesh(verts, tris, Vector3.zero);
        }

        private static Mesh BuildDiscMesh(int segments)
        {
            var verts = new List<Vector3> { Vector3.zero };
            var tris = new List<int>();
            for (int s = 0; s < segments; s++)
            {
                float theta = Mathf.PI * 2f * s / segments;
                verts.Add(new Vector3(Mathf.Cos(theta) * 0.5f, 0f, Mathf.Sin(theta) * 0.5f));
            }
            for (int s = 0; s < segments; s++)
            {
                int s1 = (s + 1) % segments;
                tris.Add(0); tris.Add(1 + s1); tris.Add(1 + s);
            }
            // 参考点取在圆盘下方 ⇒ 朝向判据为「法线朝上」。
            return FinishMesh(verts, tris, new Vector3(0f, -1f, 0f));
        }

        /// <summary>
        /// 收尾：自动把绕序修正为「朝向背离参考点」，这样法线一定朝外
        /// （Unity 的左手法线约定容易记错，与其猜不如算）。
        /// </summary>
        private static Mesh FinishMesh(List<Vector3> verts, List<int> tris, Vector3 interiorReference)
        {
            Vector3[] vertices = verts.ToArray();
            int[] triangles = tris.ToArray();

            float outwardScore = 0f;
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]];
                Vector3 b = vertices[triangles[i + 1]];
                Vector3 c = vertices[triangles[i + 2]];
                Vector3 faceNormal = Vector3.Cross(b - a, c - a);
                outwardScore += Vector3.Dot(faceNormal, (a + b + c) / 3f - interiorReference);
            }

            if (outwardScore < 0f)
            {
                for (int i = 0; i < triangles.Length; i += 3)
                {
                    int swap = triangles[i + 1];
                    triangles[i + 1] = triangles[i + 2];
                    triangles[i + 2] = swap;
                }
            }

            var mesh = new Mesh
            {
                name = "VH_BloodImpact_VfxMesh",
                hideFlags = HideFlags.HideAndDontSave
            };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Material CreateSolidMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return null;

            var mat = new Material(shader)
            {
                name = "VH_BloodImpact_Solid",
                hideFlags = HideFlags.HideAndDontSave
            };
            mat.SetColor(BaseColorId, BloodColor);
            mat.SetColor(ColorId, BloodColor);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor(EmissionColorId, BloodColor * 2.4f);
            ApplyTransparentSetup(mat);
            return mat;
        }

        private static Material CreateLineMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Sprites/Default")
                ?? Shader.Find("Unlit/Color")
                ?? Shader.Find("Standard");
            if (shader == null) return null;

            var mat = new Material(shader)
            {
                name = "VH_BloodImpact_Line",
                hideFlags = HideFlags.HideAndDontSave
            };
            mat.SetColor(BaseColorId, BloodColor);
            mat.SetColor(ColorId, BloodColor);
            ApplyTransparentSetup(mat);
            return mat;
        }

        /// <summary>URP 透明表面设置（Standard/Sprites 兜底时这些关键字不生效，退化为不透明，仍可看）。</summary>
        private static void ApplyTransparentSetup(Material mat)
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_AlphaClip", 0f);
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.renderQueue = (int)RenderQueue.Transparent;
        }
    }
}
