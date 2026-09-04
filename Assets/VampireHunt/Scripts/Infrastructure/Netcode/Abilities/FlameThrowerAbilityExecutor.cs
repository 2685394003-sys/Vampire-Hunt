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
    /// targets inside the forward cone, submits trusted hits (with burning), and spawns a
    /// short-lived flame visual. Fixed Fire element.
    /// </summary>
    public sealed class FlameThrowerAbilityExecutor : MonoBehaviour, ICombatAbilityNetworkExecutor
    {
        [SerializeField] private uint abilityId = 150;
        [SerializeField] private GameObject flameVfxPrefab;
        [SerializeField] private LayerMask targetMask = 1 << 8;
        [SerializeField] private float vfxLifetime = 0.15f;

        private readonly Collider[] m_HitBuffer = new Collider[64];

        public uint AbilityId => abilityId;

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
            var hitSet = new HashSet<MonoBehaviour>();

            for (int i = 0; i < overlapCount; i++)
            {
                Collider hitCollider = m_HitBuffer[i];
                if (hitCollider == null ||
                    !TryFindTarget(hitCollider, out MonoBehaviour target,
                        out ITrustedCombatHitTarget trustedTarget, out IHittable fallback)) continue;
                if (!hitSet.Add(target)) continue;

                // Keep only targets inside the forward cone (planar, top-down).
                Vector3 toTarget = hitCollider.ClosestPoint(origin) - origin;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude < 0.0001f) continue;
                if (Vector3.Angle(direction, toTarget.normalized) > coneAngle * 0.5f) continue;

                var request = new DamageRequest(
                    new GameplayEntityId(senderClientId + 1UL), GameplayEntityId.None,
                    message.AbilityId, message.Sequence, message.Damage, (DamageTags)message.Tags);

                int statusCount = Mathf.Min(4, message.OnHitStatuses.Count);
                var statuses = new StatusEffectSpec[statusCount];
                for (int s = 0; s < statusCount; s++)
                    statuses[s] = message.OnHitStatuses.Get(s).ToDomain();

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

            if (flameVfxPrefab != null)
            {
                GameObject flame = Instantiate(flameVfxPrefab, origin, Quaternion.LookRotation(direction, Vector3.up));
                BindFlameRange(flame, range);
            }

            return true;
        }

        /// <summary>
        /// Keeps the flame particle throw distance in sync with the runtime range: the prefab's
        /// <see cref="ParticleSystem.MainModule.startSpeed"/> stays as authored (fire feel), while
        /// <see cref="ParticleSystem.MainModule.startLifetime"/> is recomputed so
        /// speed x lifetime == range. The visual then matches the damage cone automatically.
        /// </summary>
        private void BindFlameRange(GameObject flame, float range)
        {
            var ps = flame != null ? flame.GetComponentInChildren<ParticleSystem>() : null;
            if (ps == null)
            {
                if (flame != null) Destroy(flame, vfxLifetime);
                return;
            }

            var main = ps.main;
            float speed = main.startSpeed.constant;
            float lifetime = speed > 0.01f ? range / speed : vfxLifetime;
            main.startLifetime = lifetime;
            Destroy(flame, lifetime + 0.05f);
        }

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
