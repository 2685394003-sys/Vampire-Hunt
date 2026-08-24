using Unity.Netcode;
using UnityEngine;

namespace VampireHunt.Infrastructure.Unity
{
    public enum LootPickupSpawnMode
    {
        PreparedItemGrant = 0,
        PrefabOnly = 1
    }

    [CreateAssetMenu(fileName = "LootPickup", menuName = "Vampire Hunt/Loot/Pickup Definition")]
    public sealed class LootPickupDefinitionAsset : ScriptableObject
    {
        [SerializeField] private string stableId = "pickup.item";
        [SerializeField] private NetworkObject pickupPrefab;
        [Tooltip("Prepared Item Grant passes quantity to GrantItemInteractionEffect. Prefab Only only spawns prefab instances and lets the prefab own all pickup and despawn behavior.")]
        [SerializeField] private LootPickupSpawnMode spawnMode;

        public string StableId => stableId;
        public NetworkObject PickupPrefab => pickupPrefab;
        public LootPickupSpawnMode SpawnMode => spawnMode;

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(stableId)) stableId = name;
        }
    }
}
