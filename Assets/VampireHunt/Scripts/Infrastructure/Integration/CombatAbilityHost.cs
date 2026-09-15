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
    public sealed class CombatAbilityHost : MonoBehaviour, ICombatAbilityModifierTarget
    {
        [SerializeField] private CoreStatsHandler coreStats;
        [SerializeField] private NetworkObject networkObject;
        [SerializeField] private Transform muzzle;
        [SerializeField] private CombatModifierHost damageModifiers;
        [SerializeField] private CombatAbilityNetworkBridge networkSink;
        [SerializeField] private CombatStatusHost statusHost;

        private readonly Dictionary<AbilitySlot, ICombatAbility> m_Abilities =
            new Dictionary<AbilitySlot, ICombatAbility>();
        private readonly AbilityModifierCollection m_AbilityModifiers = new AbilityModifierCollection();
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
                if (m_Abilities.ContainsKey(ability.Slot))
                {
                    Debug.LogError($"[CombatAbilityHost] Duplicate ability slot {ability.Slot}.", behaviours[i]);
                    continue;
                }
                m_Abilities.Add(ability.Slot, ability);
            }
        }

        public bool TryActivate(AbilitySlot slot)
        {
            ComposeIfNeeded();
            if (networkObject == null || !networkObject.IsSpawned || !networkObject.IsOwner ||
                coreStats == null || networkSink == null || statusHost != null && statusHost.IsActionBlocked ||
                !m_Abilities.TryGetValue(slot, out ICombatAbility ability)) return false;

            Vector3 direction = transform.forward;
            if (m_AimSource != null && m_AimSource.TryGetAim(out AimSnapshot aim))
            {
                direction = ToVector3(aim.Direction);
            }
            direction.y = 0f;
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            Vector3 origin = muzzle != null ? muzzle.position : transform.position;
            ulong sequence = ++m_Sequence;
            var context = new CombatAbilityActivationContext(
                new GameplayEntityId(networkObject.OwnerClientId + 1UL), sequence,
                Time.unscaledTimeAsDouble, ToFloat3(origin), ToFloat3(direction),
                ReadAttribute(StatKeys.Damage),
                ReadAttribute(StatKeys.Cooldown),
                ReadAttribute(StatKeys.WeaponRange),
                ReadAttribute(StatKeys.CritRate),
                ReadAttribute(StatKeys.CritDamage),
                ReadAttribute(StatKeys.KnockbackForce), Random.value);

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
            m_Sequence = 0;
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
        }
    }
}
