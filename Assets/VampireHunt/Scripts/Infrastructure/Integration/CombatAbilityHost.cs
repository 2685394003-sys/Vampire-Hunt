using System.Collections.Generic;
using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Combat;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Netcode;
using GameplayEntityId = VampireHunt.SharedKernel.EntityId;

namespace VampireHunt.Infrastructure.Integration
{
    /// <summary>Player-side application service for pure abilities and pact modifiers.</summary>
    [DisallowMultipleComponent]
    public sealed class CombatAbilityHost : MonoBehaviour, ICombatAbilityModifierTarget, IWeaponUnlockTarget
    {
        [SerializeField] private CoreStatsHandler coreStats;
        [SerializeField] private NetworkObject networkObject;
        [SerializeField] private Transform muzzle;
        [SerializeField] private CombatModifierHost damageModifiers;
        [SerializeField] private CombatAbilityNetworkBridge networkSink;
        [SerializeField] private CombatStatusHost statusHost;

        private readonly Dictionary<AbilitySlot, ICombatAbility> m_Abilities =
            new Dictionary<AbilitySlot, ICombatAbility>();
        private readonly Dictionary<uint, ICombatAbility> m_Weapons =
            new Dictionary<uint, ICombatAbility>();
        private readonly AbilityModifierCollection m_AbilityModifiers = new AbilityModifierCollection();
        private uint m_ActiveWeaponId;
        private IAttributeModifierTarget m_AttributeModifiers;
        private ICombatAimSource m_AimSource;
        private ulong m_Sequence;
        private bool m_Composed;

        public void Initialize(CorePlayerManager playerManager)
        {
            if (playerManager != null && coreStats == null) coreStats = playerManager.CoreStats;
            ComposeIfNeeded();
        }

        private void Awake()
        {
            if (coreStats == null) coreStats = GetComponent<CoreStatsHandler>();
            if (networkObject == null) networkObject = GetComponent<NetworkObject>();
            if (damageModifiers == null) damageModifiers = GetComponent<CombatModifierHost>();
            if (networkSink == null) networkSink = GetComponent<CombatAbilityNetworkBridge>();
            if (statusHost == null) statusHost = GetComponent<CombatStatusHost>();
            var behaviours = GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (m_AttributeModifiers == null && behaviours[i] is IAttributeModifierTarget target)
                    m_AttributeModifiers = target;
                if (m_AimSource == null && behaviours[i] is ICombatAimSource aimSource)
                    m_AimSource = aimSource;
            }
            ComposeIfNeeded();
        }

        private void ComposeIfNeeded()
        {
            if (m_Composed) return;
            m_Composed = true;
            var behaviours = GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (!(behaviours[i] is ICombatAbilityProvider provider)) continue;
                ICombatAbility ability = provider.CreateAbility();
                if (ability == null) continue;
                if (ability.Slot == AbilitySlot.Primary)
                {
                    // Primary slot holds switchable weapons, keyed by AbilityId (weapon id).
                    if (m_Weapons.ContainsKey(ability.AbilityId))
                    {
                        Debug.LogError($"[CombatAbilityHost] Duplicate primary weapon id {ability.AbilityId}.", behaviours[i]);
                        continue;
                    }
                    m_Weapons.Add(ability.AbilityId, ability);
                    if (m_ActiveWeaponId == 0) m_ActiveWeaponId = ability.AbilityId;
                }
                else if (m_Abilities.ContainsKey(ability.Slot))
                {
                    Debug.LogError($"[CombatAbilityHost] Duplicate ability slot {ability.Slot}.", behaviours[i]);
                    continue;
                }
                else
                {
                    m_Abilities.Add(ability.Slot, ability);
                }
            }
        }

        public bool TryActivate(AbilitySlot slot) => TryActivate(slot, null);

        /// <summary>
        /// 同 <see cref="TryActivate(AbilitySlot)"/>，但允许调用方指定原点来源（origin source）。
        /// 圆型领域用它把圆心固定在玩家自身（默认行为会用枪口 muzzle，会让领域偏到身前）。
        /// 传 null 时行为与旧签名完全一致。
        /// </summary>
        public bool TryActivate(AbilitySlot slot, Transform originSource)
        {
            ComposeIfNeeded();
            if (networkObject == null || !networkObject.IsSpawned || !networkObject.IsOwner ||
                coreStats == null || networkSink == null || statusHost != null && statusHost.IsActionBlocked) return false;

            if (!TryResolveAbility(slot, out ICombatAbility ability)) return false;

            Vector3 direction = transform.forward;
            Float3 aimPoint = default;
            if (m_AimSource != null && m_AimSource.TryGetAim(out AimSnapshot aim))
            {
                direction = ToVector3(aim.Direction);
                aimPoint = aim.WorldPoint;
            }
            direction.y = 0f;
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            Vector3 origin = originSource != null
                ? originSource.position
                : (muzzle != null ? muzzle.position : transform.position);
            ulong sequence = ++m_Sequence;
            var context = new CombatAbilityActivationContext(
                new GameplayEntityId(networkObject.OwnerClientId + 1UL), sequence,
                Time.unscaledTimeAsDouble, ToFloat3(origin), ToFloat3(direction),
                ReadAttribute(StatKeys.Damage),
                ReadAttribute(StatKeys.Cooldown),
                ReadAttribute(StatKeys.WeaponRange),
                ReadAttribute(StatKeys.CritRate),
                ReadAttribute(StatKeys.CritDamage),
                ReadAttribute(StatKeys.KnockbackForce), Random.value, aimPoint);

            if (!ability.TryBuildCast(context, out AbilityCastPlan plan)) return false;
            m_AbilityModifiers.Resolve(plan);
            if (plan.IsCancelled || plan.Damage <= 0f) return false;

            var request = new DamageRequest(plan.Caster, GameplayEntityId.None, plan.AbilityId,
                plan.Sequence, plan.Damage, plan.Tags);
            ResolvedDamage damage = damageModifiers != null
                ? damageModifiers.ResolveOutgoing(request)
                : new DamageContext(request).ToResult();
            if (damage.IsCancelled || damage.Amount <= 0f) return false;

            plan.Damage = damage.Amount;
            plan.Tags = damage.Tags;
            return networkSink.TryExecute(plan);
        }

        public void ResetAbilities()
        {
            foreach (ICombatAbility ability in m_Abilities.Values) ability.Reset();
            foreach (ICombatAbility ability in m_Weapons.Values) ability.Reset();
            m_Sequence = 0;
        }

        /// <summary>Switches the active primary weapon by its ability/weapon id. Local state; single-player suffices.</summary>
        public bool SetActiveWeapon(uint abilityId)
        {
            ComposeIfNeeded();
            if (!m_Weapons.ContainsKey(abilityId)) return false;
            m_ActiveWeaponId = abilityId;
            return true;
        }

        /// <summary>
        /// IWeaponUnlockTarget：「获得武器」类血契（2001/3001/4001/5001）生效时切换主武器。
        /// 由 Unlock 效果模块（Realm=Owner）经端口机制调用 —— 施法计划在本端构建，切这里才真正换出手。
        /// </summary>
        public bool TryUnlockWeapon(uint abilityId) => SetActiveWeapon(abilityId);

        /// <summary>The currently active primary weapon id.</summary>
        public uint ActiveWeaponId => m_ActiveWeaponId;

        private bool TryResolveAbility(AbilitySlot slot, out ICombatAbility ability)
        {
            if (slot == AbilitySlot.Primary)
            {
                return m_Weapons.TryGetValue(m_ActiveWeaponId, out ability);
            }
            return m_Abilities.TryGetValue(slot, out ability);
        }

        public bool RegisterAbilityModifier(IAbilityCastModifier modifier) => m_AbilityModifiers.Register(modifier);
        public bool UnregisterAbilityModifier(IAbilityCastModifier modifier) => m_AbilityModifiers.Unregister(modifier);
        private float ReadAttribute(int attributeId) => m_AttributeModifiers != null
            ? m_AttributeModifiers.GetFinalAttributeValue(attributeId)
            : coreStats.GetCurrentValue(attributeId);
        private static Float3 ToFloat3(Vector3 value) => new Float3(value.x, value.y, value.z);
        private static Vector3 ToVector3(Float3 value) => new Vector3(value.X, value.Y, value.Z);

        private void OnDestroy()
        {
            m_AbilityModifiers.Clear();
            m_Abilities.Clear();
            m_Weapons.Clear();
        }
    }
}
