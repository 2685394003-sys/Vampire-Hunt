using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;

namespace VampireHunt.Infrastructure.Netcode
{
    /// <summary>Small server-owned projectile used by the Boss radial volley.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class BossProjectileNetworkActor : NetworkBehaviour, IBossProjectilePayloadReceiver
    {
        [SerializeField] private LayerMask playerLayers = 1 << 3;
        [Min(0.01f)] [SerializeField] private float hitRadius = 0.3f;
        [Min(0.1f)] [SerializeField] private float lifetime = 8f;

        private readonly Collider[] m_Hits = new Collider[12];
        private BossProjectileSpawnRequest m_Request;
        private Vector3 m_Direction;
        private float m_DespawnTime;
        private bool m_Configured;

        public void ConfigureBossProjectile(in BossProjectileSpawnRequest request)
        {
            m_Request = request;
            m_Direction = new Vector3(request.Direction.X, request.Direction.Y, request.Direction.Z).normalized;
            m_Configured = true;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer) m_DespawnTime = Time.unscaledTime + lifetime;
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer || !m_Configured) return;
            transform.position += m_Direction * (m_Request.Speed * Time.deltaTime);
            if (TryHitPlayer() || Time.unscaledTime >= m_DespawnTime)
                NetworkObject.Despawn();
        }

        private bool TryHitPlayer()
        {
            int count = Physics.OverlapSphereNonAlloc(transform.position, hitRadius, m_Hits,
                playerLayers, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                NetworkObject player = m_Hits[i]?.GetComponentInParent<NetworkObject>();
                if (player == null || !player.IsPlayerObject) continue;
                MonoBehaviour[] behaviours = player.GetComponents<MonoBehaviour>();
                for (int portIndex = 0; portIndex < behaviours.Length; portIndex++)
                {
                    if (!(behaviours[portIndex] is IDamageReceiver receiver)) continue;
                    var request = new DamageRequest(m_Request.Source,
                        new VampireHunt.SharedKernel.EntityId(player.OwnerClientId + 1UL),
                        m_Request.ProjectileId, m_Request.Sequence, m_Request.Damage,
                        m_Request.Tags | DamageTags.Projectile);
                    receiver.TryApplyDamage(request, out _);
                    return true;
                }
            }
            return false;
        }
    }
}
