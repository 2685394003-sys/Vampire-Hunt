using System;
using Blocks.Gameplay.Shooter;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Integration;

namespace VampireHunt.Infrastructure.Netcode
{
    /// <summary>
    /// Server-authoritative impact adapter for Shooter's ModularProjectile chassis.
    /// Movement and network presentation remain in Shooter; damage is resolved by Vampire Hunt combat ports.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ModularProjectile))]
    public sealed class ServerCombatProjectileEffect : NetworkBehaviour, IProjectileEffect,
        IServerCombatProjectilePayloadReceiver
    {
        [Min(0.01f)] [SerializeField] private float hitRadius = 0.25f;
        [Min(0.1f)] [SerializeField] private float lifetime = 5f;
        [SerializeField] private LayerMask impactMask = (1 << 0) | (1 << 3) | (1 << 6);

        private readonly RaycastHit[] m_SweepHits = new RaycastHit[16];
        private readonly Collider[] m_OverlapHits = new Collider[16];
        private ModularProjectile m_Projectile;
        private Rigidbody m_Rigidbody;
        private ServerCombatProjectilePayload m_Payload;
        private Vector3 m_PreviousPosition;
        private float m_ExpireTime;
        private bool m_Configured;
        private bool m_CompletionRequested;

        public bool IsDeferredDespawnEnabled => false;
        public int DeferredDespawnTicks => 0;
        public event Action<ModularProjectile> OnEffectComplete;

        public void ConfigureServer(in ServerCombatProjectilePayload payload)
        {
            m_Payload = payload;
            m_Configured = true;
        }

        public void Initialize(ModularProjectile projectile)
        {
            m_Projectile = projectile;
            m_Rigidbody = GetComponent<Rigidbody>();
        }

        public void Setup(GameObject owner, IWeapon sourceWeapon, ShootingContext context) { }

        public void OnLaunch()
        {
            m_PreviousPosition = transform.position;
            m_ExpireTime = Time.unscaledTime + lifetime;
            m_CompletionRequested = false;
            if (!m_Configured && IsServer)
                Debug.LogError("[ServerCombatProjectileEffect] Projectile spawned without a combat payload.", this);
        }

        public void ProcessUpdate()
        {
            if (!IsSpawned || !IsServer || m_CompletionRequested) return;
            if (!m_Configured || Time.unscaledTime >= m_ExpireTime)
            {
                RequestCompletion();
                return;
            }

            Vector3 currentPosition = transform.position;
            Vector3 delta = currentPosition - m_PreviousPosition;
            if (TryResolveSweep(m_PreviousPosition, delta) || TryResolveOverlap(currentPosition))
            {
                RequestCompletion();
                return;
            }
            m_PreviousPosition = currentPosition;
        }

        public void Cleanup()
        {
            m_Configured = false;
            m_CompletionRequested = false;
            m_Payload = default;
            m_PreviousPosition = Vector3.zero;
            m_ExpireTime = 0f;
            if (m_Rigidbody != null)
            {
                m_Rigidbody.linearVelocity = Vector3.zero;
                m_Rigidbody.angularVelocity = Vector3.zero;
            }
        }

        public ContactEventHandlerInfo GetContactEventHandlerInfo() => new ContactEventHandlerInfo
        {
            ProvideNonRigidBodyContactEvents = false,
            HasContactEventPriority = IsServer
        };

        public Rigidbody GetRigidbody() => m_Rigidbody;

        public void ContactEvent(
            ulong eventId,
            Vector3 averageNormal,
            Rigidbody collidingBody,
            Vector3 contactPoint,
            bool hasCollisionStay = false,
            Vector3 averagedCollisionStayNormal = default) { }

        private bool TryResolveSweep(Vector3 origin, Vector3 delta)
        {
            float distance = delta.magnitude;
            if (distance <= 0.0001f) return false;
            Vector3 direction = delta / distance;
            int count = Physics.SphereCastNonAlloc(
                origin,
                hitRadius,
                direction,
                m_SweepHits,
                distance,
                impactMask,
                QueryTriggerInteraction.Collide);
            if (count <= 0) return false;

            int nearestIndex = 0;
            float nearestDistance = m_SweepHits[0].distance;
            for (int i = 1; i < count; i++)
            {
                if (m_SweepHits[i].distance >= nearestDistance) continue;
                nearestDistance = m_SweepHits[i].distance;
                nearestIndex = i;
            }
            return ResolveImpact(m_SweepHits[nearestIndex].collider, direction);
        }

        private bool TryResolveOverlap(Vector3 position)
        {
            int count = Physics.OverlapSphereNonAlloc(
                position,
                hitRadius,
                m_OverlapHits,
                impactMask,
                QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                Collider candidate = m_OverlapHits[i];
                if (candidate == null) continue;
                Vector3 direction = transform.forward.sqrMagnitude > 0.0001f
                    ? transform.forward.normalized
                    : Vector3.forward;
                if (ResolveImpact(candidate, direction)) return true;
            }
            return false;
        }

        private bool ResolveImpact(Collider hitCollider, Vector3 direction)
        {
            if (hitCollider == null) return false;
            NetworkObject target = hitCollider.GetComponentInParent<NetworkObject>();
            if (target == null || !target.IsPlayerObject) return true;

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
            if (damageReceiver == null) return true;

            var targetId = identity != null
                ? identity.CombatEntityId
                : new VampireHunt.SharedKernel.EntityId(target.OwnerClientId + 1UL);
            var request = new DamageRequest(
                m_Payload.Source,
                targetId,
                m_Payload.AttackId,
                m_Payload.Sequence,
                m_Payload.Damage,
                m_Payload.Tags | DamageTags.Projectile);
            if (damageReceiver.TryApplyDamage(request, out ResolvedDamage result) &&
                !result.IsCancelled && result.Amount > 0f &&
                impulseTarget != null && m_Payload.Knockback > 0f)
            {
                Vector3 impulse = direction.normalized * m_Payload.Knockback;
                impulseTarget.TryApplyImpulse(new Float3(impulse.x, impulse.y, impulse.z));
            }
            return true;
        }

        private void RequestCompletion()
        {
            if (m_CompletionRequested) return;
            m_CompletionRequested = true;
            OnEffectComplete?.Invoke(m_Projectile);
        }

        private void OnValidate()
        {
            hitRadius = Mathf.Max(0.01f, hitRadius);
            lifetime = Mathf.Max(0.1f, lifetime);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.65f, 0.1f, 0.9f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, hitRadius);
        }
    }
}
