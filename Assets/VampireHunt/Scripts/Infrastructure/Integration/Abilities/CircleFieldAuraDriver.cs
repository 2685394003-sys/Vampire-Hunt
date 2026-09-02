using UnityEngine;
using VampireHunt.Contracts;

namespace VampireHunt.Infrastructure.Integration
{
    /// <summary>
    /// 圆型领域的常驻驱动（aura driver）：挂在玩家身上，激活后每帧请求一次 Special 槽施法，
    /// 由 CircleFieldAbilityRuntime 的 TickInterval 节流成固定节奏的全向伤害。
    /// 圆心固定取玩家自身位置（不用枪口），因此领域始终以玩家为中心。
    /// 同时负责客户端本地的常驻领域表现（跟随玩家的圆盘/圆环）。
    /// </summary>
    /// <remarks>
    /// 血契接线点：荒芜之契（pactId 309）等「获得领域」类血契在生效时调用 <see cref="SetActive"/>(true)。
    /// 血契运行时尚未实装时，可勾选 activeOnStart 做单机验证。
    /// </remarks>
    public sealed class CircleFieldAuraDriver : MonoBehaviour, IFieldActivationTarget
    {
        [SerializeField] private CombatAbilityHost host;
        [SerializeField] private CircleFieldAbilityProvider provider;
        [SerializeField] private AbilitySlot slot = AbilitySlot.Special;
        [Tooltip("开局即激活：血契系统未接线时用于单机/调试验证。")]
        [SerializeField] private bool activeOnStart;

        [Header("常驻表现（客户端本地）")]
        [Tooltip("领域视觉预制体（prefab，可选）：跟随玩家的常驻圆形表现。")]
        [SerializeField] private GameObject fieldVisualPrefab;
        [Tooltip("领域视觉预制体的基准直径（米）：实际按 直径 / 基准直径 缩放。")]
        [SerializeField, Min(0.01f)] private float visualBaseDiameter = 1f;
        [Tooltip("领域视觉相对玩家脚下的高度（米），避免与地面 z-fighting。")]
        [SerializeField, Min(0f)] private float visualHeightOffset = 0.05f;

        private bool m_Active;
        private GameObject m_Visual;

        /// <summary>领域是否处于激活状态。</summary>
        public bool IsActive => m_Active;

        /// <summary>当前实际领域半径（米）。</summary>
        public float CurrentRadius => provider != null ? provider.CurrentRadius : 0f;

        private void Awake()
        {
            if (host == null) host = GetComponent<CombatAbilityHost>();
            if (provider == null) provider = GetComponent<CircleFieldAbilityProvider>();
        }

        private void Start()
        {
            if (activeOnStart) SetActive(true);
        }

        private void Update()
        {
            if (!m_Active || host == null) return;
            // 每帧请求，节流交给 runtime 的 TickInterval；非 owner / 未 spawn 时 host 内部直接拒绝。
            host.TryActivate(slot, transform);
            SyncVisual();
        }

        private void OnDestroy()
        {
            DestroyVisual();
        }

        /// <summary>开关领域（血契获得/失效时调用）。</summary>
        public void SetActive(bool active)
        {
            m_Active = active;
            if (!m_Active)
            {
                DestroyVisual();
                return;
            }
            EnsureVisual();
            SyncVisual();
        }

        /// <summary>
        /// IFieldActivationTarget：「获得领域」类血契（6001 荒芜降临）生效时激活常驻领域。
        /// 由 Unlock 效果模块（Realm=Owner）经端口机制调用。伤害继承系数 &gt; 0 时一并写入
        /// （荒芜降临配置 domainDPS = 玩家伤害 × 0.3，而领域资产默认继承系数为 1）。
        /// </summary>
        public bool ActivateField(float damageInheritRatio)
        {
            if (damageInheritRatio > 0f) SetBaseDamageInheritRatio(damageInheritRatio);
            SetActive(true);
            return true;
        }

        // ── 运行期系数接口（转发给运行时，供血契系统调整）─────────────

        /// <summary>设置基础伤害继承系数（1 = 全额继承玩家基础伤害）。</summary>
        public void SetBaseDamageInheritRatio(float ratio) => provider?.SetBaseDamageInheritRatio(ratio);

        /// <summary>设置 buff 触发数量系数（每次触发挂载层数 = 配置层数 × 此系数）。</summary>
        public void SetBuffTriggerCountMultiplier(float multiplier) =>
            provider?.SetBuffTriggerCountMultiplier(multiplier);

        /// <summary>设置半径缩放（1 = 配置半径）：「领域·扩大」类血契调用，伤害与表现同步。</summary>
        public void SetRadiusScale(float scale)
        {
            provider?.SetRadiusScale(scale);
            SyncVisual();
        }

        private void EnsureVisual()
        {
            if (fieldVisualPrefab == null || m_Visual != null) return;
            // 不做父子绑定：领域视觉保持在世界空间，由 SyncVisual 每帧贴到玩家脚下。
            m_Visual = Instantiate(fieldVisualPrefab, transform.position, Quaternion.identity);
        }

        private void DestroyVisual()
        {
            if (m_Visual == null) return;
            Destroy(m_Visual);
            m_Visual = null;
        }

        /// <summary>把常驻表现的位置与半径同步到当前运行时状态。</summary>
        private void SyncVisual()
        {
            if (m_Visual == null) return;
            float radius = CurrentRadius;
            if (radius <= 0f) return;
            m_Visual.transform.position = transform.position + Vector3.up * visualHeightOffset;
            float scale = radius * 2f / Mathf.Max(0.01f, visualBaseDiameter);
            m_Visual.transform.localScale = new Vector3(scale, m_Visual.transform.localScale.y, scale);
        }
    }
}
