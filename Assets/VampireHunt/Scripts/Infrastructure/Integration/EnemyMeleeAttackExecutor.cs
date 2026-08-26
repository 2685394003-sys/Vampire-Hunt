using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;

namespace VampireHunt.Infrastructure.Integration
{
    [DisallowMultipleComponent]
    public sealed class EnemyMeleeAttackExecutor : MonoBehaviour, IEnemyAttackExecutor
    {
        public bool TryExecute(in EnemyAttackExecutionContext context)
        {
            NetworkManager manager = NetworkManager.Singleton;
            NetworkObject target = context.Target;
            if (manager == null || !manager.IsListening || !manager.IsServer ||
                target == null || !target.IsSpawned ||
                Vector3.Distance(context.Origin, target.transform.position) > context.MaximumTargetDistance)
                return false;

            MonoBehaviour[] behaviours = target.GetComponents<MonoBehaviour>();
            IDamageReceiver damageReceiver = null;
            ICombatEntityIdentity identity = null;
            ICombatImpulseTarget impulseTarget = null;
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (damageReceiver == null && behaviours[i] is IDamageReceiver receiver)
                    damageReceiver = receiver;
                if (identity == null && behaviours[i] is ICombatEntityIdentity combatIdentity)
                    identity = combatIdentity;
                if (impulseTarget == null && behaviours[i] is ICombatImpulseTarget impulse)
                    impulseTarget = impulse;
            }

            if (damageReceiver == null) return false;
            var targetId = identity != null
                ? identity.CombatEntityId
                : new VampireHunt.SharedKernel.EntityId(target.OwnerClientId + 1UL);
            var request = new DamageRequest(
                context.Source,
                targetId,
                context.AttackId,
                context.AttackSequence,
                context.Damage,
                DamageTags.Melee);
            if (!damageReceiver.TryApplyDamage(request, out ResolvedDamage result) ||
                result.IsCancelled || result.Amount <= 0f)
                return false;

            if (impulseTarget != null && context.Knockback > 0f)
            {
                Vector3 impulse = context.Direction * context.Knockback;
                impulseTarget.TryApplyImpulse(new Float3(impulse.x, impulse.y, impulse.z));
            }
            return true;
        }
    }
}
