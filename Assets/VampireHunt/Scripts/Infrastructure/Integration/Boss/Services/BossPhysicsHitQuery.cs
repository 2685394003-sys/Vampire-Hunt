using System;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;

namespace VampireHunt.Infrastructure.Integration
{
    [DisallowMultipleComponent]
    public sealed class BossPhysicsHitQuery : MonoBehaviour, IBossHitQuery
    {
        [Tooltip("Only colliders on these layers participate in Boss ability hit tests.")]
        [SerializeField] private LayerMask playerLayers = 1 << 3;
        [Min(8)] [SerializeField] private int colliderCapacity = 64;
        [SerializeField] private QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Collide;

        private Collider[] m_Colliders;

        private void Awake()
        {
            EnsureBuffer();
        }

        public int Query(in BossHitQueryRequest request, BossPlayerTarget[] results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            if (results.Length == 0 || !BossNetworkPlayerResolver.TryGetServerManager(out _)) return 0;
            EnsureBuffer();

            Vector3 center = ToVector3(request.Center);
            int colliderCount;
            switch (request.Shape)
            {
                case BossHitShape.Sphere:
                    colliderCount = Physics.OverlapSphereNonAlloc(
                        center,
                        request.Radius,
                        m_Colliders,
                        playerLayers,
                        triggerInteraction);
                    break;

                case BossHitShape.Box:
                    Vector3 forward = ToVector3(request.Forward);
                    Quaternion rotation = forward.sqrMagnitude > 0.0001f
                        ? Quaternion.LookRotation(forward, Vector3.up)
                        : Quaternion.identity;
                    colliderCount = Physics.OverlapBoxNonAlloc(
                        center,
                        Abs(ToVector3(request.HalfExtents)),
                        m_Colliders,
                        rotation,
                        playerLayers,
                        triggerInteraction);
                    break;

                default:
                    return 0;
            }

            int resultCount = 0;
            for (int i = 0; i < colliderCount && resultCount < results.Length; i++)
            {
                Collider hit = m_Colliders[i];
                if (hit == null) continue;
                NetworkObject player = hit.GetComponentInParent<NetworkObject>();
                if (player == null || !player.IsPlayerObject ||
                    !BossNetworkPlayerResolver.TryCreateTarget(player, out var target) ||
                    Contains(results, resultCount, target.EntityId.Value)) continue;
                results[resultCount++] = target;
            }

            return resultCount;
        }

        private void EnsureBuffer()
        {
            colliderCapacity = Mathf.Max(8, colliderCapacity);
            if (m_Colliders == null || m_Colliders.Length != colliderCapacity)
                m_Colliders = new Collider[colliderCapacity];
        }

        private void OnValidate()
        {
            colliderCapacity = Mathf.Max(8, colliderCapacity);
        }

        private static bool Contains(BossPlayerTarget[] results, int count, ulong entityId)
        {
            for (int i = 0; i < count; i++)
                if (results[i].EntityId.Value == entityId) return true;
            return false;
        }

        private static Vector3 ToVector3(in Float3 value) => new Vector3(value.X, value.Y, value.Z);
        private static Vector3 Abs(Vector3 value) =>
            new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
    }
}
