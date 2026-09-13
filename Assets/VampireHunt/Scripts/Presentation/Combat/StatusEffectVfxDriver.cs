using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Unity;

namespace VampireHunt.Presentation.Combat
{
    /// <summary>
    /// 敌人身上的战斗特效驱动，负责两件事：
    /// <list type="bullet">
    /// <item><b>元素异常状态</b>：用一颗自发光半透明球表示敌人身上的元素状态——
    /// 火 = 红橙、冰 = 青蓝、雷 = 黄白，随脉动呼吸。正式美术资源到位后替换即可。</item>
    /// <item><b>受击特效</b>：敌人在命中点播一次性的打击特效（<see cref="hitPrefab"/>）。</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// <para>
    /// 由 <see cref="CombatVfxPresenter"/> 在敌人身上驱动（按 entity id 过滤后调用），
    /// 球挂到敌人 transform 下、跟随移动；同状态叠层先清旧球再生成新的。
    /// </para>
    /// <para>
    /// <b>受击特效为什么放在这里</b>：本类本来就挂在敌人身上、本来就实现了 <c>ICombatVfxDriver</c>，
    /// 而 <see cref="CombatVfxPresenter"/> 已按 entity id 把事件过滤到「这一个敌人」，
    /// 所以受击特效天然只对敌人生效——不需要额外的目标类型判定。
    /// （全局共享的伤害事件频道里，玩家受伤与敌人受伤用的是同一条事件，payload 只区分伤害类型、
    /// 不区分目标类型，因此挂在敌人身上的驱动是唯一不用新字段的过滤点。）
    /// </para>
    /// <para>
    /// ⚠️ <b>命中点精度</b>：<c>payload.worldPosition</c> 目前是敌人 <c>transform.position + Vector3.up</c>
    /// （胸口高度），不是武器真实接触点。观感够用；要精确到接触点需要补一条带 <c>hitPoint</c> 的表现事件链路。
    /// </para>
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

        [Header("受击特效（命中点播一次）")]
        [Tooltip("敌人被命中时在命中点播放的预制体。留空 = 不播受击特效（保持原行为）。")]
        [SerializeField] private GameObject hitPrefab;
        [Tooltip("受击特效整体缩放。预制体按自身尺寸制作，运行时按此值缩放（用于把包自带的「大招级」尺寸压到怪物体量）。")]
        [SerializeField, Min(0.01f)] private float hitScale = 0.3f;
        [Tooltip("同一敌人的最小播放间隔（秒）。避免多段伤害同帧命中时叠出一团光。0 = 不限制。")]
        [SerializeField, Min(0f)] private float hitInterval = 0.05f;
        [Tooltip("勾选后：预制体里 looping = true 的粒子系统不播放，只播一次性层。命中类特效保持勾选；若预制体本身是循环型则取消勾选。")]
        [SerializeField] private bool hitSkipLoopingSystems = true;

        private readonly Dictionary<uint, GlowOrb> m_Orbs = new Dictionary<uint, GlowOrb>();
        private Material m_FireMaterial;
        private Material m_IceMaterial;
        private Material m_LightningMaterial;
        private float m_LastHitPlayTime = float.NegativeInfinity;

        /// <summary>命中瞬间：在命中点播一次受击特效；未配置 <see cref="hitPrefab"/> 时不做任何事。</summary>
        public void PlayDamage(in DamagePresentationPayload payload, Transform anchor)
        {
            if (hitPrefab == null) return;

            float now = Time.unscaledTime;
            if (hitInterval > 0f && now - m_LastHitPlayTime < hitInterval) return;
            m_LastHitPlayTime = now;

            Vector3 position = payload.worldPosition;
            if (position.sqrMagnitude < 0.0001f)
            {
                if (anchor == null) return;
                position = anchor.position;
            }

            HitVfxPool.Play(hitPrefab, position, hitScale, hitSkipLoopingSystems);
        }

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

        /// <summary>
        /// 全局共享的受击特效对象池。
        /// </summary>
        /// <remarks>
        /// <para>
        /// <b>为什么是静态的</b>：驱动组件是「每只敌人一份」的，若每份各自建池，
        /// 一次刷 50 只怪就会预生成上百个实例。命中特效是纯世界坐标的瞬时事件，
        /// 不属于任何实体，所以整个进程共用一个池、一个上限。
        /// </para>
        /// <para>
        /// <b>为什么需要 ticker</b>：静态类没有 Update，播放中的实例需要按时回收。
        /// 运行时在池根节点上挂一个隐藏的 <see cref="HitVfxPoolTicker"/> 负责驱动。
        /// </para>
        /// <para>
        /// <b>场景切换</b>：池根节点随场景销毁，静态引用会变成 Unity 的「已销毁」空引用，
        /// 下一次播放时检测到根节点为空即重建池（同时清掉失效的实例与缓存）。
        /// </para>
        /// </remarks>
        private static class HitVfxPool
        {
            private const int Prewarm = 6;
            private const int MaxLive = 24;
            private const float FallbackLifetime = 0.6f;

            private static GameObject s_Root;
            private static GameObject s_Prefab;
            private static float s_Lifetime = FallbackLifetime;
            private static readonly Stack<GameObject> s_Idle = new Stack<GameObject>();
            private static readonly List<Live> s_Live = new List<Live>();
            private static readonly Dictionary<GameObject, ParticleSystem[]> s_Systems =
                new Dictionary<GameObject, ParticleSystem[]>();

            public static void Play(GameObject prefab, Vector3 position, float scale, bool skipLooping)
            {
                if (prefab == null) return;
                if (s_Root == null || s_Prefab != prefab) Rebuild(prefab);
                if (s_Root == null) return;

                Tick(Time.time);

                GameObject go = Rent();
                if (go == null) return;

                go.transform.SetPositionAndRotation(position, Quaternion.identity);
                go.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);
                go.SetActive(true);

                ParticleSystem[] systems = s_Systems[go];
                for (int i = 0; i < systems.Length; i++)
                {
                    ParticleSystem ps = systems[i];
                    if (ps == null) continue;

                    // ⚠️ ps.main 返回结构体副本（property getter），读字段没问题，写字段必须先取局部变量再整体赋回。
                    ParticleSystem.MainModule main = ps.main;
                    if (skipLooping && main.loop)
                    {
                        // 循环层（通常是包的「常驻底光」）不参与一次性命中表现，停掉以免残留。
                        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                        continue;
                    }

                    ps.Clear(true);
                    ps.Play(false);
                }

                s_Live.Add(new Live(go, Time.time + s_Lifetime));
            }

            public static void Tick(float now)
            {
                for (int i = s_Live.Count - 1; i >= 0; i--)
                {
                    if (now < s_Live[i].ExpireAt) continue;
                    Recycle(s_Live[i].Go);
                    s_Live.RemoveAt(i);
                }
            }

            private static void Rebuild(GameObject prefab)
            {
                DestroyRoot();

                s_Prefab = prefab;
                s_Lifetime = ResolveLifetime(prefab);

                s_Root = new GameObject("HitVfxPool");
                s_Root.AddComponent<HitVfxPoolTicker>();

                for (int i = 0; i < Prewarm; i++)
                {
                    GameObject instance = CreateInstance();
                    if (instance == null) break;
                    instance.SetActive(false);
                    s_Idle.Push(instance);
                }
            }

            private static void DestroyRoot()
            {
                if (s_Root != null) UnityEngine.Object.Destroy(s_Root);
                s_Root = null;
                s_Prefab = null;
                s_Idle.Clear();
                s_Live.Clear();
                s_Systems.Clear();
            }

            private static GameObject Rent()
            {
                while (s_Idle.Count > 0)
                {
                    GameObject candidate = s_Idle.Pop();
                    if (candidate != null) return candidate;
                }

                // 池空了但还有在播的：回收最早那个，保证同屏实例数不超过上限。
                if (s_Live.Count >= MaxLive)
                {
                    GameObject oldest = s_Live[0].Go;
                    s_Live.RemoveAt(0);
                    Recycle(oldest);
                    while (s_Idle.Count > 0)
                    {
                        GameObject candidate = s_Idle.Pop();
                        if (candidate != null) return candidate;
                    }
                }

                return CreateInstance();
            }

            private static void Recycle(GameObject go)
            {
                if (go == null) return;
                if (s_Systems.TryGetValue(go, out ParticleSystem[] systems))
                {
                    for (int i = 0; i < systems.Length; i++)
                    {
                        if (systems[i] == null) continue;
                        systems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    }
                }
                go.SetActive(false);
                s_Idle.Push(go);
            }

            private static GameObject CreateInstance()
            {
                if (s_Prefab == null || s_Root == null) return null;
                GameObject go = UnityEngine.Object.Instantiate(s_Prefab, s_Root.transform);
                if (go == null) return null;
                s_Systems[go] = go.GetComponentsInChildren<ParticleSystem>(true);
                return go;
            }

            /// <summary>
            /// 结算实例存活时长：只统计一次性（非循环）系统的最长「持续时间 + 粒子寿命」。
            /// 循环层不参与（它们在命中表现里被跳过），否则包里的常驻底光会把寿命撑到十几秒。
            /// </summary>
            private static float ResolveLifetime(GameObject prefab)
            {
                float longest = 0f;
                ParticleSystem[] systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
                for (int i = 0; i < systems.Length; i++)
                {
                    ParticleSystem.MainModule main = systems[i].main;
                    if (main.loop) continue;
                    float total = main.duration + main.startLifetime.constantMax;
                    if (total > longest) longest = total;
                }
                return Mathf.Max(0.1f, longest > 0f ? longest : FallbackLifetime);
            }

            private readonly struct Live
            {
                public readonly GameObject Go;
                public readonly float ExpireAt;

                public Live(GameObject go, float expireAt)
                {
                    Go = go;
                    ExpireAt = expireAt;
                }
            }
        }

        /// <summary>池的 Update 驱动，运行时挂到池根节点上；随根节点一起销毁。</summary>
        private sealed class HitVfxPoolTicker : MonoBehaviour
        {
            private void Update() => HitVfxPool.Tick(Time.time);
        }
    }
}
