using System.Collections.Generic;
using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;
using GameplayEntityId = VampireHunt.SharedKernel.EntityId;

namespace VampireHunt.Infrastructure.Netcode
{
    /// <summary>
    /// Parabola bomb projectile (White Pigeon / Peachone style).
    ///
    /// Ballistics: lobbed arc with an initial elevation (<see cref="arcAngle"/>) and gravity.
    /// Flight: pierces enemies it touches — single-target damage + on-hit statuses, consuming
    /// PierceCount each hit. Pierce exhausted => destroyed immediately, no explosion.
    /// Landing: on ground contact or reaching max travel, if PierceCount still &gt; 0, detonate
    /// an OverlapSphere AoE (blast damage + element/status) — otherwise it is destroyed silently.
    /// explodeOnLandingOnly: skips all in-flight hit detection (intangible until landing) and
    /// always detonates on ground contact / max travel. Used by the missile bomb.
    /// </summary>
    public sealed class ParabolaBombProjectile : NetworkBehaviour
    {
        [Header("Ballistics")]
        [Tooltip("Gravity acceleration applied to the bomb (m/s^2).")]
        [SerializeField] private float gravity = 9.8f;
        [Tooltip("World Y threshold below which the bomb counts as touching the ground.")]
        [SerializeField] private float groundY = 0.05f;

        [Header("Flight hit (pierce)")]
        [Tooltip("Radius of the flight hit sphere (bomb trigger size).")]
        [Min(0.05f)] [SerializeField] private float hitRadius = 0.5f;
        [Tooltip("勾选后：飞行途中不做任何命中检测（无实体、不撞击），落地或到达最大距离时无条件爆炸。用于导弹等纯落地炸弹。")]
        [SerializeField] private bool explodeOnLandingOnly = false;

        [Header("Blast")]
        [Tooltip("Blast damage = flight damage x this multiplier.")]
        [Min(0f)] [SerializeField] private float blastDamageMultiplier = 1f;
        [SerializeField] private GameObject blastVfxPrefab;
        [Min(0.1f)] [SerializeField] private float blastVfxLifetime = 1.5f;
        [Tooltip("爆炸特效的基准视觉直径（米）。生成时按 爆炸直径/此值 缩放 VFX，让视觉圈对齐判定范围。")]
        [Min(0.1f)] [SerializeField] private float blastVfxBaseDiameter = 4f;

        [Header("Audio (Wwise)")]
        [Tooltip("Wwise 事件名（飞行命中音），对应《策划版音频调用表》。留空不发声。")]
        [SerializeField] private string hitEventName = "";
        [Tooltip("Wwise 事件名（落地爆炸音），对应《策划版音频调用表》。留空不发声。")]
        [SerializeField] private string explosionEventName = "";

        [Header("Layers")]
        [SerializeField] private LayerMask targetMask = 1 << 8;

        private readonly NetworkVariable<ulong> m_AttackerClientId = new NetworkVariable<ulong>(
            0UL, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<AbilityCastNetworkMessage> m_Cast =
            new NetworkVariable<AbilityCastNetworkMessage>(default,
                NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly Collider[] m_HitBuffer = new Collider[32];
        private readonly Collider[] m_BlastBuffer = new Collider[64];
        private readonly HashSet<MonoBehaviour> m_HitTargets = new HashSet<MonoBehaviour>();

        private ulong m_PendingAttackerClientId;
        private AbilityCastNetworkMessage m_PendingCast;
        private bool m_HasPendingConfiguration;

        private Vector3 m_Velocity;
        private Vector3 m_StartPosition;
        private float m_MaxDistance;
        private float m_BlastRadius;
        private int m_PierceCount;
        private bool m_Completed;

        public void ConfigureServer(ulong attackerClientId, in AbilityCastNetworkMessage message)
        {
            m_PendingAttackerClientId = attackerClientId;
            m_PendingCast = message;
            m_HasPendingConfiguration = true;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer && m_HasPendingConfiguration)
            {
                m_AttackerClientId.Value = m_PendingAttackerClientId;
                m_Cast.Value = m_PendingCast;
                m_HasPendingConfiguration = false;
            }
            Launch();
        }

        private void Launch()
        {
            AbilityCastNetworkMessage cast = m_Cast.Value;
            Vector3 direction = cast.Direction.sqrMagnitude > 0.0001f ? cast.Direction.normalized : Vector3.forward;
            float speed = Mathf.Max(0.1f, cast.ProjectileSpeed);

            // Elevation is carried on SpreadAngle; blast radius on ProjectileSize.
            float elevation = Mathf.Clamp(cast.SpreadAngle, 0f, 89.9f) * Mathf.Deg2Rad;
            m_Velocity = direction * (speed * Mathf.Cos(elevation)) + Vector3.up * (speed * Mathf.Sin(elevation));

            m_StartPosition = transform.position;
            m_MaxDistance = Mathf.Max(0.1f, cast.TravelDistance);
            m_BlastRadius = Mathf.Max(0.1f, cast.ProjectileSize);
            m_PierceCount = Mathf.Max(0, cast.PierceCount);
            m_HitTargets.Clear();
        }

        private void Update()
        {
            if (!IsServer || m_Completed || !NetworkObject.IsSpawned) return;

            m_Velocity.y -= gravity * Time.deltaTime;
            transform.position += m_Velocity * Time.deltaTime;

            // explodeOnLandingOnly：飞行途中不检测命中（无实体、不撞击），直接进入落地判定
            if (!explodeOnLandingOnly && DetectEnemyHit()) return;   // pierce exhausted => destroyed, no explosion
            if (CheckLanding()) return;      // ground / max travel => explode (if pierce left)
        }

        /// <summary>Flight piercing: single-target damage + statuses, consuming PierceCount. Returns true when the bomb is consumed.</summary>
        private bool DetectEnemyHit()
        {
            int count = Physics.OverlapSphereNonAlloc(transform.position, hitRadius, m_HitBuffer, targetMask,
                QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                Collider hitCollider = m_HitBuffer[i];
                if (hitCollider == null ||
                    !TryFindTarget(hitCollider, out MonoBehaviour target,
                        out ITrustedCombatHitTarget trustedTarget, out IHittable fallback)) continue;
                if (!m_HitTargets.Add(target)) continue;

                SubmitDamage(trustedTarget, fallback, hitCollider, m_Cast.Value.Damage);
                if (!string.IsNullOrEmpty(hitEventName)) WwiseAudioBridge.PostEvent(hitEventName, gameObject);
                m_PierceCount--;

                if (m_PierceCount <= 0)
                {
                    Complete();
                    return true;
                }
            }
            return false;
        }

        /// <summary>Ground contact or max travel. Explodes only if PierceCount still &gt; 0.</summary>
        private bool CheckLanding()
        {
            bool hitGround = transform.position.y <= groundY;
            bool reachedMax = (transform.position - m_StartPosition).sqrMagnitude >= m_MaxDistance * m_MaxDistance;
            if (!hitGround && !reachedMax) return false;

            if (m_PierceCount > 0 || explodeOnLandingOnly) Explode();
            else Complete();
            return true;
        }

        private void Explode()
        {
            if (m_Completed) return;

            if (!string.IsNullOrEmpty(explosionEventName)) WwiseAudioBridge.PostEvent(explosionEventName, gameObject);

            float blastDamage = m_Cast.Value.Damage * blastDamageMultiplier;
            var blastTargets = new HashSet<MonoBehaviour>();
            int count = Physics.OverlapSphereNonAlloc(transform.position, m_BlastRadius, m_BlastBuffer, targetMask,
                QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                Collider hitCollider = m_BlastBuffer[i];
                if (hitCollider == null ||
                    !TryFindTarget(hitCollider, out MonoBehaviour target,
                        out ITrustedCombatHitTarget trustedTarget, out IHittable fallback)) continue;
                if (!blastTargets.Add(target)) continue;

                SubmitDamage(trustedTarget, fallback, hitCollider, blastDamage);
            }

            if (blastVfxPrefab != null)
            {
                GameObject vfx = Instantiate(blastVfxPrefab, transform.position, Quaternion.identity);
                // 缩放 VFX，让视觉直径 = 爆炸直径（2 × 爆炸半径），对齐判定范围
                float targetDiameter = m_BlastRadius * 2f;
                float scale = blastVfxBaseDiameter > 0.01f ? targetDiameter / blastVfxBaseDiameter : 1f;
                vfx.transform.localScale = Vector3.one * scale;
                Destroy(vfx, blastVfxLifetime);
            }

            Complete();
        }

        private void SubmitDamage(ITrustedCombatHitTarget trustedTarget, IHittable fallback, Collider hitCollider, float damage)
        {
            AbilityCastNetworkMessage cast = m_Cast.Value;
            var request = new DamageRequest(
                new GameplayEntityId(m_AttackerClientId.Value + 1UL), GameplayEntityId.None,
                cast.AbilityId, cast.Sequence, damage, (DamageTags)cast.Tags);

            int statusCount = Mathf.Min(4, cast.OnHitStatuses.Count);
            var statuses = new StatusEffectSpec[statusCount];
            for (int i = 0; i < statusCount; i++)
                statuses[i] = cast.OnHitStatuses.Get(i).ToDomain();

            Vector3 force = (hitCollider.bounds.center - transform.position).normalized * cast.Knockback;

            if (trustedTarget != null)
            {
                trustedTarget.SubmitTrustedHit(new TrustedCombatHit(request,
                    (ElementId)cast.Element, statuses, new Float3(force.x, force.y, force.z)));
            }
            else
            {
                fallback.OnHit(new HitInfo
                {
                    amount = damage,
                    hitPoint = hitCollider.ClosestPoint(transform.position),
                    hitNormal = Vector3.up,
                    attackerId = m_AttackerClientId.Value,
                    impactForce = force
                });
            }
        }

        private void Complete()
        {
            if (m_Completed) return;
            m_Completed = true;
            if (NetworkObject != null && NetworkObject.IsSpawned) NetworkObject.Despawn();
            else Destroy(gameObject);
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

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.6f, 0f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, hitRadius);
            Gizmos.color = new Color(1f, 0.2f, 0f, 0.4f);
            Gizmos.DrawWireSphere(transform.position, m_BlastRadius);
        }
    }
}
