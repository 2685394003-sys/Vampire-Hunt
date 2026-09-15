using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Unity;

namespace VampireHunt.Presentation.Combat
{
    /// <summary>
    /// Presentation-only status driver. Burn and frozen use shared Piloto mesh prefabs;
    /// other elemental statuses retain their placeholder orbs.
    /// </summary>
    /// <remarks>
    /// 由 <see cref="CombatVfxPresenter"/> 在敌人身上驱动（按 entity id 过滤后调用），
    /// Mesh overlays bind to the enemy's renderers. Stack updates retain existing instances.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class StatusEffectVfxDriver : MonoBehaviour, ICombatVfxDriver, ILightningChainPresentationSink
    {
        [Header("燃烧 / 冻结网格特效")]
        [SerializeField] private StatusMeshVfxConfig meshVfxConfig;
        [Tooltip("可选：指定身体网格。留空时自动覆盖所有蒙皮网格，没有蒙皮网格时使用普通网格。")]
        [SerializeField] private Renderer[] meshTargets;
        [Tooltip("可选：指定粒子的发射网格；分体模型建议指定躯干，表面材质仍覆盖所有目标网格。")]
        [SerializeField] private Renderer particleTarget;

        [Header("占位发光球")]
        [Tooltip("发光球直径（米），大致包住敌人上半身。")]
        [SerializeField, Min(0.1f)] private float glowDiameter = 1.6f;
        [Tooltip("发光球相对敌人脚底的高度偏移（米）。")]
        [SerializeField] private float glowHeight = 1f;
        [Tooltip("脉动幅度（0 = 不脉动，纯静止发光）。")]
        [SerializeField] private float pulseAmplitude = 0.15f;
        [Tooltip("脉动频率（次/秒）。")]
        [SerializeField] private float pulseFrequency = 3f;
        [Tooltip("发光球不透明度（0~1）。")]
        [SerializeField, Range(0.05f, 1f)] private float glowOpacity = 0.4f;

        private readonly Dictionary<uint, GlowOrb> m_Orbs = new Dictionary<uint, GlowOrb>();
        private readonly Dictionary<uint, GameObject> m_MeshEffects = new Dictionary<uint, GameObject>();
        private readonly List<Renderer> m_MeshTargets = new List<Renderer>();
        private bool m_WarnedMissingMeshSetup;
        private Material m_FireMaterial;
        private Material m_IceMaterial;
        private Material m_LightningMaterial;

        /// <summary>占位：元素测试暂不需要受击表现，留空。</summary>
        public void PlayDamage(in DamagePresentationPayload payload, Transform anchor) { }

        public void PlayLightningChain(in LightningChainPresentationCue cue)
        {
            LightningChainVisual.Play(ToVector3(cue.From), ToVector3(cue.To), cue.Intensity);
        }

        public void ApplyStatus(in StatusEffectPresentationPayload payload, Transform anchor)
        {
            if (payload.statusId == 0) return;

            if (payload.statusId == StatusEffectIds.Burn || payload.statusId == StatusEffectIds.Frozen)
            {
                ApplyMeshStatus(payload.statusId);
                return;
            }

            ElementId element = ResolveElement(payload.element, payload.statusId);
            if (element == ElementId.None) return;

            if (m_Orbs.TryGetValue(payload.statusId, out GlowOrb previous) && previous.Visual != null) return;

            GameObject orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Collider collider = orb.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            orb.name = $"ElementGlow_{element}_{payload.statusId}";

            if (anchor != null)
            {
                orb.transform.SetParent(anchor, false);
                orb.transform.localPosition = new Vector3(0f, glowHeight, 0f);
            }
            else
            {
                orb.transform.position = payload.worldPosition + Vector3.up * glowHeight;
            }
            orb.transform.localScale = Vector3.one * glowDiameter;

            MeshRenderer renderer = orb.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = MaterialFor(element);

            m_Orbs[payload.statusId] = new GlowOrb(orb);
        }

        public void RemoveStatus(in StatusEffectPresentationPayload payload, Transform anchor)
        {
            if (m_MeshEffects.TryGetValue(payload.statusId, out GameObject meshEffect))
            {
                m_MeshEffects.Remove(payload.statusId);
                ReleaseVisual(meshEffect);
            }
            if (!m_Orbs.TryGetValue(payload.statusId, out GlowOrb orb)) return;
            m_Orbs.Remove(payload.statusId);
            ReleaseVisual(orb.Visual);
        }

        public void Clear(Transform anchor)
        {
            foreach (GameObject visual in m_MeshEffects.Values) ReleaseVisual(visual);
            m_MeshEffects.Clear();
            foreach (GlowOrb orb in m_Orbs.Values)
                ReleaseVisual(orb.Visual);
            m_Orbs.Clear();
        }

        private void ApplyMeshStatus(uint statusId)
        {
            if (m_MeshEffects.TryGetValue(statusId, out GameObject current) && current != null) return;
            GameObject prefab = meshVfxConfig != null ? meshVfxConfig.GetPrefab(statusId) : null;
            ResolveMeshTargets();
            if (prefab == null || m_MeshTargets.Count == 0)
            {
                if (!m_WarnedMissingMeshSetup)
                {
                    Debug.LogWarning("[StatusEffectVfxDriver] Missing mesh VFX config or character renderer.", this);
                    m_WarnedMissingMeshSetup = true;
                }
                return;
            }

            // Bind while inactive: OverlayFX is ExecuteAlways and particles play on enable.
            var root = new GameObject($"StatusMeshVFX_{statusId}");
            root.SetActive(false);
            root.transform.SetParent(transform, false);
            GameObject instance = Instantiate(prefab, root.transform, false);
            var overlays = instance.GetComponentsInChildren<OverlayFX>(true);
            if (overlays.Length == 0)
            {
                Debug.LogWarning("[StatusEffectVfxDriver] Mesh prefab requires OverlayFX.", this);
                ReleaseVisual(root);
                return;
            }

            Renderer emitter = particleTarget != null && m_MeshTargets.Contains(particleTarget)
                ? particleTarget : LargestTarget();
            foreach (OverlayFX overlay in overlays)
            {
                // Imported prefabs contain incomplete particle lists (including a missing
                // fire reference). Bind every emitter, including the root particle system.
                overlay.particleSystems = new List<ParticleSystem>(overlay.GetComponentsInChildren<ParticleSystem>(true));
                overlay.SetTargetRenderer(emitter);
                // Extra body parts need only a material overlay, not another particle emitter.
                foreach (Renderer target in m_MeshTargets)
                {
                    if (target == emitter) continue;
                    var part = new GameObject($"Overlay_{target.name}");
                    part.transform.SetParent(root.transform, false);
                    var surface = part.AddComponent<OverlayFX>();
                    surface.overlayMaterial = overlay.overlayMaterial;
                    surface.rendererTrueForward = overlay.rendererTrueForward;
                    surface.SetTargetRenderer(target);
                }
            }
            m_MeshEffects[statusId] = root;
            root.SetActive(true);
        }

        private void ResolveMeshTargets()
        {
            m_MeshTargets.Clear();
            if (meshTargets != null && meshTargets.Length > 0)
            {
                foreach (Renderer target in meshTargets) AddMeshTarget(target);
                return;
            }
            foreach (SkinnedMeshRenderer target in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                AddMeshTarget(target);
            if (m_MeshTargets.Count != 0) return;
            foreach (MeshRenderer target in GetComponentsInChildren<MeshRenderer>(true))
            {
                if (target.GetComponentInParent<OverlayFX>() != null || target.name.StartsWith("ElementGlow_")) continue;
                AddMeshTarget(target);
            }
        }

        private void AddMeshTarget(Renderer target)
        {
            if (target == null || m_MeshTargets.Contains(target)) return;
            bool valid = target is SkinnedMeshRenderer skinned && skinned.sharedMesh != null ||
                         target is MeshRenderer && target.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null;
            if (valid) m_MeshTargets.Add(target);
        }

        private Renderer LargestTarget()
        {
            Renderer best = m_MeshTargets[0];
            foreach (Renderer candidate in m_MeshTargets)
                if (candidate.bounds.size.sqrMagnitude > best.bounds.size.sqrMagnitude) best = candidate;
            return best;
        }

        private static void ReleaseVisual(GameObject visual)
        {
            if (visual == null) return;
            // Detach overlay materials synchronously, before Unity's deferred destruction.
            visual.SetActive(false);
            if (Application.isPlaying) Destroy(visual);
            else DestroyImmediate(visual);
        }

        private void OnDisable() => Clear(null);

        private void Update()
        {
            if (m_Orbs.Count == 0 || pulseAmplitude <= 0.0001f) return;
            float wave = 1f + pulseAmplitude * Mathf.Sin(Time.time * pulseFrequency * Mathf.PI * 2f);
            foreach (GlowOrb orb in m_Orbs.Values)
            {
                if (orb.Visual == null) continue;
                orb.Visual.transform.localScale = Vector3.one * glowDiameter * wave;
            }
        }

        private Material MaterialFor(ElementId element)
        {
            switch (element)
            {
                case ElementId.Ice: return GetOrCreate(ref m_IceMaterial, new Color(0.2f, 0.65f, 1f));
                case ElementId.Lightning: return GetOrCreate(ref m_LightningMaterial, new Color(1f, 0.85f, 0.35f));
                case ElementId.Fire:
                default: return GetOrCreate(ref m_FireMaterial, new Color(1f, 0.3f, 0.08f));
            }
        }

        private Material GetOrCreate(ref Material slot, Color color)
        {
            if (slot != null) return slot;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material mat = new Material(shader);
            mat.SetColor("_BaseColor", new Color(color.r, color.g, color.b, glowOpacity));
            mat.SetColor("_EmissionColor", new Color(color.r * 2.4f, color.g * 2.4f, color.b * 2.4f, 1f));
            mat.EnableKeyword("_EMISSION");

            // URP 透明表面：让敌人透过光晕可见（Standard 兜底时这些关键字不生效，退化为不透明发光球）。
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_AlphaClip", 0f);
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;

            slot = mat;
            return mat;
        }

        private static ElementId ResolveElement(ElementId element, uint statusId)
        {
            if (element != ElementId.None) return element;
            switch (statusId)
            {
                case StatusEffectIds.Burn:
                case StatusEffectIds.Explode: return ElementId.Fire;
                case StatusEffectIds.Frost:
                case StatusEffectIds.Frozen: return ElementId.Ice;
                case StatusEffectIds.Lightning:
                case StatusEffectIds.Thunder: return ElementId.Lightning;
                default: return ElementId.None;
            }
        }

        private static Vector3 ToVector3(in Float3 value) => new Vector3(value.X, value.Y, value.Z);

        private void OnDestroy()
        {
            Clear(null);
            if (m_FireMaterial != null) Destroy(m_FireMaterial);
            if (m_IceMaterial != null) Destroy(m_IceMaterial);
            if (m_LightningMaterial != null) Destroy(m_LightningMaterial);
        }

        private sealed class GlowOrb
        {
            public readonly GameObject Visual;
            public GlowOrb(GameObject visual) => Visual = visual;
        }
    }
}
