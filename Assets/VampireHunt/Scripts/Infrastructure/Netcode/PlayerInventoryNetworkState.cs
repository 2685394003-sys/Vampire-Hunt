using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Economy;
using VampireHunt.Infrastructure.Integration;
using VampireHunt.Infrastructure.Unity;
using Effects = VampireHunt.Effects;
using GameplayEntityId = VampireHunt.SharedKernel.EntityId;

namespace VampireHunt.Infrastructure.Netcode
{
    public struct UsableItemSlotNetworkState : INetworkSerializable, IEquatable<UsableItemSlotNetworkState>
    {
        public int SlotIndex;
        public uint ItemId;
        public int Quantity;

        public bool IsEmpty => ItemId == 0 || Quantity <= 0;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref SlotIndex);
            serializer.SerializeValue(ref ItemId);
            serializer.SerializeValue(ref Quantity);
        }

        public bool Equals(UsableItemSlotNetworkState other) =>
            SlotIndex == other.SlotIndex && ItemId == other.ItemId && Quantity == other.Quantity;
    }

    public struct AccessoryStackNetworkState : INetworkSerializable, IEquatable<AccessoryStackNetworkState>
    {
        public uint AccessoryId;
        public int Stacks;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref AccessoryId);
            serializer.SerializeValue(ref Stacks);
        }

        public bool Equals(AccessoryStackNetworkState other) =>
            AccessoryId == other.AccessoryId && Stacks == other.Stacks;
    }

    /// <summary>
    /// Player-owned inventory replica. The server writes the finite usable-item slots and the
    /// unlimited accessory collection; accessories are projected into the shared effect host.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PlayerInventoryNetworkState : NetworkBehaviour,
        IPlayerItemInventory,
        IUsableItemUseInventory
    {
        [SerializeField] private ItemCatalogAsset catalog;
        [SerializeField] private GameplayEffectHost effectHost;
        [SerializeField, Range(1, 32)] private int baseUsableSlotCapacity = 4;

        private readonly NetworkVariable<int> m_UsableSlotCapacity =
            new NetworkVariable<int>(0,
                NetworkVariableReadPermission.Owner,
                NetworkVariableWritePermission.Server);
        private readonly NetworkList<UsableItemSlotNetworkState> m_UsableSlots =
            new NetworkList<UsableItemSlotNetworkState>(default,
                NetworkVariableReadPermission.Owner,
                NetworkVariableWritePermission.Server);
        private readonly NetworkList<AccessoryStackNetworkState> m_Accessories =
            new NetworkList<AccessoryStackNetworkState>();

        private readonly List<UsableItemSlot> m_UsableSnapshot = new List<UsableItemSlot>();
        private ItemCatalog m_DomainCatalog;
        private UsableItemInventory m_ServerUsableItems;
        private AccessoryInventory m_ServerAccessories;
        private ICombatEntityIdentity m_Identity;

        public event Action UsableItemsChanged;
        public event Action AccessoriesChanged;
        public event Action<AccessoryStackNetworkState, int> AccessoryChanged;

        public int UsableSlotCapacity => m_UsableSlotCapacity.Value;
        public int AccessoryDistinctCount => m_Accessories.Count;

        private void Awake()
        {
            if (effectHost == null) effectHost = GetComponent<GameplayEffectHost>();
            var behaviours = GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length && m_Identity == null; i++)
                if (behaviours[i] is ICombatEntityIdentity identity) m_Identity = identity;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            m_DomainCatalog = catalog != null ? catalog.CreateCatalog() : new ItemCatalog(null);
            if (IsServer) RestoreServerState();

            m_UsableSlotCapacity.OnValueChanged += HandleUsableCapacityChanged;
            m_UsableSlots.OnListChanged += HandleUsableListChanged;
            m_Accessories.OnListChanged += HandleAccessoryListChanged;
            ReconcileAccessoryEffects();

            if (IsOwner) UsableItemsChanged?.Invoke();
            AccessoriesChanged?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            m_UsableSlotCapacity.OnValueChanged -= HandleUsableCapacityChanged;
            m_UsableSlots.OnListChanged -= HandleUsableListChanged;
            m_Accessories.OnListChanged -= HandleAccessoryListChanged;
            effectHost?.RemoveSourceKind(Effects.EffectSourceKind.Equipment);
            m_ServerUsableItems = null;
            m_ServerAccessories = null;
            base.OnNetworkDespawn();
        }

        public bool TryGrantUsable(uint itemId, int quantity)
        {
            if (!IsServer || m_ServerUsableItems == null || quantity <= 0 ||
                !m_DomainCatalog.TryGetUsable(itemId, out UsableItemDefinition definition) ||
                !m_ServerUsableItems.TryAdd(definition, quantity)) return false;
            SyncUsableSlotsServer();
            return true;
        }

        public bool CanGrantUsable(uint itemId, int quantity)
        {
            return IsServer && m_ServerUsableItems != null && quantity > 0 &&
                   m_DomainCatalog.TryGetUsable(itemId, out UsableItemDefinition definition) &&
                   m_ServerUsableItems.CanAdd(definition, quantity);
        }

        public bool TryConsumeUsable(int slotIndex, int quantity)
        {
            if (!IsServer || m_ServerUsableItems == null ||
                !m_ServerUsableItems.TryGetSlot(slotIndex, out UsableItemSlot slot) || slot.IsEmpty ||
                !m_DomainCatalog.TryGetUsable(slot.ItemId, out UsableItemDefinition definition) ||
                definition.ConsumptionPolicy != UseConsumptionPolicy.ConsumeOnSuccess ||
                !m_ServerUsableItems.TryRemoveAt(slotIndex, quantity, out _)) return false;
            SyncUsableSlotsServer();
            return true;
        }

        public bool TryGetUsableForUse(int slotIndex, out UsableItemDefinition definition)
        {
            definition = null;
            if (!IsServer || m_ServerUsableItems == null || m_DomainCatalog == null ||
                !m_ServerUsableItems.TryGetSlot(slotIndex, out UsableItemSlot slot) || slot.IsEmpty)
                return false;
            return m_DomainCatalog.TryGetUsable(slot.ItemId, out definition);
        }

        public bool TryGrantAccessory(uint accessoryId, int quantity)
        {
            if (!IsServer || m_ServerAccessories == null || quantity <= 0 ||
                !m_DomainCatalog.TryGetAccessory(accessoryId, out AccessoryDefinition definition) ||
                !m_ServerAccessories.TryAddOrStack(definition, quantity, out AccessoryStack stack))
                return false;
            SyncAccessoryServer(stack);
            return true;
        }

        public bool CanGrantAccessory(uint accessoryId, int quantity)
        {
            return IsServer && m_ServerAccessories != null && quantity > 0 &&
                   m_DomainCatalog.TryGetAccessory(accessoryId, out AccessoryDefinition definition) &&
                   m_ServerAccessories.CanAdd(definition, quantity);
        }

        public bool TryRemoveAccessory(uint accessoryId, int quantity)
        {
            if (!IsServer || m_ServerAccessories == null ||
                !m_ServerAccessories.TryRemove(accessoryId, quantity, out AccessoryStack stack))
                return false;
            SyncAccessoryServer(stack);
            return true;
        }

        public bool TryGetUsableSlot(int slotIndex, out UsableItemSlotNetworkState slot)
        {
            if (slotIndex < 0 || slotIndex >= m_UsableSlots.Count)
            {
                slot = default;
                return false;
            }
            slot = m_UsableSlots[slotIndex];
            return true;
        }

        public int GetAccessoryStacks(uint accessoryId)
        {
            int index = FindAccessoryIndex(accessoryId);
            return index >= 0 ? m_Accessories[index].Stacks : 0;
        }

        public void CaptureUsableItems(List<UsableItemSlotNetworkState> output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            output.Clear();
            for (int i = 0; i < m_UsableSlots.Count; i++) output.Add(m_UsableSlots[i]);
        }

        public void CaptureAccessories(List<AccessoryStackNetworkState> output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            output.Clear();
            for (int i = 0; i < m_Accessories.Count; i++) output.Add(m_Accessories[i]);
            output.Sort((left, right) => left.AccessoryId.CompareTo(right.AccessoryId));
        }

        private void RestoreServerState()
        {
            int capacity = Mathf.Clamp(baseUsableSlotCapacity, 1, 32);
            m_UsableSlotCapacity.Value = capacity;
            m_ServerUsableItems = new UsableItemInventory(capacity);
            for (int i = 0; i < m_UsableSlots.Count; i++)
            {
                UsableItemSlotNetworkState slot = m_UsableSlots[i];
                if (slot.IsEmpty || slot.SlotIndex < 0 || slot.SlotIndex >= capacity ||
                    !m_DomainCatalog.TryGetUsable(slot.ItemId, out UsableItemDefinition definition))
                    continue;
                m_ServerUsableItems.TryRestoreSlot(slot.SlotIndex, definition, slot.Quantity);
            }
            SyncUsableSlotsServer();

            m_ServerAccessories = new AccessoryInventory();
            for (int i = 0; i < m_Accessories.Count; i++)
            {
                AccessoryStackNetworkState state = m_Accessories[i];
                if (state.Stacks <= 0 ||
                    !m_DomainCatalog.TryGetAccessory(state.AccessoryId, out AccessoryDefinition definition))
                    continue;
                m_ServerAccessories.TryRestore(definition, state.Stacks);
            }
        }

        private void SyncUsableSlotsServer()
        {
            if (!IsServer || m_ServerUsableItems == null) return;
            m_ServerUsableItems.Capture(m_UsableSnapshot);
            if (m_UsableSlots.Count != m_UsableSnapshot.Count)
            {
                m_UsableSlots.Clear();
                for (int i = 0; i < m_UsableSnapshot.Count; i++)
                    m_UsableSlots.Add(ToNetworkState(m_UsableSnapshot[i]));
                return;
            }

            for (int i = 0; i < m_UsableSnapshot.Count; i++)
            {
                UsableItemSlotNetworkState next = ToNetworkState(m_UsableSnapshot[i]);
                if (!m_UsableSlots[i].Equals(next)) m_UsableSlots[i] = next;
            }
        }

        private void SyncAccessoryServer(in AccessoryStack stack)
        {
            if (!IsServer || stack.AccessoryId == 0) return;
            int index = FindAccessoryIndex(stack.AccessoryId);
            if (stack.Stacks <= 0)
            {
                if (index >= 0) m_Accessories.RemoveAt(index);
                return;
            }

            var state = new AccessoryStackNetworkState
            {
                AccessoryId = stack.AccessoryId,
                Stacks = stack.Stacks
            };
            if (index < 0) m_Accessories.Add(state);
            else m_Accessories[index] = state;
        }

        private static UsableItemSlotNetworkState ToNetworkState(in UsableItemSlot slot) =>
            new UsableItemSlotNetworkState
            {
                SlotIndex = slot.SlotIndex,
                ItemId = slot.ItemId,
                Quantity = slot.Quantity
            };

        private int FindAccessoryIndex(uint accessoryId)
        {
            for (int i = 0; i < m_Accessories.Count; i++)
                if (m_Accessories[i].AccessoryId == accessoryId) return i;
            return -1;
        }

        private void HandleUsableCapacityChanged(int previous, int current)
        {
            if (IsOwner) UsableItemsChanged?.Invoke();
        }

        private void HandleUsableListChanged(NetworkListEvent<UsableItemSlotNetworkState> change)
        {
            if (IsOwner) UsableItemsChanged?.Invoke();
        }

        private void HandleAccessoryListChanged(NetworkListEvent<AccessoryStackNetworkState> change)
        {
            int previousStacks = change.Type == NetworkListEvent<AccessoryStackNetworkState>.EventType.Value
                ? change.PreviousValue.Stacks
                : 0;
            switch (change.Type)
            {
                case NetworkListEvent<AccessoryStackNetworkState>.EventType.Add:
                case NetworkListEvent<AccessoryStackNetworkState>.EventType.Insert:
                case NetworkListEvent<AccessoryStackNetworkState>.EventType.Value:
                    InstallOrUpdateAccessory(change.Value);
                    break;
                case NetworkListEvent<AccessoryStackNetworkState>.EventType.Remove:
                case NetworkListEvent<AccessoryStackNetworkState>.EventType.RemoveAt:
                    RemoveAccessoryEffect(change.Value.AccessoryId);
                    break;
                case NetworkListEvent<AccessoryStackNetworkState>.EventType.Full:
                case NetworkListEvent<AccessoryStackNetworkState>.EventType.Clear:
                    ReconcileAccessoryEffects();
                    break;
            }
            AccessoryChanged?.Invoke(change.Value, previousStacks);
            AccessoriesChanged?.Invoke();
        }

        private void ReconcileAccessoryEffects()
        {
            effectHost?.RemoveSourceKind(Effects.EffectSourceKind.Equipment);
            for (int i = 0; i < m_Accessories.Count; i++) InstallOrUpdateAccessory(m_Accessories[i]);
        }

        private void InstallOrUpdateAccessory(in AccessoryStackNetworkState accessory)
        {
            if (effectHost == null || accessory.AccessoryId == 0 || accessory.Stacks <= 0 ||
                m_DomainCatalog == null ||
                !m_DomainCatalog.TryGetAccessory(accessory.AccessoryId, out AccessoryDefinition definition))
                return;
            GameplayEntityId entity = m_Identity?.CombatEntityId ?? GameplayEntityId.None;
            var key = new Effects.EffectSourceKey(Effects.EffectSourceKind.Equipment, accessory.AccessoryId);
            var state = new Effects.EffectRuntimeState(
                key,
                entity,
                entity,
                accessory.Stacks,
                1f,
                0d,
                double.PositiveInfinity);
            effectHost.SetSource(definition.RuntimeEffects, state);
        }

        private void RemoveAccessoryEffect(uint accessoryId)
        {
            if (accessoryId == 0) return;
            var key = new Effects.EffectSourceKey(Effects.EffectSourceKind.Equipment, accessoryId);
            effectHost?.RemoveSource(key);
        }
    }
}
