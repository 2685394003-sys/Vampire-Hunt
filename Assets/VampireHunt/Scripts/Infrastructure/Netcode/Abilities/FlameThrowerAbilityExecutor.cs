using System.Collections.Generic;
using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;
using GameplayEntityId = VampireHunt.SharedKernel.EntityId;

namespace VampireHunt.Infrastructure.Netcode
{
    /// <summary>
    /// Server-side executor for the cone flamethrower. Each tick overlaps a sphere, keeps only
    /// targets inside the forward cone and submits trusted hits (with burning). Fixed Fire element.
    /// Presentation is replicated by <see cref="CombatAbilityNetworkBridge"/> after execution.
    /// </summary>
    public sealed class FlameThrowerAbilityExecutor : MonoBehaviour, ICombatAbilityNetworkExecutor
    {
        [SerializeField] private uint abilityId = 150;
        [SerializeField] private LayerMask targetMask = 1 << 8;

        private readonly Collider[] m_HitBuffer = new Collider[64];
        private readonly HashSet<MonoBehaviour> m_HitSet = new HashSet<MonoBehaviour>();
        private readonly CombatHitTargetResolver m_TargetResolver = new CombatHitTargetResolver();

        [SerializeField] private CombatIntentPolicy combatIntentPolicy = CombatIntentPolicy.Combat;
        public CombatIntentPolicy IntentPolicy => combatIntentPolicy;

        public uint AbilityId => abilityId;

        public bool CanExecuteServer(NetworkManager manager, ulong senderClientId, in AbilityCastNetworkMessage message)
        {
            if (manager == null || !manager.IsServer || message.AbilityId != abilityId) return false;
            return true;
        }

        public bool ExecuteServer(NetworkManager manager, ulong senderClientId, in AbilityCastNetworkMessage message)
        {
            if (manager == null || !manager.IsServer || message.AbilityId != abilityId) return false;

            Vector3 origin = message.Origin;
            Vector3 direction = message.Direction.sqrMagnitude > 0.0001f
                ? message.Direction.normalized
                : transform.forward;

            float range = Mathf.Max(0.1f, message.TravelDistance);
            float coneAngle = Mathf.Max(0f, message.SpreadAngle);

            int overlapCount = Physics.OverlapSphereNonAlloc(origin, range, m_HitBuffer, targetMask,
                QueryTriggerInteraction.Collide);
            m_HitSet.Clear();
            int statusCount = Mathf.Min(4, message.OnHitStatuses.Count);
            var statuses = new StatusEffectSpec[statusCount];
            for (int s = 0; s < statusCount; s++)
                statuses[s] = message.OnHitStatuses.Get(s).ToDomain();

            for (int i = 0; i < overlapCount; i++)
            {
                Collider hitCollider = m_HitBuffer[i];
                if (hitCollider == null ||
                    !m_TargetResolver.TryResolve(hitCollider, out MonoBehaviour target,
                        out ITrustedCombatHitTarget trustedTarget, out IHittable fallback)) continue;
                if (!m_HitSet.Add(target)) continue;

                // Keep only targets inside the forward cone (planar, top-down).
                Vector3 toTarget = hitCollider.ClosestPoint(origin) - origin;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude < 0.0001f) continue;
                if (Vector3.Angle(direction, toTarget.normalized) > coneAngle * 0.5f) continue;

                var request = new DamageRequest(
                    new GameplayEntityId(senderClientId + 1UL), GameplayEntityId.None,
                    message.AbilityId, message.Sequence, message.Damage, (DamageTags)message.Tags);

                Vector3 force = direction * message.Knockback;

                if (trustedTarget != null)
                {
                    trustedTarget.SubmitTrustedHit(new TrustedCombatHit(request,
                        (ElementId)message.Element, statuses, new Float3(force.x, force.y, force.z)));
                }
                else
                {
                    fallback.OnHit(new HitInfo
                    {
                        amount = message.Damage,
                        hitPoint = hitCollider.ClosestPoint(origin),
                        hitNormal = -direction,
                        attackerId = senderClientId,
                        impactForce = force
                    });
                }
            }

            return true;
        }
    }
}
