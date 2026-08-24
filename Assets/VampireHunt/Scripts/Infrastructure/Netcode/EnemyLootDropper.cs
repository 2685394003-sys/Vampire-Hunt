using Unity.Netcode;
using UnityEngine;
using VampireHunt.Infrastructure.Integration;
using VampireHunt.Infrastructure.Unity;

namespace VampireHunt.Infrastructure.Netcode
{
    /// <summary>Server-only adapter that turns one enemy loot roll into shared world pickups.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class EnemyLootDropper : NetworkBehaviour
    {
        [Header("Roll")]
        [SerializeField] private int runSeed = 1337;

        [Header("Spawn Placement")]
        [SerializeField, Min(0f)] private float scatterRadius = 0.75f;
        [SerializeField] private LayerMask groundMask = 1;
        [SerializeField, Min(0.1f)] private float groundProbeHeight = 4f;
        [SerializeField, Min(0f)] private float groundOffset = 0.1f;

        private bool m_DropsSpawned;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer) m_DropsSpawned = false;
        }

        public void SpawnDropsOnce(LootTableAsset lootTable, ulong enemyEntityId, Vector3 origin)
        {
            if (!IsSpawned || !IsServer || m_DropsSpawned || lootTable == null) return;
            m_DropsSpawned = true;

            LootSpawnRequest[] drops = lootTable.Roll(CreateSeed(runSeed, lootTable.StableId, enemyEntityId));
            int totalInstanceCount = CountSpawnedInstances(drops);
            int instanceIndex = 0;
            for (int i = 0; i < drops.Length; i++)
            {
                LootSpawnRequest drop = drops[i];
                NetworkObject prefab = drop.Pickup != null ? drop.Pickup.PickupPrefab : null;
                if (prefab == null) continue;

                if (drop.Pickup.SpawnMode == LootPickupSpawnMode.PrefabOnly)
                {
                    for (int quantityIndex = 0; quantityIndex < drop.Quantity; quantityIndex++)
                    {
                        SpawnPrefabInstance(prefab, origin, instanceIndex++, totalInstanceCount, null);
                    }
                }
                else
                {
                    SpawnPrefabInstance(prefab, origin, instanceIndex++, totalInstanceCount, drop.Quantity);
                }
            }
        }

        private static int CountSpawnedInstances(LootSpawnRequest[] drops)
        {
            int count = 0;
            for (int i = 0; i < drops.Length; i++)
            {
                LootSpawnRequest drop = drops[i];
                if (drop.Pickup == null || drop.Pickup.PickupPrefab == null) continue;
                count += drop.Pickup.SpawnMode == LootPickupSpawnMode.PrefabOnly
                    ? drop.Quantity
                    : 1;
            }
            return count;
        }

        private void SpawnPrefabInstance(
            NetworkObject prefab,
            Vector3 origin,
            int instanceIndex,
            int totalInstanceCount,
            int? preparedItemQuantity)
        {
            Vector3 spawnPosition = ResolveSpawnPosition(origin, instanceIndex, totalInstanceCount);
            NetworkObject instance = Instantiate(prefab, spawnPosition, Quaternion.identity);

            if (preparedItemQuantity.HasValue)
            {
                if (instance.TryGetComponent(out GrantItemInteractionEffect grantItem))
                {
                    grantItem.PrepareServerSpawn(preparedItemQuantity.Value);
                }
                else
                {
                    Debug.LogWarning(
                        $"[EnemyLootDropper] Pickup '{prefab.name}' uses PreparedItemGrant but has no GrantItemInteractionEffect.",
                        prefab);
                }
            }

            instance.Spawn(true);
        }

        private Vector3 ResolveSpawnPosition(Vector3 origin, int index, int count)
        {
            Vector3 candidate = origin;
            if (count > 1 && scatterRadius > 0f)
            {
                float angle = (Mathf.PI * 2f * index) / count;
                candidate += new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * scatterRadius;
            }

            Vector3 rayOrigin = candidate + Vector3.up * groundProbeHeight;
            if (Physics.Raycast(
                    rayOrigin,
                    Vector3.down,
                    out RaycastHit hit,
                    groundProbeHeight * 2f,
                    groundMask,
                    QueryTriggerInteraction.Ignore))
            {
                return hit.point + Vector3.up * groundOffset;
            }

            return candidate + Vector3.up * groundOffset;
        }

        private static int CreateSeed(int runSeed, string stableId, ulong enemyEntityId)
        {
            unchecked
            {
                uint hash = (uint)runSeed ^ 2166136261u;
                if (!string.IsNullOrEmpty(stableId))
                {
                    for (int i = 0; i < stableId.Length; i++)
                    {
                        hash ^= stableId[i];
                        hash *= 16777619u;
                    }
                }

                hash ^= (uint)enemyEntityId;
                hash *= 16777619u;
                hash ^= (uint)(enemyEntityId >> 32);
                hash *= 16777619u;
                return (int)hash;
            }
        }

        private void OnValidate()
        {
            scatterRadius = Mathf.Max(0f, scatterRadius);
            groundProbeHeight = Mathf.Max(0.1f, groundProbeHeight);
            groundOffset = Mathf.Max(0f, groundOffset);
        }
    }
}
