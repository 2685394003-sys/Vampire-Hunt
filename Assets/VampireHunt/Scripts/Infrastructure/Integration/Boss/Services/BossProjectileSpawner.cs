using System;
using Blocks.Gameplay.Shooter;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;

namespace VampireHunt.Infrastructure.Integration
{
    [DisallowMultipleComponent]
    public sealed class BossProjectileSpawner : MonoBehaviour, IBossProjectileSpawner
    {
        [Serializable]
        private struct ProjectileEntry
        {
            [Min(1)] public uint projectileId;
            public NetworkObject prefab;
            public LayerMask hitMask;
        }

        [Tooltip("ProjectileId to network-prefab mapping. Prefabs must also be registered with NetworkManager.")]
        [SerializeField] private ProjectileEntry[] projectiles = Array.Empty<ProjectileEntry>();

        public bool TrySpawn(in BossProjectileSpawnRequest request, out ulong networkObjectId)
        {
            networkObjectId = 0;
            if (request.ProjectileId == 0 || request.Direction.SqrMagnitude <= 0.0001f ||
                !BossNetworkPlayerResolver.TryGetServerManager(out NetworkManager manager) ||
                !TryFindEntry(request.ProjectileId, out ProjectileEntry entry) || entry.prefab == null) return false;

            Vector3 origin = ToVector3(request.Origin);
            Vector3 direction = ToVector3(request.Direction).normalized;
            NetworkObject instance = Instantiate(
                entry.prefab,
                origin,
                Quaternion.LookRotation(direction, Vector3.up));
            instance.transform.localScale *= request.Scale;

            MonoBehaviour[] behaviours = instance.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IBossProjectilePayloadReceiver receiver)
                    receiver.ConfigureBossProjectile(request);
            }

            if (instance.TryGetComponent<StraightLineMovement>(out var movement))
                movement.SetVelocityRate(request.Speed);

            if (instance.TryGetComponent<ModularProjectile>(out var projectile))
            {
                var context = new ShootingContext
                {
                    owner = gameObject,
                    origin = origin,
                    direction = direction,
                    damage = request.Damage,
                    ownerClientId = NetworkManager.ServerClientId,
                    hitMask = entry.hitMask
                };
                projectile.LaunchWithContext(origin, direction, Vector3.zero, null, context, gameObject);
            }

            instance.Spawn();
            networkObjectId = instance.NetworkObjectId;
            return true;
        }

        private bool TryFindEntry(uint projectileId, out ProjectileEntry entry)
        {
            if (projectiles != null)
            {
                for (int i = 0; i < projectiles.Length; i++)
                {
                    if (projectiles[i].projectileId != projectileId) continue;
                    entry = projectiles[i];
                    return true;
                }
            }

            entry = default;
            return false;
        }

        private void OnValidate()
        {
            if (projectiles == null) projectiles = Array.Empty<ProjectileEntry>();
        }

        private static Vector3 ToVector3(in Float3 value) => new Vector3(value.X, value.Y, value.Z);
    }
}
