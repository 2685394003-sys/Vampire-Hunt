using System.Collections.Generic;
using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Integration;
using VampireHunt.Infrastructure.Unity;
using VampireHunt.Infrastructure.Netcode.Abilities.Familiar;
using VampireHunt.Player.Abilities.Familiar;
using GameplayEntityId = VampireHunt.SharedKernel.EntityId;

namespace VampireHunt.Infrastructure.Netcode.Abilities.Familiar
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
    /// 本类只向 IGunnerFamiliarPresentationPublisher 发布只读姿态与开火事件，不直接创建或复制表现对象。
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed partial class GunnerFamiliarController : MonoBehaviour, IServerCombatResolutionListener, IFamiliarPactTarget
    {
        private const float EnemyScanInterval = 0.2f;
        [Header("配置")]
        [Tooltip("使魔配置资产（asset）：所有可调参数与武器档位都在这里。")]
        [SerializeField] private GunnerFamiliarAsset definitionAsset;
        [Tooltip("使魔数量：多只各自独立维护候选目标缓存，沿轨道均匀分布、交战时绕目标散开。")]
        [SerializeField, Min(1)] private int familiarCount = 2;
        [Tooltip("开局即激活：血契系统未接线时用于单机/调试验证。")]
        [SerializeField] private bool activeOnStart;

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
        private readonly FamiliarTargetQuery m_TargetQuery = new FamiliarTargetQuery();

        private GunnerFamiliarDefinition m_Definition;
        private IAttributeModifierTarget m_AttributeModifiers;
        private RaycastHit[] m_HitBuffer = new RaycastHit[32];
        private StatusEffectSpec[] m_Statuses = System.Array.Empty<StatusEffectSpec>();
        private GameplayEntityId m_OwnerEntityId;
        private FamiliarWeaponId m_CurrentWeapon;
        private bool m_Active;
        private bool m_Composed;
        private ulong m_Sequence;
        private float m_ScanTimer;
        private IGunnerFamiliarPresentationPublisher m_PresentationPublisher;

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
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (!(behaviours[i] is IGunnerFamiliarPresentationPublisher publisher)) continue;
                m_PresentationPublisher = publisher;
                break;
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
            // 服务器权威：客户端不推进状态机；姿态与开火表现由独立网络桥同步。
            if (!IsServerAuthoritative()) return;
            TickAll(Time.deltaTime);
        }

        private void OnDestroy()
        {
            ReleaseBrains();
            DestroyVisuals();
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

        // 配置/血契接口见 GunnerFamiliarController.Configuration.cs。

        // 候选、状态推进、索敌与伤害见 GunnerFamiliarController.Runtime.cs。

        // 只读表现事件适配见 GunnerFamiliarController.Presentation.cs；实例化由 Presentation 层处理。

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

    }
}
