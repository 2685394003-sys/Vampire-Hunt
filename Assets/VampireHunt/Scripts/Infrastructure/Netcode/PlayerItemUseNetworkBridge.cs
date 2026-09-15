using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Economy;
using VampireHunt.Infrastructure.Unity;
using VampireHunt.Infrastructure.Unity.Items;
using GameplayEntityId = VampireHunt.SharedKernel.EntityId;

namespace VampireHunt.Infrastructure.Netcode
{
    /// <summary>
    /// Owner input/UI adapter and server composition root for usable items. Item rules live in
    /// ConsumableService; asset effect executors are invoked only by the server.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(PlayerInventoryNetworkState))]
    public sealed class PlayerItemUseNetworkBridge : NetworkBehaviour,
        IPlayerAddon,
        IUsableItemEffectExecutor
    {
        [Header("Input and Presentation")]
        [SerializeField] private ItemSlotUseEvent onUseItemSlotRequested;
        [SerializeField] private ItemUsePresentationEvent onItemUseResolved;

        [Header("Runtime Dependencies")]
        [SerializeField] private ItemCatalogAsset catalog;
        [SerializeField] private PlayerInventoryNetworkState inventory;
        [SerializeField] private CoreStatsHandler coreStats;
        [SerializeField] private CombatStatusHost statusHost;

        private CorePlayerManager m_PlayerManager;
        private ICombatEntityIdentity m_Identity;
        private ConsumableService m_Service;
        private bool m_UseEnabled = true;

        private void Awake() => CacheReferences();

        public void Initialize(CorePlayerManager playerManager)
        {
            m_PlayerManager = playerManager;
            CacheReferences();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            CacheReferences();
            if (IsServer) m_Service = new ConsumableService(inventory, this);
        }

        public override void OnNetworkDespawn()
        {
            m_Service?.ClearCooldowns();
            m_Service = null;
            base.OnNetworkDespawn();
        }

        public void OnPlayerSpawn()
        {
            if (m_PlayerManager == null || !m_PlayerManager.IsOwner) return;
            if (onUseItemSlotRequested == null)
            {
                Debug.LogWarning("[PlayerItemUseNetworkBridge] Item-use input event is not assigned.", this);
                return;
            }
            onUseItemSlotRequested.RegisterListener(HandleUseItemSlotRequested);
        }

        public void OnPlayerDespawn()
        {
            if (m_PlayerManager != null && m_PlayerManager.IsOwner && onUseItemSlotRequested != null)
                onUseItemSlotRequested.UnregisterListener(HandleUseItemSlotRequested);
        }

        public void OnLifeStateChanged(PlayerLifeState previousState, PlayerLifeState newState)
        {
            m_UseEnabled = newState != PlayerLifeState.Eliminated;
        }

        /// <summary>Owner-side entry point for UI buttons and input events.</summary>
        public bool RequestUseSlot(int slotIndex)
        {
            if (!IsSpawned || !IsOwner || slotIndex < 0 || inventory == null ||
                slotIndex >= inventory.UsableSlotCapacity) return false;
            RequestUseItemRpc(slotIndex);
            return true;
        }

        private void HandleUseItemSlotRequested(int slotIndex) => RequestUseSlot(slotIndex);

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestUseItemRpc(int slotIndex)
        {
            ItemUseResult result;
            if (!m_UseEnabled || statusHost != null && statusHost.IsActionBlocked)
            {
                result = ItemUseResult.Failed(slotIndex, 0, ItemUseFailureReason.ActionBlocked);
            }
            else if (m_Service == null)
            {
                result = ItemUseResult.Failed(slotIndex, 0, ItemUseFailureReason.SlotUnavailable);
            }
            else
            {
                result = m_Service.TryUse(slotIndex, NetworkManager.ServerTime.Time);
            }

            ResolveItemUseRpc(
                result.SlotIndex,
                result.ItemId,
                result.Success,
                (byte)result.FailureReason,
                result.NextReadyTime);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void ResolveItemUseRpc(
            int slotIndex,
            uint itemId,
            bool success,
            byte failureReason,
            double nextReadyTime)
        {
            onItemUseResolved?.Raise(new ItemUsePresentationPayload
            {
                slotIndex = slotIndex,
                itemId = itemId,
                success = success,
                failureReason = (ItemUseFailureReason)failureReason,
                nextReadyServerTime = nextReadyTime
            });
        }

        public bool HasConfiguredEffects(uint itemId)
        {
            if (!TryGetEffects(itemId, out UsableItemEffectAsset[] effects)) return false;
            for (int i = 0; i < effects.Length; i++) if (effects[i] != null) return true;
            return false;
        }

        public bool CanApplyAny(uint itemId)
        {
            if (!TryGetEffects(itemId, out UsableItemEffectAsset[] effects)) return false;
            UsableItemEffectContext context = CreateEffectContext();
            for (int i = 0; i < effects.Length; i++)
                if (effects[i] != null && effects[i].CanApply(context)) return true;
            return false;
        }

        public bool TryApplyConfiguredEffects(uint itemId)
        {
            if (!IsServer || !TryGetEffects(itemId, out UsableItemEffectAsset[] effects)) return false;
            UsableItemEffectContext context = CreateEffectContext();
            bool appliedAny = false;
            for (int i = 0; i < effects.Length; i++)
            {
                UsableItemEffectAsset effect = effects[i];
                if (effect == null || !effect.CanApply(context)) continue;
                try
                {
                    appliedAny |= effect.TryApply(context);
                }
                catch (System.Exception exception)
                {
                    Debug.LogException(exception, effect);
                }
            }
            return appliedAny;
        }

        private bool TryGetEffects(uint itemId, out UsableItemEffectAsset[] effects)
        {
            effects = null;
            if (catalog == null || !catalog.TryGetUsableAsset(
                    itemId, out UsableItemDefinitionAsset definition)) return false;
            effects = definition.UseEffects;
            return effects != null;
        }

        private UsableItemEffectContext CreateEffectContext()
        {
            GameplayEntityId entity = m_Identity?.CombatEntityId ?? GameplayEntityId.None;
            return new UsableItemEffectContext(
                gameObject,
                OwnerClientId,
                entity,
                coreStats,
                statusHost);
        }

        private void CacheReferences()
        {
            if (inventory == null) inventory = GetComponent<PlayerInventoryNetworkState>();
            if (coreStats == null) coreStats = m_PlayerManager != null
                ? m_PlayerManager.CoreStats
                : GetComponent<CoreStatsHandler>();
            if (statusHost == null) statusHost = GetComponent<CombatStatusHost>();

            if (m_Identity == null)
            {
                var behaviours = GetComponents<MonoBehaviour>();
                for (int i = 0; i < behaviours.Length && m_Identity == null; i++)
                    if (behaviours[i] is ICombatEntityIdentity identity) m_Identity = identity;
            }
        }
    }
}
