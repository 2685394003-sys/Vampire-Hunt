using Unity.Netcode;
using UnityEngine;

namespace VampireHunt.Infrastructure.Netcode
{
    /// <summary>Server-authoritative expiry for shared world pickups.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkPickupLifetime : NetworkBehaviour
    {
        [SerializeField, Min(0f)] private float lifetimeSeconds = 45f;

        private float m_DespawnTime = float.PositiveInfinity;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            m_DespawnTime = IsServer && lifetimeSeconds > 0f
                ? Time.unscaledTime + lifetimeSeconds
                : float.PositiveInfinity;
        }

        private void Update()
        {
            if (!IsServer || !IsSpawned || Time.unscaledTime < m_DespawnTime) return;
            NetworkObject.Despawn(true);
        }

        private void OnValidate()
        {
            lifetimeSeconds = Mathf.Max(0f, lifetimeSeconds);
        }
    }
}
