using System.Collections;
using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Economy;
using VampireHunt.Infrastructure.Netcode;
using VampireHunt.Infrastructure.Unity;

namespace VampireHunt.Infrastructure.Integration
{
    /// <summary>
    /// Modular-interaction adapter that asks the server to grant the configured item to the
    /// interacting player's inventory. A pickup is despawned only after the complete quantity
    /// has been accepted, so a full usable-item inventory leaves it available for a later retry.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(ModularInteractable))]
    public sealed class GrantItemInteractionEffect : NetworkBehaviour, IInteractionEffect
    {
        [Header("Item Grant")]
        [SerializeField] private ItemDefinitionAsset item;
        [SerializeField, Min(1)] private int quantity = 1;
        [SerializeField] private int priority = 100;

        [Header("Pickup Rules")]
        [SerializeField] private bool despawnOnSuccess = true;
        [SerializeField, Min(0.1f)] private float maxGrantDistance = 4f;

        private bool m_GrantCommitted;

        public ItemDefinitionAsset Item => item;
        public int Quantity => quantity;
        public int Priority => priority;
        public bool DespawnOnSuccess => despawnOnSuccess;
        public float MaxGrantDistance => maxGrantDistance;

        /// <summary>Overrides the granted stack size on the server instance before it is spawned.</summary>
        public void PrepareServerSpawn(int preparedQuantity)
        {
            if (IsSpawned)
            {
                Debug.LogWarning("[GrantItemInteractionEffect] Quantity must be prepared before spawn.", this);
                return;
            }

            quantity = Mathf.Max(1, preparedQuantity);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer) m_GrantCommitted = false;
        }

        public IEnumerator ApplyEffect(GameObject interactor, GameObject interactable)
        {
            if (!IsSpawned || item == null || quantity <= 0 || interactor == null)
                yield break;

            NetworkObject interactorNetworkObject = interactor.GetComponentInParent<NetworkObject>();
            if (interactorNetworkObject == null || !interactorNetworkObject.IsOwner)
                yield break;

            WwiseAudioBridge.PostEvent("Play_UI_ItemPickup", gameObject);
            RequestGrantItemRpc();
        }

        public void CancelEffect(GameObject interactor)
        {
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestGrantItemRpc(RpcParams rpcParams = default)
        {
            if (m_GrantCommitted || item == null || quantity <= 0 || NetworkManager == null)
                return;

            ulong senderClientId = rpcParams.Receive.SenderClientId;
            if (!NetworkManager.ConnectedClients.TryGetValue(senderClientId, out NetworkClient client) ||
                client.PlayerObject == null)
                return;

            GameObject player = client.PlayerObject.gameObject;
            if (player.GetComponent<CorePlayerManager>() == null ||
                (player.transform.position - transform.position).sqrMagnitude > maxGrantDistance * maxGrantDistance ||
                !player.TryGetComponent(out PlayerInventoryNetworkState inventory))
                return;

            bool granted = item.Kind switch
            {
                ItemKind.Usable => inventory.TryGrantUsable(item.ItemId, quantity),
                ItemKind.Accessory => inventory.TryGrantAccessory(item.ItemId, quantity),
                _ => false
            };

            if (!granted)
                return;

            // Commit before requesting the despawn so concurrent requests cannot duplicate the item.
            m_GrantCommitted = true;
            if (despawnOnSuccess)
                GetComponent<ModularInteractable>().RequestDespawn();
        }

        private void OnValidate()
        {
            quantity = Mathf.Max(1, quantity);
            maxGrantDistance = Mathf.Max(0.1f, maxGrantDistance);
        }
    }
}
