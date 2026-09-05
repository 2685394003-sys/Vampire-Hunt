using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Unity;

namespace VampireHunt.Presentation.Combat
{
    /// <summary>
    /// 元素异常状态的<b>占位</b>特效驱动：实现 <see cref="ICombatVfxDriver"/>，
    /// 用一颗自发光半透明球表示敌人身上的元素状态——
    /// 火 = 红橙、冰 = 青蓝、雷 = 黄白，随脉动呼吸。
    /// 正式美术资源（粒子/材质）到位后替换本类即可，不污染玩法层。
    /// </summary>
    /// <remarks>
    /// 由 <see cref="CombatVfxPresenter"/> 在敌人身上驱动（按 entity id 过滤后调用），
    /// 球挂到敌人 transform 下、跟随移动；同状态叠层先清旧球再生成新的。
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class StatusEffectVfxDriver : MonoBehaviour, ICombatVfxDriver, ILightningChainPresentationSink
    {
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

            ElementId element = ResolveElement(payload.element, payload.statusId);
            if (element == ElementId.None) return;

            // 同状态先清旧球（叠层会触发 Remove+Add，避免残留旧球）。
            if (m_Orbs.TryGetValue(payload.statusId, out GlowOrb previous) && previous.Visual != null)
                Destroy(previous.Visual);
            m_Orbs.Remove(payload.statusId);

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
            if (!m_Orbs.TryGetValue(payload.statusId, out GlowOrb orb)) return;
            m_Orbs.Remove(payload.statusId);
            if (orb.Visual != null) Destroy(orb.Visual);
        }

        public void Clear(Transform anchor)
        {
            foreach (GlowOrb orb in m_Orbs.Values)
                if (orb.Visual != null) Destroy(orb.Visual);
            m_Orbs.Clear();
        }

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
