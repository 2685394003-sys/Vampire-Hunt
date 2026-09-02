using System.Collections.Generic;
using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Unity;
using VampireHunt.Player.Abilities.Familiar;
using GameplayEntityId = VampireHunt.SharedKernel.EntityId;

namespace VampireHunt.Infrastructure.Integration
{
    /// <summary>
    /// 射击僚机使魔（gunner familiar）的控制器：管理<b>多只</b>使魔共存，驱动它们的状态机，
    /// 并在服务器权威地结算射击伤害。挂在玩家根物体上。
    /// </summary>
    /// <remarks>
    /// <b>索敌规则</b>（与撞击水滴使魔完全一致）：使魔<b>不会主动找怪</b>。它只通过
    /// <see cref="IServerCombatResolutionListener"/> 监听「玩家攻击命中敌人」的结算记录，
    /// 把被命中的敌人登记为候选目标（索敌范围 <c>AcquireRange</c> 内才算）。
    /// 使魔自身造成的伤害带 <c>DamageTags.Familiar</c> 标签，会被这里过滤掉，因此不会自触发。
    /// </remarks>
    /// <remarks>
    /// <b>战斗流程</b>：飞到距目标 <c>Weapon.Range</c> 米的站位 → 瞄准前摇 → 连发 → 射击冷却 → 下一套。
    /// 这四个状态<b>全程不可打断</b>；玩家中途改打别的敌人只会写进候选缓存，
    /// 必须等当前这一整套（含冷却）打完，在冷却结束的决策点才切换到最新候选目标。
    /// </remarks>
    /// <remarks>
    /// <b>伤害结算</b>：即时命中（hitscan）—— 开火帧做一次球形扫描（sphere cast），
    /// 按 <c>pierceCount</c> 取前 N 个敌人直接结算。曳光弹只是表现，不参与判定，
    /// 因此不需要网络生成的弹丸对象，也不会有弹道延迟导致的命中丢失。
    /// </remarks>
    /// <remarks>
    /// <b>网络</b>：状态机与伤害结算只在服务器推进，与圆型领域、水滴使魔一致。
    /// 视觉 GameObject 由服务器 Instantiate，联机时其他客户端暂时看不到（既有约定，待补网络同步）。
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class GunnerFamiliarController : MonoBehaviour, IServerCombatResolutionListener, IFamiliarPactTarget
    {
        private const float EnemyScanInterval = 0.2f;
        private const float MuzzleVfxLifetime = 1f;

        [Header("配置")]
        [Tooltip("使魔配置资产（asset）：所有可调参数与武器档位都在这里。")]
        [SerializeField] private GunnerFamiliarAsset definitionAsset;
        [Tooltip("使魔数量：多只各自独立维护候选目标缓存，沿轨道均匀分布、交战时绕目标散开。")]
        [SerializeField, Min(1)] private int familiarCount = 2;
        [Tooltip("开局即激活：血契系统未接线时用于单机/调试验证。")]
        [SerializeField] private bool activeOnStart;

        [Header("视觉")]
        [Tooltip("使魔本体的视觉预制体（prefab，可选）。留空则只有逻辑没有表现。")]
        [SerializeField] private GameObject familiarVisualPrefab;

        [Header("统一环绕队列")]
        [Tooltip("队列归属者（owner）：同一个 owner 名下的所有使魔（含别的种类，如撞击水滴）会排进同一条" +
                 "环绕队列。留空 = 自动取本物体的根（transform.root），通常是玩家根物体，保持留空即可。")]
        [SerializeField] private Transform orbitOwner;

        [Header("指针指令（血契「牵丝之契」 pactId 316）")]
        [Tooltip("指针指令源：提供鼠标世界坐标与左键按住状态。留空 = 自动从本物体查找。")]
        [SerializeField] private FamiliarPointerCommand pointerCommand;
        [Tooltip("是否启用「围绕鼠标」：由血契开启。关闭时使魔照常围绕玩家（默认行为，改动前后完全一致）。")]
        [SerializeField] private bool pointerOrbit;
        [Tooltip("按住左键时，以指针（鼠标）为圆心、这个半径内的敌人会被指派为使魔目标（米）。")]
        [SerializeField, Min(0.5f)] private float commandRadius = 3f;
        [Tooltip("指针指令目标的有效期（秒）：松手后多久回归「谁被打中就追谁」的被动索敌。\n" +
                 "调小 = 更听指挥、松手即散；调大 = 咬得更久。")]
        [SerializeField, Min(0.05f)] private float commandTargetLifetime = 1f;
        [Tooltip("环绕中心跟随指针的平滑速度：越大跟得越紧（鼠标一甩队列就到），越小越飘（有惯性感）。")]
        [SerializeField, Min(0.1f)] private float orbitCenterFollowLerp = 6f;
        [Tooltip("环绕中心离玩家的最大距离（米）：鼠标拖太远时把队列夹在这个半径上，防止使魔飞出索敌范围。\n" +
                 "0 = 不限制（使魔会一直跟到鼠标处，可能跑到离玩家很远的地方）。")]
        [SerializeField, Min(0f)] private float maxOrbitCenterDistance = 15f;

        [Header("弹道表现（武器档位没配时用它兜底）")]
        [Tooltip("曳光弹（子弹）预制体兜底：武器档位里没配 tracerPrefab 时用这个。" +
                 "再留空则程序生成一个发光小球，保证一定能看到子弹。")]
        [SerializeField] private GameObject fallbackTracerPrefab;
        [Tooltip("命中闪光预制体兜底：子弹飞到终点时播一下。")]
        [SerializeField] private GameObject fallbackImpactVfxPrefab;
        [Tooltip("枪口闪光预制体兜底。")]
        [SerializeField] private GameObject fallbackMuzzleVfxPrefab;
        [Tooltip("命中闪光存活时间（秒）。")]
        [SerializeField, Min(0.02f)] private float impactVfxLifetime = 0.14f;
        [Tooltip("曳光弹的整体缩放倍率（在预制体自身尺寸上再乘一层，方便整体调大调小）。")]
        [SerializeField, Min(0.05f)] private float tracerScaleMultiplier = 1f;

        [Header("引用（留空自动从本物体查找）")]
        [SerializeField] private NetworkObject networkObject;
        [SerializeField] private ServerCombatResolutionHost resolutionHost;
        [SerializeField] private CoreStatsHandler coreStats;

        [Header("调试")]
        [Tooltip("在 Console 打印候选登记、开火与武器切换，便于确认链路是否通。")]
        [SerializeField] private bool logToConsole;
        [Tooltip("把每次开火的射线画在 Scene 视图里（仅编辑器可见）。")]
        [SerializeField] private bool debugDrawShots;

        private readonly List<GunnerFamiliarBrain> m_Brains = new List<GunnerFamiliarBrain>();
        private readonly List<GameObject> m_Visuals = new List<GameObject>();
        private readonly Dictionary<GameplayEntityId, MonoBehaviour> m_EnemyCache =
            new Dictionary<GameplayEntityId, MonoBehaviour>();
        private readonly List<Tracer> m_Tracers = new List<Tracer>();
        /// <summary>指针指令索敌时按距离排序用的临时列表（复用，避免每帧分配）。</summary>
        private readonly List<Collider> m_CommandTargets = new List<Collider>();
        /// <summary>距离排序的比较器（缓存委托，避免每帧 new 一个 Comparison）。</summary>
        private readonly System.Comparison<Collider> m_DistanceComparison;

        private GunnerFamiliarDefinition m_Definition;
        private IAttributeModifierTarget m_AttributeModifiers;
        private Collider[] m_ScanBuffer = new Collider[64];
        private RaycastHit[] m_HitBuffer = new RaycastHit[32];
        private Collider[] m_CommandBuffer = new Collider[16];
        private StatusEffectSpec[] m_Statuses = System.Array.Empty<StatusEffectSpec>();
        private GameplayEntityId m_OwnerEntityId;
        private FamiliarWeaponId m_CurrentWeapon;
        private bool m_Active;
        private bool m_Composed;
        private ulong m_Sequence;
        private float m_ScanTimer;
        private bool m_WarnedMissingTracer;
        private Material m_FallbackMaterial;

        // ── 血契调制注册（IFamiliarPactTarget，8xxx 射击使魔）──
        // 各契模块运行时以自身为 source 注册/注销；任何变更都会触发 RecomputeFromPactScales()
        // （从资产按当前武器档位重建定义，再把契因子乘到 weapon profile 上 → 重建使魔群）。
        private readonly Dictionary<object, float> m_DamageScales = new Dictionary<object, float>();
        private readonly Dictionary<object, float> m_IntervalScales = new Dictionary<object, float>();
        private readonly Dictionary<object, int> m_CountAdds = new Dictionary<object, int>();
        private object m_WeaponTierSource;
        private int m_WeaponTierId = -1;
        private object m_ElementSource;
        private ElementId m_OverrideElement = ElementId.None;
        private StatusEffectSpec[] m_OverrideStatuses = System.Array.Empty<StatusEffectSpec>();
        private float m_ElementStackEff = 1f;

        // ── 指针跟随状态 ──
        private Vector3 m_OrbitCenter;
        private bool m_OrbitCenterInitialized;
        private Vector3 m_SortOrigin;

        /// <summary>正在使用的武器档位 id。</summary>
        public FamiliarWeaponId CurrentWeapon => m_CurrentWeapon;

        /// <summary>
        /// 统一环绕队列的归属者：优先用玩家自己的 NetworkObject 所在物体。
        /// 不用 <c>transform.root</c> 是因为它会一路往上找 —— 玩家一旦被挂进「Players」这类容器，
        /// 所有玩家的使魔就会串进同一条队列。NetworkObject 挂在玩家自己身上，才是稳定的归属。
        /// </summary>
        private Transform OrbitOwner
        {
            get
            {
                if (orbitOwner != null) return orbitOwner;
                if (networkObject != null) return networkObject.transform;
                return transform.root;
            }
        }

        public GunnerFamiliarController()
        {
            // 缓存比较器委托：指针索敌每帧都要按距离排序，不能每帧 new 一个 Comparison。
            m_DistanceComparison = CompareDistanceToSortOrigin;
        }

        // ── 生命周期 ──────────────────────────────────────────

        private void Awake()
        {
            if (networkObject == null) networkObject = GetComponent<NetworkObject>();
            if (resolutionHost == null) resolutionHost = GetComponent<ServerCombatResolutionHost>();
            if (coreStats == null) coreStats = GetComponent<CoreStatsHandler>();
            if (pointerCommand == null) pointerCommand = GetComponent<FamiliarPointerCommand>();
            var behaviours = GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IAttributeModifierTarget target) { m_AttributeModifiers = target; break; }
            }
            ComposeIfNeeded();
        }

        private void OnEnable()
        {
            ComposeIfNeeded();
            resolutionHost?.RegisterCombatResolutionListener(this);
        }

        private void OnDisable()
        {
            resolutionHost?.UnregisterCombatResolutionListener(this);
        }

        private void Start()
        {
            if (activeOnStart) SetActive(true);
        }

        private void Update()
        {
            if (!m_Active) return;
            // 服务器权威：客户端不推进状态机（视觉表现待补网络同步）。
            if (!IsServerAuthoritative()) return;
            TickAll(Time.deltaTime);
        }

        private void OnDestroy()
        {
            ReleaseBrains();
            DestroyVisuals();
            if (m_FallbackMaterial != null)
            {
                Destroy(m_FallbackMaterial);
                m_FallbackMaterial = null;
            }
        }

        /// <summary>
        /// 让本控制器的所有使魔退出统一环绕队列并把槽位让给同伴。
        /// 关闭使魔、重建 brain、销毁控制器这三种情况都必须调用，
        /// 否则队列里会留下永远没人站的空位（表现为待机队形缺一个角）。
        /// </summary>
        private void ReleaseBrains()
        {
            for (int i = 0; i < m_Brains.Count; i++) m_Brains[i].ReleaseOrbitSlot();
            m_Brains.Clear();
        }

        // ── 血契 / 调试开关 ───────────────────────────────────

        /// <summary>开关使魔（血契「获得使魔」时调用 true）。</summary>
        public void SetActive(bool active)
        {
            ComposeIfNeeded();
            m_Active = active;
            if (!m_Active)
            {
                for (int i = 0; i < m_Brains.Count; i++) m_Brains[i].AbortEngagement();
                ReleaseBrains();     // 关掉的使魔不留空位
                DestroyVisuals();
                return;
            }
            RebuildBrains();
            SpawnVisuals();
        }

        /// <summary>使魔是否处于激活状态。</summary>
        public bool IsActive => m_Active;

        /// <summary>当前使魔数量。</summary>
        public int FamiliarCount => m_Brains.Count;

        /// <summary>设置使魔数量（血契「使魔·增殖」等调用），会立即重建。</summary>
        public void SetFamiliarCount(int count)
        {
            if (count < 1) count = 1;
            familiarCount = count;
            if (!m_Active) return;
            RebuildBrains();
            SpawnVisuals();
        }

        /// <summary>
        /// 切换武器档位（血契「使魔·改型」系列调用）：手枪 → 狙击 / 步枪 / 激光。
        /// 会整体替换伤害倍率、射程、连发、冷却、穿透等一整套武器参数。
        /// </summary>
        public void SetWeapon(FamiliarWeaponId weaponId)
        {
            ComposeIfNeeded();
            GunnerFamiliarWeaponProfile profile = definitionAsset != null
                ? definitionAsset.FindWeapon(weaponId)
                : null;
            m_Definition = definitionAsset != null
                ? definitionAsset.CreateDefinition(weaponId)
                : m_Definition;
            m_CurrentWeapon = weaponId;
            RebuildStatuses();
            RebuildBrains();

            if (logToConsole)
            {
                Debug.Log($"[GunnerFamiliar] 武器档位 → {weaponId}" +
                          (profile == null ? "（资产里没配这一档，已退回内置默认）" : $"（{profile.displayName}）"), this);
            }
        }

        // ── 运行期系数接口（供血契调整）──────────────────────

        /// <summary>设置基础伤害继承系数（1 = 全额继承玩家基础伤害）。</summary>
        public void SetBaseDamageInheritRatio(float ratio) => OverrideDefinition(baseDamageInheritRatio: Mathf.Max(0f, ratio));

        /// <summary>设置当前武器档位的伤害倍率（单发伤害 = 玩家基础伤害 × 继承系数 × 倍率）。</summary>
        public void SetDamageMultiplier(float multiplier) => OverrideDefinition(damageMultiplier: Mathf.Max(0f, multiplier));

        /// <summary>
        /// 设置 buff 触发数量系数：每次触发挂载层数 = 配置层数 × 此系数（四舍五入、最少 1）。
        /// </summary>
        public void SetBuffTriggerCountMultiplier(float multiplier) =>
            OverrideDefinition(buffTriggerCountMultiplier: Mathf.Max(0f, multiplier));

        /// <summary>设置飞行速度（血契「使魔·疾行」）。</summary>
        public void SetMoveSpeed(float speed) => OverrideDefinition(moveSpeed: Mathf.Max(0.1f, speed));

        /// <summary>设置环绕半径（血契「使魔·扩域」）。</summary>
        public void SetOrbitRadius(float radius) => OverrideDefinition(orbitRadius: Mathf.Max(0.1f, radius));

        /// <summary>设置瞄准前摇时长（秒）。</summary>
        public void SetAimDuration(float seconds) => OverrideDefinition(aimDuration: Mathf.Max(0f, seconds));

        /// <summary>
        /// 设置当前武器档位的元素与命中附加状态（血契「使魔·元素化」/ 调试键调用）。
        /// <paramref name="onHitStatuses"/> 传 null 则只改元素、保留原档位命中状态。
        /// </summary>
        public void SetElement(ElementId element, StatusEffectSpec[] onHitStatuses = null)
        {
            ComposeIfNeeded();
            GunnerFamiliarWeaponProfile weapon = m_Definition.Weapon;
            if (weapon == null) return;
            weapon.element = element;
            if (onHitStatuses != null) weapon.onHitStatuses = onHitStatuses;
            RebuildStatuses();
            if (m_Active) RebuildBrains();
        }

        // ── 血契「牵丝之契」接口 ──────────────────────────────

        /// <summary>
        /// 开关「围绕鼠标」（血契「牵丝之契」选中时调用 true）。<br/>
        /// 关闭后环绕中心会<b>平滑地</b>滑回玩家身上（不是瞬移），使魔行为回到默认（围绕玩家）。
        /// </summary>
        public void SetPointerOrbit(bool enabled) => pointerOrbit = enabled;

        /// <summary>是否处于「围绕鼠标」模式。</summary>
        public bool PointerOrbit => pointerOrbit;

        /// <summary>设置左键指令的索敌半径（米）：以指针为圆心多大范围内的敌人会被指派。</summary>
        public void SetCommandRadius(float radius) => commandRadius = Mathf.Max(0.5f, radius);

        private void OverrideDefinition(
            float? baseDamageInheritRatio = null,
            float? damageMultiplier = null,
            float? buffTriggerCountMultiplier = null,
            float? moveSpeed = null,
            float? orbitRadius = null,
            float? aimDuration = null)
        {
            GunnerFamiliarDefinition current = m_Definition;
            GunnerFamiliarWeaponProfile weapon = current.Weapon;
            if (damageMultiplier.HasValue) weapon.damageMultiplier = Mathf.Max(0f, damageMultiplier.Value);

            m_Definition = new GunnerFamiliarDefinition(
                current.AbilityId, current.WeaponTag,
                orbitRadius ?? current.OrbitRadius, current.OrbitSpeed, current.OrbitHeight,
                current.BobAmplitude, current.BobFrequency, current.FollowLerp,
                current.AcquireRange, current.PendingTargetLifetime, current.TargetMask, current.MaxTargets,
                moveSpeed ?? current.MoveSpeed, current.MoveLerp, current.RangeTolerance, current.StrafeSpeed,
                current.MaxApproachDuration,
                aimDuration ?? current.AimDuration, current.AimLerp,
                current.MaxVolleysPerTarget, current.SwitchTargetOnNewCandidate, current.StopWhenPendingExpired,
                current.ReturnSpeed, current.ReturnArriveDistance,
                baseDamageInheritRatio ?? current.BaseDamageInheritRatio,
                buffTriggerCountMultiplier ?? current.BuffTriggerCountMultiplier,
                weapon,
                current.VisualScale);
            RebuildStatuses();
            RebuildBrains();
        }

        // ── 血契调制端口实现（IFamiliarPactTarget，供 8xxx 契模块调用）────────

        public FamiliarKind Kind => FamiliarKind.Gunner;

        /// <summary>召唤（8001）：激活射击使魔群。仅一次、永久；重复调用幂等。</summary>
        public void ActivateFamiliar() => SetActive(true);

        public void RegisterDamageScale(object source, float factor)
        {
            m_DamageScales[source] = Mathf.Max(0f, factor);
            RecomputeFromPactScales();
        }

        public void RegisterIntervalScale(object source, float factor)
        {
            m_IntervalScales[source] = Mathf.Max(0f, factor);
            RecomputeFromPactScales();
        }

        public void RegisterCountAdd(object source, int add)
        {
            m_CountAdds[source] = add;
            RecomputeFromPactScales();
        }

        public void SetWeaponTier(object source, int weaponTier)
        {
            m_WeaponTierSource = source;
            m_WeaponTierId = Mathf.Clamp(weaponTier, 0, 4);
            RecomputeFromPactScales();
        }

        public void ConvertElement(object source, ElementId element, uint statusId,
            int statusStacks, float statusDuration, float stackEffMultiplier)
        {
            m_ElementSource = source;
            m_OverrideElement = element;
            m_OverrideStatuses = statusId != 0
                ? new[] { new StatusEffectSpec(statusId, Mathf.Max(1, statusStacks), statusDuration, 0f, element) }
                : System.Array.Empty<StatusEffectSpec>();
            m_ElementStackEff = Mathf.Max(0f, stackEffMultiplier);
            RecomputeFromPactScales();
        }

        public void UnregisterAll(object source)
        {
            bool changed = m_DamageScales.Remove(source);
            changed |= m_IntervalScales.Remove(source);
            changed |= m_CountAdds.Remove(source);
            if (ReferenceEquals(m_WeaponTierSource, source))
            {
                m_WeaponTierSource = null;
                m_WeaponTierId = -1;
                changed = true;
            }
            if (ReferenceEquals(m_ElementSource, source))
            {
                m_ElementSource = null;
                m_OverrideElement = ElementId.None;
                m_OverrideStatuses = System.Array.Empty<StatusEffectSpec>();
                m_ElementStackEff = 1f;
                changed = true;
            }
            if (changed) RecomputeFromPactScales();
        }

        /// <summary>
        /// 血契变更后的统一重算：按「资产默认档位 or 契档位覆写」<b>从配置资产重建基线定义</b>，
        /// 再把全部已注册契因子乘到 weapon profile 上（伤害 ×Πscale、整套冷却 ×Πscale）、
        /// 套用元素转化覆写（元素 + 命中状态 + 叠层效率 ×stackEff）。契因子永远相对 asset 默认值乘，
        /// 避免叠层时二次累乘。契变更发生在升级选牌/叠层时刻，RebuildBrains 打断战斗属可接受行为。
        /// </summary>
        private void RecomputeFromPactScales()
        {
            if (definitionAsset == null) return;
            ComposeIfNeeded();
            if (!m_Composed) return;

            FamiliarWeaponId weaponId = m_WeaponTierSource != null
                ? (FamiliarWeaponId)Mathf.Clamp(m_WeaponTierId, 0, 4)
                : definitionAsset.DefaultWeaponId;
            m_CurrentWeapon = weaponId;
            m_Definition = definitionAsset.CreateDefinition(weaponId);   // 含武器档位拷贝（基线）
            GunnerFamiliarWeaponProfile weapon = m_Definition.Weapon;
            if (weapon == null) return;

            float damageFactor = 1f;
            foreach (float factor in m_DamageScales.Values) damageFactor *= factor;
            float intervalFactor = 1f;
            foreach (float factor in m_IntervalScales.Values) intervalFactor *= factor;

            weapon.damageMultiplier = Mathf.Max(0f, weapon.damageMultiplier * damageFactor);
            weapon.fireCooldown = Mathf.Max(0f, weapon.fireCooldown * intervalFactor);

            if (m_ElementSource != null)
            {
                weapon.element = m_OverrideElement;
                weapon.onHitStatuses = m_OverrideStatuses;
                // 叠层效率 ×stackEff：命中挂载层数 = 基础层数 × buff 触发系数（转化契 ×2）。
                // GunnerFamiliarDefinition 是 readonly struct，乘系数需整体重建一份（weapon 引用不变，已乘的伤害/冷却保留）。
                if (!Mathf.Approximately(m_ElementStackEff, 1f))
                {
                    GunnerFamiliarDefinition current = m_Definition;
                    m_Definition = new GunnerFamiliarDefinition(
                        current.AbilityId, current.WeaponTag,
                        current.OrbitRadius, current.OrbitSpeed, current.OrbitHeight,
                        current.BobAmplitude, current.BobFrequency, current.FollowLerp,
                        current.AcquireRange, current.PendingTargetLifetime, current.TargetMask, current.MaxTargets,
                        current.MoveSpeed, current.MoveLerp, current.RangeTolerance, current.StrafeSpeed,
                        current.MaxApproachDuration,
                        current.AimDuration, current.AimLerp,
                        current.MaxVolleysPerTarget, current.SwitchTargetOnNewCandidate, current.StopWhenPendingExpired,
                        current.ReturnSpeed, current.ReturnArriveDistance,
                        current.BaseDamageInheritRatio,
                        current.BuffTriggerCountMultiplier * m_ElementStackEff,
                        weapon,
                        current.VisualScale);
                }
            }
            RebuildStatuses();
            RebuildBrains();
        }

        /// <summary>实际使魔数量 = prefab 基础数量 + 契增量合计（下限 1）。</summary>
        private int EffectiveFamiliarCount
        {
            get
            {
                int add = 0;
                foreach (int value in m_CountAdds.Values) add += value;
                return familiarCount + add;
            }
        }

        // ── 候选目标：玩家命中回调 ────────────────────────────

        public int Priority => 0;

        /// <summary>
        /// 玩家（作为伤害来源）命中敌人时由 <c>ServerCombatResolutionHost</c> 回调。
        /// 把被命中的敌人登记为每只使魔的候选目标；新命中的敌人<b>覆盖</b>旧候选。
        /// 正在射击流程中的使魔只写缓存、<b>不打断</b>当前这一套。
        /// </summary>
        public void OnCombatResolved(in CombatResolutionRecord record, CombatParticipantRole role)
        {
            if (!m_Active) return;
            if ((role & CombatParticipantRole.Source) == 0) return;
            if ((record.Tags & m_Definition.WeaponTag) != 0) return;   // 使魔自己的伤害，不登记
            if (record.Target.IsNone) return;
            if (!IsWithinAcquireRange(record.Target)) return;

            // 被动索敌的优先级低于左键指令：玩家指过目标之后，武器误伤到别的怪不会把使魔带走。
            for (int i = 0; i < m_Brains.Count; i++)
                m_Brains[i].SetPendingTarget(record.Target, m_Definition.PendingTargetLifetime,
                    GunnerFamiliarBrain.PriorityPassive);

            if (logToConsole)
            {
                Debug.Log($"[GunnerFamiliar] 登记候选目标 entity={record.Target.Value} " +
                          $"（{m_Brains.Count} 只使魔各自缓存，射击流程中的不会被打断）", this);
            }
        }

        private bool IsWithinAcquireRange(GameplayEntityId targetId)
        {
            // 缓存命中时直接量距离；没扫到就先放行，等起飞前再校验（避免刚出生那一帧漏登记）。
            if (m_EnemyCache.TryGetValue(targetId, out MonoBehaviour cached) && cached != null)
            {
                float squared = (cached.transform.position - transform.position).sqrMagnitude;
                return squared <= m_Definition.AcquireRange * m_Definition.AcquireRange;
            }
            return true;
        }

        // ── 每帧推进 ──────────────────────────────────────────

        private void TickAll(float deltaTime)
        {
            Vector3 ownerPosition = transform.position;

            m_ScanTimer += deltaTime;
            if (m_ScanTimer >= EnemyScanInterval)
            {
                m_ScanTimer = 0f;
                RefreshEnemyCache(ownerPosition);
            }

            // ① 先算这一帧的环绕中心：血契开启且指针有效时跟鼠标，否则跟玩家（行为与改动前一致）。
            Vector3 orbitCenter = ComputeOrbitCenter(ownerPosition, deltaTime);

            // ② 按住左键 → 把指针附近的敌人按距离指派给各只使魔（优先级高于被动索敌）。
            IssuePointerCommands();

            for (int i = 0; i < m_Brains.Count; i++)
            {
                GunnerFamiliarBrain brain = m_Brains[i];

                // 空闲且手上有有效候选 → 解析出目标对象后立刻起飞。
                // 交战中（接近/瞄准/开火/冷却）的使魔不会走这里，因此不会被打断。
                if (brain.WantsEngage && TryResolveTarget(brain.PendingTargetId, ownerPosition, out MonoBehaviour target))
                {
                    brain.BeginEngagement(target, brain.PendingTargetId);
                    if (logToConsole)
                        Debug.Log($"[GunnerFamiliar] #{i} 起飞 → entity={brain.PendingTargetId.Value}", this);
                }

                // 注意：环绕中心传的是 orbitCenter（待机/返回时围绕的点），
                // 但索敌范围仍以玩家位置为准 —— 使魔能追多远，始终由玩家决定，不会被鼠标拖出战场。
                brain.Tick(deltaTime, orbitCenter);

                // 开火请求：只有 Fire 状态才会产生，且每发只取一次。
                if (brain.HasPendingShot && brain.TryConsumeShot(out Vector3 origin, out Vector3 direction))
                    ResolveShot(brain, origin, direction);
            }

            TickTracers(deltaTime);
            SyncVisuals();
        }

        // ── 指针跟随与指令索敌 ────────────────────────────────

        /// <summary>
        /// 计算这一帧的环绕中心（orbit center）：血契开启且指针有效时跟随鼠标，否则回到玩家身上。<br/>
        /// 无论往哪个方向切都是<b>平滑插值</b>过去的（<c>OrbitCenterFollowLerp</c>），不会瞬移。
        /// </summary>
        private Vector3 ComputeOrbitCenter(Vector3 ownerPosition, float deltaTime)
        {
            Vector3 target = ownerPosition;

            if (pointerOrbit && pointerCommand != null && pointerCommand.TryGetPointer(out Vector3 pointer))
            {
                target = pointer;
                // 距离上限：鼠标拖太远时把队列夹在玩家周围这个半径上（0 = 不限制）。
                if (maxOrbitCenterDistance > 0f)
                {
                    Vector3 offset = target - ownerPosition;
                    offset.y = 0f;
                    float max = maxOrbitCenterDistance;
                    if (offset.sqrMagnitude > max * max)
                        target = ownerPosition + offset.normalized * max;
                }
            }

            if (!m_OrbitCenterInitialized)
            {
                m_OrbitCenter = target;
                m_OrbitCenterInitialized = true;
                return target;
            }

            float t = 1f - Mathf.Exp(-orbitCenterFollowLerp * deltaTime);
            m_OrbitCenter = Vector3.Lerp(m_OrbitCenter, target, t);
            return m_OrbitCenter;
        }

        /// <summary>
        /// 左键指令索敌（pointer command）：按住左键时，以指针为圆心、<c>CommandRadius</c> 为半径
        /// 找出范围内的敌人，<b>按到指针的距离从近到远排序</b>，第 N 只使魔领第 N 近的目标。<br/>
        /// 敌人比使魔少时多只一起集火最近的；使魔比敌人多时多余的也集火最近的（不会闲着）。
        /// </summary>
        /// <remarks>
        /// 写进的是<b>候选缓存</b>而不是强制起飞：正在打一套（接近/瞄准/开火/冷却）的使魔只记下目标，
        /// 等这套打完在决策点自然转向 —— 与「射击流程不可打断」的既有规则保持一致。
        /// </remarks>
        private void IssuePointerCommands()
        {
            if (!pointerOrbit || pointerCommand == null) return;
            if (!pointerCommand.IsCommandHeld) return;
            if (!pointerCommand.TryGetPointer(out Vector3 origin)) return;

            if (m_CommandBuffer == null || m_CommandBuffer.Length < m_Definition.MaxTargets)
                m_CommandBuffer = new Collider[Mathf.Max(1, m_Definition.MaxTargets)];

            int count = Physics.OverlapSphereNonAlloc(origin, commandRadius, m_CommandBuffer,
                m_Definition.TargetMask, QueryTriggerInteraction.Collide);
            if (count <= 0) return;

            // 按到指针的距离升序排序（数量很小，List.Sort 足够；比较器与基准点都复用，不每帧分配）。
            m_CommandTargets.Clear();
            for (int i = 0; i < count && m_CommandTargets.Count < m_CommandBuffer.Length; i++)
            {
                Collider candidate = m_CommandBuffer[i];
                if (candidate != null) m_CommandTargets.Add(candidate);
            }
            if (m_CommandTargets.Count == 0) return;

            m_SortOrigin = origin;
            m_CommandTargets.Sort(m_DistanceComparison);

            for (int i = 0; i < m_Brains.Count; i++)
            {
                // 敌人比使魔少 → 多余的使魔集火最近的那只（取最后一个下标，不会越界）。
                int pick = i < m_CommandTargets.Count ? i : m_CommandTargets.Count - 1;
                GameplayEntityId targetId = ResolveEntityId(m_CommandTargets[pick].transform);
                if (targetId.IsNone) continue;

                m_Brains[i].SetPendingTarget(targetId, commandTargetLifetime,
                    GunnerFamiliarBrain.PriorityCommand);
            }

            if (logToConsole)
            {
                Debug.Log($"[GunnerFamiliar] 左键指令 → 指针 {origin} 半径 {commandRadius}m 内 " +
                          $"{m_CommandTargets.Count} 个敌人，指派给 {m_Brains.Count} 只使魔", this);
            }
        }

        /// <summary>排序用：到 <c>m_SortOrigin</c> 的距离升序（只比 XZ 平面，避免身高干扰）。</summary>
        private int CompareDistanceToSortOrigin(Collider a, Collider b)
        {
            float da = HorizontalSqrDistance(a.transform.position, m_SortOrigin);
            float db = HorizontalSqrDistance(b.transform.position, m_SortOrigin);
            return da.CompareTo(db);
        }

        private static float HorizontalSqrDistance(Vector3 from, Vector3 to)
        {
            float dx = from.x - to.x;
            float dz = from.z - to.z;
            return dx * dx + dz * dz;
        }

        /// <summary>结算一次射击：球形扫描 → 按穿透数取前 N 个敌人 → 逐个走可信命中。</summary>
        private void ResolveShot(GunnerFamiliarBrain brain, Vector3 origin, Vector3 direction)
        {
            GunnerFamiliarWeaponProfile weapon = m_Definition.Weapon;
            if (m_HitBuffer == null || m_HitBuffer.Length < m_Definition.MaxTargets)
                m_HitBuffer = new RaycastHit[Mathf.Max(1, m_Definition.MaxTargets)];

            Vector3 aimDirection = ApplySpread(direction, weapon.spreadAngle);
            float maxDistance = Mathf.Max(0.5f, weapon.maxFireDistance);
            float radius = Mathf.Max(0.01f, weapon.projectileRadius);

            int count = Physics.SphereCastNonAlloc(origin, radius, aimDirection, m_HitBuffer,
                maxDistance, m_Definition.TargetMask, QueryTriggerInteraction.Collide);

            Vector3 endPoint = origin + aimDirection * maxDistance;
            if (count > 0)
            {
                // SphereCast 的返回顺序不保证按距离排序，手动排一次，穿透才符合直觉（先打近的）。
                SortByDistance(m_HitBuffer, count);
                endPoint = m_HitBuffer[0].point;
            }

            if (debugDrawShots) Debug.DrawLine(origin, endPoint, Color.yellow, 0.1f);
            SpawnMuzzleVfx(origin, aimDirection);

            float damage = ReadAttribute(StatKeys.Damage) * m_Definition.BaseDamageInheritRatio *
                           weapon.damageMultiplier;
            if (damage <= 0f || count <= 0)
            {
                // 没打中任何东西也要把子弹画出来（飞满最大射程），否则玩家会以为没开火。
                SpawnTracer(origin, endPoint, aimDirection, false);
                return;
            }

            float knockback = ReadAttribute(StatKeys.KnockbackForce) * weapon.knockbackMultiplier;
            int pierce = Mathf.Max(1, weapon.pierceCount);
            DamageTags tags = m_Definition.WeaponTag | weapon.extraDamageTags;
            int resolved = 0;
            Vector3 tracerEnd = endPoint;   // 穿透武器让子弹飞过所有被打中的目标，而不是停在第一个

            for (int i = 0; i < count && resolved < pierce; i++)
            {
                Collider hitCollider = m_HitBuffer[i].collider;
                if (hitCollider == null) continue;
                if (!TryFindTarget(hitCollider, out _, out ITrustedCombatHitTarget trusted, out IHittable fallback))
                    continue;

                GameplayEntityId targetId = ResolveEntityId(hitCollider.transform);
                if (targetId.IsNone) continue;

                Vector3 force = aimDirection * knockback;
                var request = new DamageRequest(m_OwnerEntityId, targetId, m_Definition.AbilityId,
                    m_Sequence++, damage, tags);
                var hit = new TrustedCombatHit(request, weapon.element, m_Statuses,
                    new Float3(force.x, force.y, force.z));

                if (trusted != null)
                {
                    trusted.SubmitTrustedHit(hit);
                }
                else if (fallback != null)
                {
                    fallback.OnHit(new HitInfo
                    {
                        amount = damage,
                        hitPoint = m_HitBuffer[i].point,
                        hitNormal = -aimDirection,
                        attackerId = m_OwnerEntityId.IsNone ? 0UL : m_OwnerEntityId.Value - 1UL,
                        impactForce = force
                    });
                }
                resolved++;
                tracerEnd = m_HitBuffer[i].point;
            }

            SpawnTracer(origin, tracerEnd, aimDirection, true);
        }

        /// <summary>按散布角随机偏转朝向（0 = 精准）。</summary>
        private static Vector3 ApplySpread(Vector3 direction, float spreadAngle)
        {
            if (spreadAngle <= 0f) return direction;
            Vector3 up = Mathf.Abs(direction.y) > 0.99f ? Vector3.forward : Vector3.up;
            Vector3 right = Vector3.Cross(up, direction).normalized;
            Vector3 realUp = Vector3.Cross(direction, right).normalized;
            Quaternion offset = Quaternion.AngleAxis(Random.Range(-spreadAngle, spreadAngle), realUp) *
                                Quaternion.AngleAxis(Random.Range(-spreadAngle, spreadAngle), right);
            return (offset * direction).normalized;
        }

        private static void SortByDistance(RaycastHit[] buffer, int count)
        {
            // 命中数很少（≤ MaxTargets），插入排序足够且无 GC。
            for (int i = 1; i < count; i++)
            {
                RaycastHit key = buffer[i];
                int j = i - 1;
                while (j >= 0 && buffer[j].distance > key.distance)
                {
                    buffer[j + 1] = buffer[j];
                    j--;
                }
                buffer[j + 1] = key;
            }
        }

        private void RefreshEnemyCache(Vector3 ownerPosition)
        {
            m_EnemyCache.Clear();
            if (m_ScanBuffer == null || m_ScanBuffer.Length < m_Definition.MaxTargets)
                m_ScanBuffer = new Collider[Mathf.Max(1, m_Definition.MaxTargets)];

            int count = Physics.OverlapSphereNonAlloc(ownerPosition, m_Definition.AcquireRange, m_ScanBuffer,
                m_Definition.TargetMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                Collider candidate = m_ScanBuffer[i];
                if (candidate == null) continue;
                if (!TryFindTarget(candidate, out MonoBehaviour behaviour, out _, out _)) continue;
                GameplayEntityId id = ResolveEntityId(behaviour);
                if (id.IsNone) continue;
                m_EnemyCache[id] = behaviour;
            }
        }

        private bool TryResolveTarget(GameplayEntityId targetId, Vector3 ownerPosition, out MonoBehaviour target)
        {
            target = null;
            if (targetId.IsNone) return false;
            if (!m_EnemyCache.TryGetValue(targetId, out MonoBehaviour cached) || cached == null) return false;

            // 起飞前校验：目标还在索敌范围内才追（超出就等候选过期）。
            float squared = (cached.transform.position - ownerPosition).sqrMagnitude;
            if (squared > m_Definition.AcquireRange * m_Definition.AcquireRange) return false;

            target = cached;
            return true;
        }

        // ── 视觉 ──────────────────────────────────────────────

        private void SpawnVisuals()
        {
            DestroyVisuals();
            if (familiarVisualPrefab == null) return;
            for (int i = 0; i < m_Brains.Count; i++)
            {
                GameObject visual = Instantiate(familiarVisualPrefab, m_Brains[i].Position, Quaternion.identity);
                visual.transform.localScale = Vector3.one * m_Definition.VisualScale;
                m_Visuals.Add(visual);
            }
        }

        private void DestroyVisuals()
        {
            for (int i = 0; i < m_Visuals.Count; i++)
            {
                if (m_Visuals[i] != null) Destroy(m_Visuals[i]);
            }
            m_Visuals.Clear();
            ClearTracers();
        }

        private void SyncVisuals()
        {
            for (int i = 0; i < m_Visuals.Count && i < m_Brains.Count; i++)
            {
                GameObject visual = m_Visuals[i];
                if (visual == null) continue;
                GunnerFamiliarBrain brain = m_Brains[i];

                visual.transform.position = brain.Position;
                if (brain.Facing.sqrMagnitude > 0.0001f)
                    visual.transform.rotation = Quaternion.LookRotation(brain.Facing);
                visual.transform.localScale = Vector3.one * m_Definition.VisualScale;
            }
        }

        private void SpawnMuzzleVfx(Vector3 origin, Vector3 direction)
        {
            GameObject prefab = m_Definition.Weapon.muzzleVfxPrefab;
            if (prefab == null) prefab = fallbackMuzzleVfxPrefab;
            if (prefab == null) return;
            GameObject vfx = Instantiate(prefab, origin,
                direction.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(direction) : Quaternion.identity);
            Destroy(vfx, MuzzleVfxLifetime);
        }

        /// <summary>命中闪光（在曳光弹抵达终点时播放）。</summary>
        private void SpawnImpactVfx(Vector3 point)
        {
            GameObject prefab = fallbackImpactVfxPrefab;
            if (prefab == null) return;
            GameObject vfx = Instantiate(prefab, point, Quaternion.identity);
            Destroy(vfx, impactVfxLifetime);
        }

        /// <summary>
        /// 生成一发曳光弹：沿 <paramref name="direction"/> 从 <paramref name="from"/> 飞到 <paramref name="to"/>。
        /// 纯表现——伤害在开火帧已经即时结算（hitscan），这里的飞行只负责<b>让玩家看得见子弹</b>。
        /// </summary>
        private void SpawnTracer(Vector3 from, Vector3 to, Vector3 direction, bool spawnImpactOnArrive)
        {
            GameObject prefab = m_Definition.Weapon.tracerPrefab;
            if (prefab == null) prefab = fallbackTracerPrefab;

            GameObject go;
            if (prefab != null)
            {
                go = Instantiate(prefab, from,
                    direction.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(direction) : Quaternion.identity);
            }
            else
            {
                // 连兜底 prefab 都没接：程序生成一个发光小球，宁可丑也要看得见。
                go = CreateFallbackTracer(from, direction);
                if (!m_WarnedMissingTracer)
                {
                    m_WarnedMissingTracer = true;
                    Debug.LogWarning("[GunnerFamiliar] 武器档位与控制器都没接曳光弹预制体（tracerPrefab / " +
                                     "fallbackTracerPrefab），已用程序生成的临时发光小球代替。", this);
                }
            }

            float scale = Mathf.Max(0.05f, tracerScaleMultiplier) * Mathf.Max(0.05f, m_Definition.Weapon.tracerScale);
            go.transform.localScale *= scale;

            float speed = Mathf.Max(1f, m_Definition.Weapon.projectileSpeed);
            m_Tracers.Add(new Tracer(go, from, to, speed, spawnImpactOnArrive));
        }

        /// <summary>程序生成一颗自发光小球（兜底用，只在 prefab 全空时走这条路）。</summary>
        private GameObject CreateFallbackTracer(Vector3 position, Vector3 direction)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Collider collider = go.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            go.transform.position = position;
            if (direction.sqrMagnitude > 0.0001f) go.transform.rotation = Quaternion.LookRotation(direction);
            go.transform.localScale = new Vector3(0.13f, 0.13f, 0.6f);

            if (m_FallbackMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                m_FallbackMaterial = new Material(shader);
                m_FallbackMaterial.SetColor("_BaseColor", Color.white);
                m_FallbackMaterial.SetColor("_EmissionColor", new Color(3.2f, 2.4f, 0.6f, 1f));
                m_FallbackMaterial.EnableKeyword("_EMISSION");
            }
            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = m_FallbackMaterial;
            return go;
        }

        private void TickTracers(float deltaTime)
        {
            if (m_Tracers.Count == 0) return;
            for (int i = m_Tracers.Count - 1; i >= 0; i--)
            {
                Tracer tracer = m_Tracers[i];
                if (tracer.Visual == null) { m_Tracers.RemoveAt(i); continue; }

                tracer.Elapsed += deltaTime;
                float total = tracer.TotalDistance / tracer.Speed;
                float t = total <= 0.0001f ? 1f : Mathf.Clamp01(tracer.Elapsed / total);
                tracer.Visual.transform.position = Vector3.Lerp(tracer.From, tracer.To, t);

                if (t < 1f) continue;
                if (tracer.SpawnImpactOnArrive) SpawnImpactVfx(tracer.To);
                Destroy(tracer.Visual);
                m_Tracers.RemoveAt(i);
            }
        }

        private void ClearTracers()
        {
            for (int i = 0; i < m_Tracers.Count; i++)
            {
                if (m_Tracers[i].Visual != null) Destroy(m_Tracers[i].Visual);
            }
            m_Tracers.Clear();
        }

        // ── 组装 ──────────────────────────────────────────────

        private void ComposeIfNeeded()
        {
            if (m_Composed) return;
            if (definitionAsset == null) return;

            m_CurrentWeapon = definitionAsset.DefaultWeaponId;
            m_Definition = definitionAsset.CreateDefinition(m_CurrentWeapon);
            m_OwnerEntityId = networkObject != null
                ? new GameplayEntityId(networkObject.OwnerClientId + 1UL)
                : GameplayEntityId.None;
            RebuildStatuses();
            RebuildBrains();
            m_Composed = true;
        }

        private void RebuildStatuses()
        {
            StatusEffectSpec[] specs = m_Definition.Weapon?.onHitStatuses;
            if (specs == null || specs.Length == 0)
            {
                m_Statuses = System.Array.Empty<StatusEffectSpec>();
                return;
            }

            float multiplier = m_Definition.BuffTriggerCountMultiplier;
            if (Mathf.Approximately(multiplier, 1f))
            {
                m_Statuses = specs;
                return;
            }

            var result = new StatusEffectSpec[specs.Length];
            for (int i = 0; i < specs.Length; i++)
            {
                StatusEffectSpec spec = specs[i];
                int stacks = Mathf.Max(1, Mathf.RoundToInt(spec.Stacks * multiplier));
                result[i] = new StatusEffectSpec(spec.StatusId, stacks, spec.Duration, spec.Magnitude, spec.Element);
            }
            m_Statuses = result;
        }

        private void RebuildBrains()
        {
            ReleaseBrains();      // 旧 brain 先退出队列，新 brain 才会拿到连续的槽位
            int count = Mathf.Max(1, EffectiveFamiliarCount);
            Transform owner = OrbitOwner;
            for (int i = 0; i < count; i++)
            {
                var brain = new GunnerFamiliarBrain(m_Definition, i, count, owner);
                brain.SnapToOrbit(transform.position);
                m_Brains.Add(brain);
            }
        }

        /// <summary>
        /// 是否允许本机推进状态机：只在服务器（host / server）推进。
        /// 没有 NetworkObject 时（纯单机、未联网）放行，此时使魔会照常飞行，但伤害结算会被敌人侧以「未 Spawn」拒绝。
        /// </summary>
        private bool IsServerAuthoritative()
        {
            if (networkObject == null) return true;
            NetworkManager manager = networkObject.NetworkManager ?? NetworkManager.Singleton;
            return manager == null || manager.IsServer;
        }

        private float ReadAttribute(int attributeId)
        {
            if (m_AttributeModifiers != null) return m_AttributeModifiers.GetFinalAttributeValue(attributeId);
            return coreStats != null ? coreStats.GetCurrentValue(attributeId) : 0f;
        }

        // ── 内部类型 ──────────────────────────────────────────

        /// <summary>曳光弹：纯表现，沿直线飞到命中点后销毁，不参与任何判定。</summary>
        private sealed class Tracer
        {
            public GameObject Visual;
            public Vector3 From;
            public Vector3 To;
            public float Speed;
            public float Elapsed;
            public float TotalDistance;
            /// <summary>抵达终点时是否播命中闪光（打中东西才播，空枪不播）。</summary>
            public bool SpawnImpactOnArrive;

            public Tracer(GameObject visual, Vector3 from, Vector3 to, float speed, bool spawnImpactOnArrive)
            {
                Visual = visual;
                From = from;
                To = to;
                Speed = Mathf.Max(1f, speed);
                Elapsed = 0f;
                TotalDistance = Vector3.Distance(from, to);
                SpawnImpactOnArrive = spawnImpactOnArrive;
            }
        }

        // ── 静态工具 ──────────────────────────────────────────

        private static GameplayEntityId ResolveEntityId(Component component)
        {
            if (component == null) return GameplayEntityId.None;
            ICombatEntityIdentity identity = component.GetComponentInParent<ICombatEntityIdentity>();
            return identity != null ? identity.CombatEntityId : GameplayEntityId.None;
        }

        /// <summary>从碰撞体向上找可信命中目标（ITrustedCombatHitTarget），找不到则退回 IHittable。</summary>
        private static bool TryFindTarget(Collider collider, out MonoBehaviour target,
            out ITrustedCombatHitTarget trustedTarget, out IHittable fallback)
        {
            MonoBehaviour[] behaviours = collider.GetComponentsInParent<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is ITrustedCombatHitTarget trusted)
                {
                    target = behaviours[i];
                    trustedTarget = trusted;
                    fallback = null;
                    return true;
                }
            }
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IHittable hittable)
                {
                    target = behaviours[i];
                    trustedTarget = null;
                    fallback = hittable;
                    return true;
                }
            }
            target = null;
            trustedTarget = null;
            fallback = null;
            return false;
        }
    }
}
