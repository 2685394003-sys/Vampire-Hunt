using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Integration;
using VampireHunt.Infrastructure.Netcode;

namespace VampireHunt.Player.Abilities
{
    /// <summary>
    /// 元素三系（v2.2）测试用开关，仅在单机/调试期使用：
    /// Digit1 = 清除：移除当前武器的元素注入，并清掉 <see cref="clearRadius"/> 内所有敌人 + 玩家自身的
    ///          灼烧/霜寒/冻结/闪电/燃爆/引雷；
    /// Digit2 = 给<b>当前武器</b>注入「火」元素（Element=Fire + 命中叠 1 层灼烧 Burn，满 5 触发燃爆）；
    /// Digit3 = 给<b>当前武器</b>注入「冰」元素（Element=Ice + 命中叠 1 层霜寒 Frost，满 5 触发冻结）；
    /// Digit4 = 给<b>当前武器</b>注入「雷」元素（Element=Lightning + 命中叠 1 层闪电 Lightning，满 5 触发引雷）；
    /// Digit5 = 唤出一只带「冰」元素的撞击水滴使魔（ImpactFamiliar）；
    /// Digit6 = 唤出一只带「火」元素的射击僚机使魔（GunnerFamiliar）。
    /// </summary>
    /// <remarks>
    /// 武器元素通过 <see cref="IAbilityCastModifier"/> 注入 <see cref="AbilityCastPlan"/> 的
    /// <c>Element</c> 与 <c>OnHitStatuses</c>，玩家用武器命中敌人时走完整结算链路（含元素反应），
    /// 与真实元素武器命中完全一致。
    /// 需要 <b>Start Host</b>（服务器）才生效；纯客户端会收到警告并忽略。
    /// 血契系统实装后本脚本可整体删除。
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class DebugEffectSwitcher : MonoBehaviour, IAbilityCastModifier
    {
        [Header("元素测试")]
        [Tooltip("Digit1 清除元素状态时的扫描半径（米），比命中范围大，防止敌人走出范围清不掉。")]
        [SerializeField] private float clearRadius = 30f;
        [Tooltip("每次命中施加的元素层数（默认 1，连打叠层；霜寒满 5 冻结、闪电满 5 引雷、灼烧满 5 燃爆）。")]
        [SerializeField] private int stacksPerPress = 1;

        [Header("调试输出")]
        [Tooltip("切换时在 Console 打印当前状态，方便确认按键是否生效。")]
        [SerializeField] private bool logToConsole = true;

        /// <summary>元素测试涉及的 6 个状态 id（v2.2）。</summary>
        private static readonly uint[] ElementStatusIds =
        {
            StatusEffectIds.Burn,
            StatusEffectIds.Frost,
            StatusEffectIds.Frozen,
            StatusEffectIds.Lightning,
            StatusEffectIds.Explode,
            StatusEffectIds.Thunder
        };

        private CombatAbilityHost m_AbilityHost;
        private ImpactFamiliarController m_ImpactFamiliar;
        private GunnerFamiliarController m_GunnerFamiliar;
        private NetworkObject m_NetworkObject;
        private readonly List<EnemyNetworkActor> m_EnemyBuffer = new List<EnemyNetworkActor>();
        private readonly Collider[] m_ColliderBuffer = new Collider[64];

        private ElementId m_WeaponElement = ElementId.None;
        private uint m_WeaponStatusId;
        private bool m_ModifierRegistered;

        public int Priority => 300;

        private void Awake()
        {
            m_NetworkObject = GetComponentInParent<NetworkObject>();
            m_AbilityHost = GetComponent<CombatAbilityHost>();
            m_ImpactFamiliar = GetComponent<ImpactFamiliarController>();
            m_GunnerFamiliar = GetComponent<GunnerFamiliarController>();
        }

        private void OnDisable()
        {
            if (m_AbilityHost != null && m_ModifierRegistered)
            {
                m_AbilityHost.UnregisterAbilityModifier(this);
                m_ModifierRegistered = false;
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.digit1Key.wasPressedThisFrame) ClearElement();
            if (keyboard.digit2Key.wasPressedThisFrame) SetWeaponElement(ElementId.Fire, StatusEffectIds.Burn);
            if (keyboard.digit3Key.wasPressedThisFrame) SetWeaponElement(ElementId.Ice, StatusEffectIds.Frost);
            if (keyboard.digit4Key.wasPressedThisFrame) SetWeaponElement(ElementId.Lightning, StatusEffectIds.Lightning);
            if (keyboard.digit5Key.wasPressedThisFrame) SummonImpactFamiliar();
            if (keyboard.digit6Key.wasPressedThisFrame) SummonGunnerFamiliar();
        }

        // ── IAbilityCastModifier：给当前武器注入元素与命中状态 ──

        /// <summary>
        /// 血契/元素注入的挂点：每次玩家施放武器前被 <see cref="CombatAbilityHost"/> 调用，
        /// 把当前调试元素写入施法计划，命中即叠层。与 <c>ApplyStatusOnHitRuntime</c> 同一机制。
        /// </summary>
        public void Modify(AbilityCastPlan plan)
        {
            if (plan == null || m_WeaponElement == ElementId.None) return;
            if (plan.Element == ElementId.None) plan.Element = m_WeaponElement;
            // 属性精通：影响所有元素效果。挂元素层数 = 基础层数 × 武器属性精通，
            // 并把属性精通烙到状态规格上（闪电连锁传导时按「源层数 × 属性精通」复制）。
            float mastery = plan.ElementMastery;
            plan.OnHitStatuses.Add(new StatusEffectSpec(
                m_WeaponStatusId, (float)Mathf.Max(1, stacksPerPress) * mastery, 0f, 0f, m_WeaponElement, mastery));
            plan.Tags |= DamageTags.Pact;
        }

        /// <summary>给当前武器注入指定元素（Digit2/3/4）。</summary>
        public void SetWeaponElement(ElementId element, uint statusId)
        {
            if (!IsServerAuthoritative())
            {
                Log($"Digit2-4 - 需要 Start Host（服务器）才能给武器注入元素（{ElementName(element)}）", true);
                return;
            }

            m_WeaponElement = element;
            m_WeaponStatusId = statusId;
            if (m_AbilityHost != null && !m_ModifierRegistered)
            {
                m_AbilityHost.RegisterAbilityModifier(this);
                m_ModifierRegistered = true;
            }
            Log($"当前武器已注入「{ElementName(element)}」元素（命中叠 1 层状态 {statusId}）");
        }

        /// <summary>清除（Digit1）：移除武器元素注入 + 清掉范围内敌人与玩家自身的元素状态。</summary>
        public void ClearElement()
        {
            if (m_AbilityHost != null && m_ModifierRegistered)
            {
                m_AbilityHost.UnregisterAbilityModifier(this);
                m_ModifierRegistered = false;
            }
            m_WeaponElement = ElementId.None;
            m_WeaponStatusId = 0;

            if (!IsServerAuthoritative())
            {
                Log("Digit1 - 需要 Start Host（服务器）才能清除元素状态", true);
                return;
            }

            int cleared = 0;
            int enemyCount = CollectEnemies(clearRadius);
            for (int i = 0; i < m_EnemyBuffer.Count; i++)
            {
                if (!m_EnemyBuffer[i].TryGetComponent(out CombatStatusHost host)) continue;
                for (int j = 0; j < ElementStatusIds.Length; j++)
                    if (host.RemoveStatus(ElementStatusIds[j])) cleared++;
            }

            // 玩家自身也清（防止测试过程中玩家被冻结等影响操作）。
            if (TryGetComponent(out CombatStatusHost self))
            {
                for (int j = 0; j < ElementStatusIds.Length; j++)
                    if (self.RemoveStatus(ElementStatusIds[j])) cleared++;
            }

            Log($"Digit1 - 已移除武器元素并清除元素状态（共 {cleared} 个；{clearRadius}m 内敌人 {enemyCount} 只 + 玩家自身）");
        }

        /// <summary>唤出带「冰」元素的撞击水滴使魔（Digit5）。</summary>
        public void SummonImpactFamiliar()
        {
            if (m_ImpactFamiliar == null)
            {
                Log("Digit5 - 未找到 ImpactFamiliarController（玩家身上未挂载）", true);
                return;
            }
            m_ImpactFamiliar.SetElement(ElementId.Ice,
                new[] { new StatusEffectSpec(StatusEffectIds.Frost, 1, 0f, 0f, ElementId.Ice) });
            m_ImpactFamiliar.SetActive(true);
            Log("Digit5 - 已唤出「冰」属性撞击水滴使魔（命中叠 1 层霜寒）");
        }

        /// <summary>唤出带「火」元素的射击僚机使魔（Digit6）。</summary>
        public void SummonGunnerFamiliar()
        {
            if (m_GunnerFamiliar == null)
            {
                Log("Digit6 - 未找到 GunnerFamiliarController（玩家身上未挂载）", true);
                return;
            }
            m_GunnerFamiliar.SetElement(ElementId.Fire,
                new[] { new StatusEffectSpec(StatusEffectIds.Burn, 1, 0f, 0f, ElementId.Fire) });
            m_GunnerFamiliar.SetActive(true);
            Log("Digit6 - 已唤出「火」属性射击僚机使魔（命中叠 1 层灼烧）");
        }

        /// <summary>收集以玩家为圆心、半径内所有敌人的引用（去重），结果写入 <see cref="m_EnemyBuffer"/>。</summary>
        private int CollectEnemies(float radius)
        {
            m_EnemyBuffer.Clear();
            int overlapCount = Physics.OverlapSphereNonAlloc(
                transform.position, radius, m_ColliderBuffer);
            for (int i = 0; i < overlapCount; i++)
            {
                EnemyNetworkActor actor = m_ColliderBuffer[i].GetComponentInParent<EnemyNetworkActor>();
                if (actor == null || m_EnemyBuffer.Contains(actor)) continue;
                m_EnemyBuffer.Add(actor);
            }
            return m_EnemyBuffer.Count;
        }

        /// <summary>是否具备服务器权威：Host/Server 放行；纯客户端（未 Start Host）阻止并提示。</summary>
        private bool IsServerAuthoritative()
        {
            if (m_NetworkObject == null) return NetworkManager.Singleton == null || NetworkManager.Singleton.IsServer;
            NetworkManager manager = m_NetworkObject.NetworkManager ?? NetworkManager.Singleton;
            return manager == null || manager.IsServer;
        }

        private static string ElementName(ElementId element) => element switch
        {
            ElementId.Fire => "火",
            ElementId.Ice => "冰",
            ElementId.Lightning => "雷",
            _ => element.ToString()
        };

        private void Log(string message, bool warning = false)
        {
            if (!logToConsole) return;
            if (warning) Debug.LogWarning($"[DebugEffectSwitcher] {message}", this);
            else Debug.Log($"[DebugEffectSwitcher] {message}", this);
        }
    }
}
