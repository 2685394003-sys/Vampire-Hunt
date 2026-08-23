using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Effects;
using VampireHunt.Infrastructure.Integration;
using VampireHunt.Infrastructure.Unity;
using VampireHunt.Progression;
using GameplayEntityId = VampireHunt.SharedKernel.EntityId;

namespace VampireHunt.Infrastructure.Netcode
{
    public struct PactStackNetworkState : INetworkSerializable, IEquatable<PactStackNetworkState>
    {
        public uint PactId;
        public int Stacks;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref PactId);
            serializer.SerializeValue(ref Stacks);
        }

        public bool Equals(PactStackNetworkState other) => PactId == other.PactId && Stacks == other.Stacks;
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class PactNetworkState : NetworkBehaviour
    {
        [SerializeField] private PactCatalogAsset catalog;
        [SerializeField] private GameplayEffectHost effectHost;

        private readonly NetworkList<PactStackNetworkState> m_ReplicatedPacts =
            new NetworkList<PactStackNetworkState>();
        private PactCatalog m_DomainCatalog;
        private PactInventory m_ServerInventory;
        private ICombatEntityIdentity m_Identity;

        private void Awake()
        {
            if (effectHost == null) effectHost = GetComponent<GameplayEffectHost>();
            var behaviours = GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length && m_Identity == null; i++)
                if (behaviours[i] is ICombatEntityIdentity identity) m_Identity = identity;
        }

        public event Action InventoryChanged;
        public event Action<PactStackNetworkState, int> PactChanged;

        public int DistinctCount => m_ReplicatedPacts.Count;
        public int TotalStacks
        {
            get
            {
                int total = 0;
                for (int i = 0; i < m_ReplicatedPacts.Count; i++) total += m_ReplicatedPacts[i].Stacks;
                return total;
            }
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            m_DomainCatalog = catalog != null ? catalog.CreateCatalog() : new PactCatalog(null);
            if (IsServer)
            {
                m_ServerInventory = new PactInventory();
                for (int i = 0; i < m_ReplicatedPacts.Count; i++)
                    m_ServerInventory.Restore(m_ReplicatedPacts[i].PactId, m_ReplicatedPacts[i].Stacks);
            }
            m_ReplicatedPacts.OnListChanged += HandleListChanged;
            for (int i = 0; i < m_ReplicatedPacts.Count; i++) InstallOrUpdateEffect(m_ReplicatedPacts[i]);
            InventoryChanged?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            m_ReplicatedPacts.OnListChanged -= HandleListChanged;
            effectHost?.RemoveSourceKind(EffectSourceKind.Pact);
            m_ServerInventory = null;
            base.OnNetworkDespawn();
        }

        public bool TryAddOrStackServer(uint pactId, out int stacks)
        {
            stacks = 0;
            if (!IsServer || m_ServerInventory == null ||
                !m_DomainCatalog.TryGet(pactId, out PactDefinition definition) ||
                !m_ServerInventory.TryAddOrStack(definition, out PactStack stack)) return false;

            stacks = stack.Stacks;
            int existingIndex = FindIndex(pactId);
            var state = new PactStackNetworkState { PactId = pactId, Stacks = stacks };
            if (existingIndex < 0) m_ReplicatedPacts.Add(state);
            else m_ReplicatedPacts[existingIndex] = state;
            return true;
        }

        public int GetStacks(uint pactId)
        {
            int index = FindIndex(pactId);
            return index >= 0 ? m_ReplicatedPacts[index].Stacks : 0;
        }

        public PactInventory CreateInventorySnapshot()
        {
            var inventory = new PactInventory();
            for (int i = 0; i < m_ReplicatedPacts.Count; i++)
                inventory.Restore(m_ReplicatedPacts[i].PactId, m_ReplicatedPacts[i].Stacks);
            return inventory;
        }

        public void Capture(List<PactStackNetworkState> output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            output.Clear();
            for (int i = 0; i < m_ReplicatedPacts.Count; i++) output.Add(m_ReplicatedPacts[i]);
            output.Sort((left, right) => left.PactId.CompareTo(right.PactId));
        }

        private int FindIndex(uint pactId)
        {
            for (int i = 0; i < m_ReplicatedPacts.Count; i++)
            {
                if (m_ReplicatedPacts[i].PactId == pactId) return i;
            }
            return -1;
        }

        private void HandleListChanged(NetworkListEvent<PactStackNetworkState> change)
        {
            int previousStacks = change.Type == NetworkListEvent<PactStackNetworkState>.EventType.Value
                ? change.PreviousValue.Stacks
                : 0;
            switch (change.Type)
            {
                case NetworkListEvent<PactStackNetworkState>.EventType.Add:
                case NetworkListEvent<PactStackNetworkState>.EventType.Insert:
                case NetworkListEvent<PactStackNetworkState>.EventType.Value:
                    InstallOrUpdateEffect(change.Value);
                    break;
                case NetworkListEvent<PactStackNetworkState>.EventType.Remove:
                case NetworkListEvent<PactStackNetworkState>.EventType.RemoveAt:
                    RemoveEffect(change.Value.PactId);
                    break;
                case NetworkListEvent<PactStackNetworkState>.EventType.Full:
                    effectHost?.RemoveSourceKind(EffectSourceKind.Pact);
                    for (int i = 0; i < m_ReplicatedPacts.Count; i++) InstallOrUpdateEffect(m_ReplicatedPacts[i]);
                    break;
            }
            PactChanged?.Invoke(change.Value, previousStacks);
            InventoryChanged?.Invoke();
        }

        private void InstallOrUpdateEffect(in PactStackNetworkState pact)
        {
            if (effectHost == null || pact.PactId == 0 || pact.Stacks <= 0 ||
                m_DomainCatalog == null || !m_DomainCatalog.TryGet(pact.PactId, out PactDefinition definition)) return;
            GameplayEntityId entity = m_Identity?.CombatEntityId ?? GameplayEntityId.None;
            var key = new EffectSourceKey(EffectSourceKind.Pact, pact.PactId);
            var state = new EffectRuntimeState(
                key, entity, entity, pact.Stacks, 1f, 0d, double.PositiveInfinity);
            effectHost.SetSource(definition.RuntimeEffects, state);
        }

        private void RemoveEffect(uint pactId)
        {
            if (pactId == 0) return;
            var key = new EffectSourceKey(EffectSourceKind.Pact, pactId);
            effectHost?.RemoveSource(key);
        }
    }
}
