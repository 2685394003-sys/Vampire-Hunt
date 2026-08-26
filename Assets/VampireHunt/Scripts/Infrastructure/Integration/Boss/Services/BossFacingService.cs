using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;

namespace VampireHunt.Infrastructure.Integration
{
    /// <summary>
    /// Atomic Unity adapter for server-authored Boss facing. NetworkTransform on the same
    /// prefab remains responsible for replicating the resulting rotation to clients.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class BossFacingService : MonoBehaviour, IBossFacingService
    {
        private NetworkObject m_NetworkObject;

        public Float3 CurrentFacing
        {
            get
            {
                Vector3 forward = transform.forward;
                return new Float3(forward.x, 0f, forward.z).Normalized();
            }
        }

        private void Awake() => m_NetworkObject = GetComponent<NetworkObject>();

        public bool TrySetFacing(in Float3 forward)
        {
            if (m_NetworkObject == null) m_NetworkObject = GetComponent<NetworkObject>();
            NetworkManager manager = NetworkManager.Singleton;
            if (m_NetworkObject != null && m_NetworkObject.IsSpawned &&
                manager != null && manager.IsListening && !manager.IsServer) return false;

            Vector3 planar = new Vector3(forward.X, 0f, forward.Z);
            if (planar.sqrMagnitude <= .0001f) return false;
            transform.rotation = Quaternion.LookRotation(planar.normalized, Vector3.up);
            return true;
        }
    }
}
