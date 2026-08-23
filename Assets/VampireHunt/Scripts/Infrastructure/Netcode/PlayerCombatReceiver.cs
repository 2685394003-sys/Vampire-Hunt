using Blocks.Gameplay.Core;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Integration;
using VampireHunt.Infrastructure.Unity;
using GameplayEntityId = VampireHunt.SharedKernel.EntityId;

namespace VampireHunt.Infrastructure.Netcode
{
    /// <summary>
    /// Client-server adapter for damage applied to a player. HitProcessor routes
    /// the submitted result to the server authority; no hit validation is added.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerCombatReceiver : HitProcessor, IScarletRewardReceiver, IDamageReceiver, ICombatEntityIdentity
    {
        private const uint EnemyMeleeAttackId = 2;

        [SerializeField] private CoreStatsHandler coreStats;
        [SerializeField] private CoreMovement coreMovement;
        [SerializeField] private CombatModifierHost modifierHost;
        [SerializeField] private DamagePresentationEvent onDamagePresented;

        private ulong m_IncomingSequence;

        public GameplayEntityId CombatEntityId => new GameplayEntityId(OwnerClientId + 1UL);

        private void Awake()
        {
            if (coreStats == null) coreStats = GetComponent<CoreStatsHandler>();
            if (coreMovement == null) coreMovement = GetComponent<CoreMovement>();
            if (modifierHost == null) modifierHost = GetComponent<CombatModifierHost>();
        }

        protected override void HandleHit(HitInfo info)
        {
            if (!IsServer || coreStats == null || info.amount <= 0f) return;

            var request = new DamageRequest(
                new GameplayEntityId(info.attackerId),
                new GameplayEntityId(OwnerClientId + 1UL),
                EnemyMeleeAttackId,
                ++m_IncomingSequence,
                info.amount,
                DamageTags.Melee);

            if (!TryApplyDamage(request, out _)) return;

            if (info.impactForce.sqrMagnitude > 0f)
            {
                ApplyKnockbackRpc(info.impactForce);
            }

        }

        public bool TryApplyDamage(in DamageRequest request, out ResolvedDamage result)
        {
            result = modifierHost != null
                ? modifierHost.ResolveIncoming(request)
                : new DamageContext(request).ToResult();
            if (!IsServer || coreStats == null || result.IsCancelled || result.Amount <= 0f) return false;

            float healthBefore = Mathf.Max(0f, coreStats.GetCurrentValue(StatKeys.Health));
            coreStats.ModifyStat(StatKeys.Health, -result.Amount, request.Source.Value, ModificationSource.Damage);
            float healthAfter = Mathf.Max(0f, coreStats.GetCurrentValue(StatKeys.Health));
            var resolution = new CombatResolutionRecord(result, healthBefore, healthAfter);
            modifierHost?.NotifyOutcome(result);
            RaiseDamagePresentationRpc(request.Source.Value, CombatEntityId.Value, request.AttackId,
                request.Sequence, result.Amount, (uint)result.Tags, transform.position + Vector3.up);
            ServerCombatResolutionRouter.Publish(NetworkManager, resolution);
            return true;
        }

        [global::Unity.Netcode.Rpc(
            global::Unity.Netcode.SendTo.ClientsAndHost,
            InvokePermission = global::Unity.Netcode.RpcInvokePermission.Server)]
        private void RaiseDamagePresentationRpc(ulong sourceEntityId, ulong targetEntityId,
            uint attackId, ulong sequence, float amount, uint tags, Vector3 worldPosition)
        {
            onDamagePresented?.Raise(new DamagePresentationPayload
            {
                sourceEntityId = sourceEntityId,
                targetEntityId = targetEntityId,
                attackId = attackId,
                sequence = sequence,
                amount = amount,
                tags = (DamageTags)tags,
                worldPosition = worldPosition
            });
        }

        [global::Unity.Netcode.Rpc(
            global::Unity.Netcode.SendTo.Owner,
            InvokePermission = global::Unity.Netcode.RpcInvokePermission.Server)]
        private void ApplyKnockbackRpc(Vector3 force)
        {
            if (coreMovement != null)
            {
                coreMovement.ApplyExternalForce(force, ForceMode.Impulse);
            }
        }

        /// <summary>Server-side implementation of the cross-module reward port.</summary>
        public bool TryGrantScarlet(float amount, GameplayEntityId sourceEntityId)
        {
            if (!IsServer || coreStats == null || amount <= 0f) return false;
            coreStats.ModifyStat(StatKeys.Scarlet, amount, sourceEntityId.Value, ModificationSource.Direct);
            return true;
        }
    }
}
