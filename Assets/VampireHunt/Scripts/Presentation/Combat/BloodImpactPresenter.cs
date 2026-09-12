using System.Collections.Generic;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Unity;

namespace VampireHunt.Presentation.Combat
{
    /// <summary>
    /// 撞击使魔的<b>撞击血爆</b>网关：订阅全局伤害呈现事件，把「使魔撞击伤害」翻成一次血爆。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>两种播放方式，靠 <see cref="impactPrefab"/> 切换：</b>
    /// </para>
    /// <list type="bullet">
    /// <item><b>预制体模式</b>（推荐）：指定 Shuriken 预制体，走对象池实例化。
    /// 所有形状/数量/时长/配色都在 Inspector 里调，改特效不用改代码。</item>
    /// <item><b>程序化模式</b>（兜底）：留空则调用 <see cref="BloodImpactVfx"/>，
    /// 运行时用代码现场造几何 + 材质，不依赖任何美术资产。</item>
    /// </list>
    /// <para>
    /// <b>为什么不做成 <see cref="ICombatVfxDriver"/></b>：<see cref="CombatVfxPresenter"/> 只认
    /// <b>一个</b> driver（未指定时回退到同物体上的任意实现），而它挂在敌人身上、已经在驱动元素光晕
    /// （<see cref="StatusEffectVfxDriver"/>）。把血爆塞进去会和元素表现抢同一个槽位。
    /// 撞击血爆是「世界坐标上的瞬时事件」，不属于任何实体，所以独立成一个场景级订阅者，
    /// 与 <see cref="VampireHunt.Presentation.Audio.EntityDamageAudio"/> 同一模式。
    /// </para>
    /// <para>
    /// <b>为什么零代码改动就能接上</b>：<c>DamagePresentationPayload</c> 已经带
    /// <c>tags</c>（区分伤害来源）与 <c>worldPosition</c>（命中位置），
    /// 而撞击使魔的 <c>WeaponTag</c> 默认就是 <see cref="DamageTags.Familiar"/>。
    /// 事件由服务器 RPC 广播到所有端，所以每个客户端都会看到血爆。
    /// </para>
    /// <para>
    /// ⚠️ <b>命中点精度</b>：<c>worldPosition</c> 现在是敌人 <c>transform.position + Vector3.up</c>
    /// （胸口高度），不是剑刃真实接触点。观感上够用；若要精确到接触点，
    /// 需要给撞击使魔补一条带 <c>hitPoint</c> 的表现事件链路（见设计文档第四节）。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BloodImpactPresenter : MonoBehaviour
    {
        [Header("事件源")]
        [Tooltip("全局伤害呈现事件频道（与 CombatVfxPresenter / EntityDamageAudio 填同一个资源）。")]
        [SerializeField] private DamagePresentationEvent onDamage;
        [Tooltip("只对带这些标签的伤害播血爆。使魔撞击默认 Familiar。")]
        [SerializeField] private DamageTags matchTags = DamageTags.Familiar;

        [Header("播放方式")]
        [Tooltip("撞击血爆预制体（Shuriken）。指定后走粒子路径；留空则回退到程序化版本。")]
        [SerializeField] private GameObject impactPrefab;
        [Tooltip("血渍预制体（可选）。仅预制体模式下生效；程序化模式的血渍内建在 BloodImpactBurst 里。")]
        [SerializeField] private GameObject stainPrefab;

        [Header("尺寸")]
        [Tooltip("撞击判定半径（米）。应与撞击使魔资产上的 impactRadius 保持一致。")]
        [SerializeField, Min(0.05f)] private float impactRadius = 0.9f;
        [Tooltip("预制体是按这个判定半径做的。运行时按 impactRadius / 此值 缩放整个物体。")]
        [SerializeField, Min(0.05f)] private float prefabReferenceRadius = 0.9f;
        [Tooltip("程序化模式使用的血颜色。")]
        [SerializeField] private Color bloodColor = new Color(0.62f, 0.035f, 0.035f, 1f);
        [Tooltip("用 bloodColor 覆盖粒子起始颜色。关掉 = 完全由预制体/材质决定（推荐关，配色统一在材质里改）。")]
        [SerializeField] private bool overrideParticleColor = false;

        [Header("对象池")]
        [Tooltip("预热实例数。")]
        [SerializeField, Min(1)] private int poolPrewarm = 3;
        [Tooltip("池上限；超出时回收最早的一个，保证同屏多把使魔同时命中也不会无限实例化。")]
        [SerializeField, Min(1)] private int poolMax = 16;

        [Header("血渍（预制体模式）")]
        [Tooltip("是否在命中点下方地面留一片血渍。")]
        [SerializeField] private bool groundStain = true;
        [Tooltip("地面检测层。")]
        [SerializeField] private LayerMask groundMask = ~0;
        [Tooltip("向下探地面的射线长度（米）。")]
        [SerializeField, Min(0.5f)] private float groundProbeDistance = 4f;

        [Header("节流")]
        [Tooltip("同一目标的最小播放间隔（秒）。多把使魔同帧命中同一敌人时，避免同一位置叠出一团白。0 = 不限制。")]
        [SerializeField, Min(0f)] private float sameTargetInterval = 0.05f;

        [Header("程序化回退参数")]
        [SerializeField] private BloodImpactParams parameters = BloodImpactParams.Default;

        private PrefabPool m_ImpactPool;
        private PrefabPool m_StainPool;

        private readonly Dictionary<ulong, float> m_LastPlayed = new Dictionary<ulong, float>();
        private readonly List<ulong> m_Scratch = new List<ulong>();

        private void Awake()
        {
            if (impactPrefab == null) return;

            var container = new GameObject("BloodImpactPool");
            container.transform.SetParent(transform, false);
            container.transform.localPosition = Vector3.zero;
            container.transform.localRotation = Quaternion.identity;
            container.transform.localScale = Vector3.one;

            m_ImpactPool = new PrefabPool(impactPrefab, container.transform, poolPrewarm, poolMax);
            if (stainPrefab != null && groundStain)
                m_StainPool = new PrefabPool(stainPrefab, container.transform, Mathf.Max(2, poolPrewarm), poolMax);
        }

        private void OnEnable()
        {
            onDamage?.RegisterListener(HandleDamage);
        }

        private void OnDisable()
        {
            onDamage?.UnregisterListener(HandleDamage);
            m_LastPlayed.Clear();
            m_ImpactPool?.Clear();
            m_StainPool?.Clear();
        }

        private void Update()
        {
            float now = Time.time;
            m_ImpactPool?.Tick(now);
            m_StainPool?.Tick(now);
        }

        /// <summary>在指定世界坐标播一次血爆（供调试与脚本调用）。</summary>
        public void PlayAt(Vector3 position)
        {
            PlayImpact(position);
        }

        [ContextMenu("试播一次（本物体前方 3 m）")]
        private void DebugPlay()
        {
            PlayAt(transform.position + Vector3.up + transform.forward * 3f);
        }

#if UNITY_EDITOR
        /// <summary>
        /// 编辑模式专用：清掉「试播一次」留下的孤儿血爆。
        /// </summary>
        /// <remarks>
        /// ⚠️ 编辑模式下 MonoBehaviour 的 <c>Update</c> <b>不会执行</b>（同理 <c>Awake</c>/<c>OnEnable</c>
        /// 也不会），所以 <see cref="BloodImpactBurst"/> 的自动销毁逻辑不会跑 —— 每点一次「试播一次」，
        /// 场景里就多一份 23 个子物体的残留。而且此时对象池<b>还没建起来</b>
        /// （<c>m_ImpactPool</c> 为 null），所以走的必然是程序化兜底路径。
        /// 这个菜单把残留一次清干净，支持 Undo。
        /// </remarks>
        [ContextMenu("清除试播残留（血爆）")]
        private void ClearDebugLeftovers()
        {
            BloodImpactBurst[] bursts = UnityEngine.Object.FindObjectsByType<BloodImpactBurst>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            int removed = 0;
            for (int i = 0; i < bursts.Length; i++)
            {
                if (bursts[i] == null) continue;
                UnityEditor.Undo.DestroyObjectImmediate(bursts[i].gameObject);
                removed++;
            }

            Debug.Log($"[BloodImpactPresenter] 已清除 {removed} 份试播残留（血爆）。", this);
        }
#endif

        private void HandleDamage(DamagePresentationPayload payload)
        {
            if (payload.targetEntityId == 0) return;
            if (matchTags != DamageTags.None && (payload.tags & matchTags) == 0) return;
            if (!TryConsume(payload.targetEntityId)) return;

            PlayImpact(payload.worldPosition);
        }

        private void PlayImpact(Vector3 position)
        {
            if (m_ImpactPool != null)
            {
                float scale = impactRadius / Mathf.Max(0.0001f, prefabReferenceRadius);
                m_ImpactPool.Spawn(position, scale, overrideParticleColor, bloodColor);
                if (m_StainPool != null) TrySpawnStain(position);
                return;
            }

            BloodImpactVfx.Play(position, impactRadius, parameters, bloodColor);
        }

        private void TrySpawnStain(Vector3 position)
        {
            if (!groundStain) return;
            Vector3 origin = position + Vector3.up * 0.2f;
            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, groundProbeDistance,
                    groundMask, QueryTriggerInteraction.Ignore))
            {
                return;
            }

            float scale = impactRadius / Mathf.Max(0.0001f, prefabReferenceRadius);
            m_StainPool.Spawn(hit.point + Vector3.up * 0.01f, scale, overrideParticleColor, bloodColor);
        }

        private bool TryConsume(ulong targetEntityId)
        {
            float now = Time.unscaledTime;
            if (sameTargetInterval > 0f
                && m_LastPlayed.TryGetValue(targetEntityId, out float last)
                && now - last < sameTargetInterval)
            {
                return false;
            }

            m_LastPlayed[targetEntityId] = now;
            if (m_LastPlayed.Count > 64) PruneStale(now);
            return true;
        }

        private void PruneStale(float now)
        {
            float cutoff = now - Mathf.Max(1f, sameTargetInterval);
            m_Scratch.Clear();
            foreach (KeyValuePair<ulong, float> entry in m_LastPlayed)
                if (entry.Value < cutoff) m_Scratch.Add(entry.Key);
            for (int i = 0; i < m_Scratch.Count; i++) m_LastPlayed.Remove(m_Scratch[i]);
        }

        /// <summary>
        /// 极简对象池：每次命中 <c>Instantiate</c> / <c>Destroy</c> 会在怪群密集时造成 GC 抖动，
        /// 所以预生成 + 复用一个固定上限的实例集。池跟着本物体一起销毁（实例是它的子物体）。
        /// </summary>
        private sealed class PrefabPool
        {
            private readonly GameObject m_Prefab;
            private readonly Transform m_Root;
            private readonly int m_Max;
            private readonly float m_Lifetime;
            private readonly Stack<GameObject> m_Idle = new Stack<GameObject>();
            private readonly List<Entry> m_Live = new List<Entry>();
            private readonly Dictionary<GameObject, ParticleSystem[]> m_Systems = new Dictionary<GameObject, ParticleSystem[]>();

            public PrefabPool(GameObject prefab, Transform root, int prewarm, int max)
            {
                m_Prefab = prefab;
                m_Root = root;
                m_Max = Mathf.Max(1, max);
                m_Lifetime = ResolveLifetime(prefab);

                for (int i = 0; i < Mathf.Max(0, prewarm); i++)
                {
                    GameObject instance = CreateInstance();
                    if (instance == null) break;
                    instance.SetActive(false);
                    m_Idle.Push(instance);
                }
            }

            public void Spawn(Vector3 position, float scale, bool overrideColor, Color color)
            {
                GameObject go = Rent();
                if (go == null) return;

                go.transform.SetPositionAndRotation(position, Quaternion.identity);
                go.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);
                go.SetActive(true);

                ParticleSystem[] systems = m_Systems[go];
                for (int i = 0; i < systems.Length; i++)
                {
                    ParticleSystem ps = systems[i];
                    if (ps == null) continue;

                    // ⚠️ ps.main 返回的是结构体副本（property getter），不能直接写它的字段，
                    //    否则 CS1612。必须先取到局部变量再改，最后整体赋回。
                    ParticleSystem.MainModule main = ps.main;
                    if (overrideColor) main.startColor = new ParticleSystem.MinMaxGradient(color);

                    ps.Clear(true);
                    ps.Play(false);
                }

                m_Live.Add(new Entry(go, Time.time + m_Lifetime));
            }

            public void Tick(float now)
            {
                for (int i = m_Live.Count - 1; i >= 0; i--)
                {
                    if (now < m_Live[i].ExpireAt) continue;
                    Recycle(m_Live[i].Go);
                    m_Live.RemoveAt(i);
                }
            }

            public void Clear()
            {
                for (int i = 0; i < m_Live.Count; i++) Recycle(m_Live[i].Go);
                m_Live.Clear();
            }

            private GameObject Rent()
            {
                if (m_Idle.Count > 0) return m_Idle.Pop();

                // 池空了但还有在播的：回收最早那个，保证实例数不超过上限。
                if (m_Live.Count >= m_Max)
                {
                    GameObject oldest = m_Live[0].Go;
                    m_Live.RemoveAt(0);
                    Recycle(oldest);
                    if (m_Idle.Count > 0) return m_Idle.Pop();
                }

                return CreateInstance();
            }

            private void Recycle(GameObject go)
            {
                if (go == null) return;
                if (m_Systems.TryGetValue(go, out ParticleSystem[] systems))
                {
                    for (int i = 0; i < systems.Length; i++)
                    {
                        if (systems[i] == null) continue;
                        systems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    }
                }
                go.SetActive(false);
                m_Idle.Push(go);
            }

            private GameObject CreateInstance()
            {
                if (m_Prefab == null || m_Root == null) return null;
                GameObject go = Object.Instantiate(m_Prefab, m_Root);
                if (go == null) return null;
                m_Systems[go] = go.GetComponentsInChildren<ParticleSystem>(true);
                return go;
            }

            private static float ResolveLifetime(GameObject prefab)
            {
                float longest = 0f;
                ParticleSystem[] systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
                for (int i = 0; i < systems.Length; i++)
                {
                    ParticleSystem.MainModule main = systems[i].main;
                    float total = main.duration + main.startLifetime.constantMax;
                    if (total > longest) longest = total;
                }
                return Mathf.Max(0.05f, longest);
            }

            private readonly struct Entry
            {
                public readonly GameObject Go;
                public readonly float ExpireAt;

                public Entry(GameObject go, float expireAt)
                {
                    Go = go;
                    ExpireAt = expireAt;
                }
            }
        }
    }
}
