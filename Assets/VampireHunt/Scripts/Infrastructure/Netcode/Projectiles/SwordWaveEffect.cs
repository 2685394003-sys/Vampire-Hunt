using System;
using System.Collections.Generic;
using Blocks.Gameplay.Core;
using Blocks.Gameplay.Shooter;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Integration;
using GameplayEntityId = VampireHunt.SharedKernel.EntityId;

namespace VampireHunt.Infrastructure.Netcode
{
    /// <summary>Owner-side trusted hit detector for a networked sword wave.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ModularProjectile))]
    public sealed class SwordWaveEffect : NetworkBehaviour, IProjectileEffect
    {
        [Header("Hit Detection")]
        [Min(0.05f)] [SerializeField] private float hitRadius = 0.65f;
        [SerializeField] private LayerMask targetMask = 1 << 8;
        [Header("Fallback Lifetime")]
        [Min(0.1f)] [SerializeField] private float defaultMaximumDistance = 12f;
        [Min(0.1f)] [SerializeField] private float maximumLifetime = 2f;

        private readonly NetworkVariable<ulong> m_AttackerClientId = new NetworkVariable<ulong>(
            0UL, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<AbilityCastNetworkMessage> m_Cast =
            new NetworkVariable<AbilityCastNetworkMessage>(default,
                NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly Collider[] m_HitBuffer = new Collider[32];
        private readonly HashSet<MonoBehaviour> m_HitTargets = new HashSet<MonoBehaviour>();
        private ModularProjectile m_Projectile;
        private Rigidbody m_Rigidbody;
        private CombatModifierHost m_OwnerModifierHost;
        private ulong m_PendingAttackerClientId;
        private AbilityCastNetworkMessage m_PendingCast;
        private bool m_HasPendingConfiguration;
        private Vector3 m_StartPosition;
        private float m_ExpireTime;
        private int m_HitCount;
        private bool m_CompletionRequested;

        public bool IsDeferredDespawnEnabled => false;
        public int DeferredDespawnTicks => 0;
        public event Action<ModularProjectile> OnEffectComplete;

        public void ConfigureServer(ulong attackerClientId, in AbilityCastNetworkMessage message)
        {
            m_PendingAttackerClientId = attackerClientId;
            m_PendingCast = message;
            m_HasPendingConfiguration = true;
        }

        public void Initialize(ModularProjectile projectile)
        {
            m_Projectile = projectile;
            m_Rigidbody = GetComponent<Rigidbody>();
        }

        public void Setup(GameObject owner, IWeapon sourceWeapon, ShootingContext context) { }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer && m_HasPendingConfiguration)
            {
                m_AttackerClientId.Value = m_PendingAttackerClientId;
                m_Cast.Value = m_PendingCast;
                m_HasPendingConfiguration = false;
            }

            ApplyRuntimeProjectileValues();
            if (IsOwner && !IsServer) OnLaunch();
        }

        private void ApplyRuntimeProjectileValues()
        {
            AbilityCastNetworkMessage cast = m_Cast.Value;
            if (TryGetComponent<StraightLineMovement>(out var movement) && cast.ProjectileSpeed > 0f)
                movement.SetVelocityRate(cast.ProjectileSpeed);
            if (cast.ProjectileSize > 0f) transform.localScale = Vector3.one * cast.ProjectileSize;
        }

        public void OnLaunch()
        {
            m_StartPosition = transform.position;
            m_ExpireTime = Time.unscaledTime + maximumLifetime;
            m_HitCount = 0;
            m_CompletionRequested = false;
            m_HitTargets.Clear();
            if (NetworkManager.LocalClient != null && NetworkManager.LocalClient.PlayerObject != null)
                m_OwnerModifierHost = NetworkManager.LocalClient.PlayerObject.GetComponent<CombatModifierHost>();
        }

        public void ProcessUpdate()
        {
            if (!IsSpawned || !IsOwner || m_CompletionRequested) return;
            DetectTrustedHits();
            float maximumDistance = m_Cast.Value.TravelDistance > 0f
                ? m_Cast.Value.TravelDistance
                : defaultMaximumDistance;
            if ((transform.position - m_StartPosition).sqrMagnitude >= maximumDistance * maximumDistance ||
                Time.unscaledTime >= m_ExpireTime) RequestCompletion();
        }

        public void Cleanup()
        {
            m_HitTargets.Clear();
            m_CompletionRequested = true;
        }

        public ContactEventHandlerInfo GetContactEventHandlerInfo() => new ContactEventHandlerInfo
        {
            ProvideNonRigidBodyContactEvents = false,
            HasContactEventPriority = IsOwner
        };

        public Rigidbody GetRigidbody() => m_Rigidbody;
        public void ContactEvent(ulong eventId, Vector3 averageNormal, Rigidbody collidingBody,
            Vector3 contactPoint, bool hasCollisionStay = false,
            Vector3 averagedCollisionStayNormal = default) { }

        private void DetectTrustedHits()
        {
            AbilityCastNetworkMessage cast = m_Cast.Value;
            float radius = hitRadius * Mathf.Max(0.1f, cast.ProjectileSize);
            int overlapCount = Physics.OverlapSphereNonAlloc(transform.position, radius, m_HitBuffer,
                targetMask, QueryTriggerInteraction.Collide);

            for (int i = 0; i < overlapCount; i++)
            {
                Collider hitCollider = m_HitBuffer[i];
                if (hitCollider == null || !TryFindTarget(hitCollider, out MonoBehaviour target,
                        out ITrustedCombatHitTarget trustedTarget, out IHittable fallback)) continue;
                if (!m_HitTargets.Add(target)) continue;

                Vector3 direction = transform.forward.sqrMagnitude > 0.0001f
                    ? transform.forward.normalized
                    : Vector3.forward;
                if (trustedTarget != null)
                {
                    var request = new DamageRequest(
                        new GameplayEntityId(m_AttackerClientId.Value + 1UL), GameplayEntityId.None,
                        cast.AbilityId, cast.Sequence, cast.Damage, (DamageTags)cast.Tags);
                    int statusCount = Mathf.Min(4, cast.OnHitStatuses.Count);
                    var statuses = new StatusEffectSpec[statusCount];
                    for (int statusIndex = 0; statusIndex < statusCount; statusIndex++)
                        statuses[statusIndex] = cast.OnHitStatuses.Get(statusIndex).ToDomain();
                    var force = direction * cast.Knockback;
                    trustedTarget.SubmitTrustedHit(new TrustedCombatHit(request, (ElementId)cast.Element,
                        statuses, new Float3(force.x, force.y, force.z)));
                }
                else
                {
                    fallback.OnHit(new HitInfo
                    {
                        amount = cast.Damage,
                        hitPoint = hitCollider.ClosestPoint(transform.position),
                        hitNormal = -direction,
                        attackerId = m_AttackerClientId.Value,
                        impactForce = direction * cast.Knockback
                    });
                }

                NotifyTrustedOutcome(cast);
                m_HitCount++;
                if (m_HitCount >= Mathf.Max(1, cast.PierceCount))
                {
                    RequestCompletion();
                    return;
                }
            }
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

        private void NotifyTrustedOutcome(in AbilityCastNetworkMessage cast)
        {
            if (m_OwnerModifierHost == null) return;
            var request = new DamageRequest(new GameplayEntityId(m_AttackerClientId.Value + 1UL),
                GameplayEntityId.None, cast.AbilityId, cast.Sequence, cast.Damage, (DamageTags)cast.Tags);
            m_OwnerModifierHost.NotifyOutcome(new ResolvedDamage(request, cast.Damage,
                (DamageTags)cast.Tags, false));
        }

        private void RequestCompletion()
        {
            if (m_CompletionRequested) return;
            m_CompletionRequested = true;
            if (IsServer) OnEffectComplete?.Invoke(m_Projectile);
            else RequestDespawnRpc();
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestDespawnRpc()
        {
            if (NetworkObject != null && NetworkObject.IsSpawned) NetworkObject.Despawn();
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.8f, 0.1f, 0.15f, 0.75f);
            Gizmos.DrawWireSphere(transform.position, hitRadius);
        }
    }
}
