using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Infrastructure.Unity;
using VampireHunt.Progression;

namespace VampireHunt.Infrastructure.Netcode
{
    public struct EnemyAffixStackNetworkState : INetworkSerializable, IEquatable<EnemyAffixStackNetworkState>
    {
        public uint AffixId;
        public int Stacks;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref AffixId);
            serializer.SerializeValue(ref Stacks);
        }

        public bool Equals(EnemyAffixStackNetworkState other) =>
            AffixId == other.AffixId && Stacks == other.Stacks;
    }

    /// <summary>
    /// Scene-owned, server-authoritative affix state for one run. It broadcasts a compact read model;
    /// enemy instances only consume a snapshot at spawn time.
    /// </summary>
    [DefaultExecutionOrder(-150)]
    [DisallowMultipleComponent]
    public sealed class EnemyAffixRunState : MonoBehaviour
    {
        private const string SnapshotMessageName = "VampireHunt.EnemyAffixes.v1";
        private const int SnapshotCapacity = 1024;

        [SerializeField] private EnemyAffixCatalogAsset catalog;

        private readonly List<EnemyAffixStackNetworkState> m_LocalStacks =
            new List<EnemyAffixStackNetworkState>();
        private readonly List<EnemyAffixStack> m_CapturedStacks = new List<EnemyAffixStack>();
        private NetworkManager m_NetworkManager;
        private EnemyAffixCatalog m_DomainCatalog;
        private EnemyAffixSet m_ServerSet;
        private bool m_HandlerRegistered;
        private uint m_LocalRevision;

        public event Action AffixesChanged;
        public EnemyAffixCatalogAsset CatalogAsset => catalog;
        public bool IsServerAuthority => m_NetworkManager != null && m_NetworkManager.IsServer;

        private void Awake()
        {
            m_DomainCatalog = catalog != null ? catalog.CreateCatalog() : new EnemyAffixCatalog(null);
        }

        private void OnEnable() => TryBindNetworkManager();
        private void Start() => TryBindNetworkManager();

        private void Update()
        {
            if (m_NetworkManager == null) TryBindNetworkManager();
            else TryRegisterHandler();
        }

        private void OnDisable() => UnbindNetworkManager();

        public int GetStacks(uint affixId)
        {
            for (int i = 0; i < m_LocalStacks.Count; i++)
                if (m_LocalStacks[i].AffixId == affixId) return m_LocalStacks[i].Stacks;
            return 0;
        }

        public void Capture(List<EnemyAffixStackNetworkState> output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            output.Clear();
            output.AddRange(m_LocalStacks);
        }

        public EnemyAffixSet CreateSetSnapshot()
        {
            var result = new EnemyAffixSet();
            for (int i = 0; i < m_LocalStacks.Count; i++)
                result.Restore(m_LocalStacks[i].AffixId, m_LocalStacks[i].Stacks);
            return result;
        }

        public EnemyAffixSpawnSnapshot CaptureSpawnSnapshot()
        {
            if (!IsServerAuthority || m_ServerSet == null || m_DomainCatalog == null)
                return EnemyAffixSpawnSnapshot.Empty;

            m_ServerSet.Capture(m_CapturedStacks);
            var entries = new List<EnemyAffixSpawnEntry>(m_CapturedStacks.Count);
            for (int i = 0; i < m_CapturedStacks.Count; i++)
            {
                EnemyAffixStack stack = m_CapturedStacks[i];
                if (!m_DomainCatalog.TryGet(stack.AffixId, out EnemyAffixDefinition definition)) continue;
                entries.Add(new EnemyAffixSpawnEntry(definition, stack.Stacks));
            }
            return new EnemyAffixSpawnSnapshot(entries.ToArray(), m_ServerSet.Revision);
        }

        public bool TryAddOrStackServer(uint affixId, out int stacks)
        {
            stacks = 0;
            if (!IsServerAuthority || m_ServerSet == null || m_DomainCatalog == null ||
                !m_DomainCatalog.TryGet(affixId, out EnemyAffixDefinition definition) ||
                !m_ServerSet.TryAddOrStack(definition, out EnemyAffixStack result)) return false;
            stacks = result.Stacks;
            PublishSnapshot();
            return true;
        }

        public bool TryRemoveOneServer(uint affixId)
        {
            if (!IsServerAuthority || m_ServerSet == null || !m_ServerSet.TryRemoveOne(affixId, out _))
                return false;
            PublishSnapshot();
            return true;
        }

        private void TryBindNetworkManager()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || manager == m_NetworkManager) return;
            UnbindNetworkManager();
            m_NetworkManager = manager;
            m_NetworkManager.OnServerStarted += HandleServerStarted;
            m_NetworkManager.OnClientConnectedCallback += HandleClientConnected;
            m_NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            TryRegisterHandler();
            if (m_NetworkManager.IsListening && m_NetworkManager.IsServer) HandleServerStarted();
        }

        private void UnbindNetworkManager()
        {
            if (m_NetworkManager == null) return;
            if (m_HandlerRegistered && m_NetworkManager.CustomMessagingManager != null)
                m_NetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(SnapshotMessageName);
            m_NetworkManager.OnServerStarted -= HandleServerStarted;
            m_NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
            m_NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            m_HandlerRegistered = false;
            m_NetworkManager = null;
        }

        private void TryRegisterHandler()
        {
            if (m_HandlerRegistered || m_NetworkManager?.CustomMessagingManager == null) return;
            m_NetworkManager.CustomMessagingManager.RegisterNamedMessageHandler(
                SnapshotMessageName,
                HandleSnapshotMessage);
            m_HandlerRegistered = true;
        }

        private void HandleServerStarted()
        {
            m_ServerSet = new EnemyAffixSet();
            ApplySnapshot(Array.Empty<EnemyAffixStackNetworkState>(), 0, true);
            PublishSnapshot();
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (IsServerAuthority) PublishSnapshot(clientId);
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            if (m_NetworkManager == null || m_NetworkManager.IsServer ||
                clientId != m_NetworkManager.LocalClientId) return;
            ApplySnapshot(Array.Empty<EnemyAffixStackNetworkState>(), 0, true);
        }

        private void PublishSnapshot(ulong? targetClientId = null)
        {
            if (!IsServerAuthority || m_ServerSet == null ||
                m_NetworkManager?.CustomMessagingManager == null) return;

            m_ServerSet.Capture(m_CapturedStacks);
            var states = new EnemyAffixStackNetworkState[m_CapturedStacks.Count];
            for (int i = 0; i < states.Length; i++)
            {
                states[i] = new EnemyAffixStackNetworkState
                {
                    AffixId = m_CapturedStacks[i].AffixId,
                    Stacks = m_CapturedStacks[i].Stacks
                };
            }
            ApplySnapshot(states, m_ServerSet.Revision);

            var writer = new FastBufferWriter(SnapshotCapacity, Allocator.Temp);
            try
            {
                writer.WriteValueSafe(m_ServerSet.Revision);
                ushort count = (ushort)Math.Min(ushort.MaxValue, states.Length);
                writer.WriteValueSafe(count);
                for (int i = 0; i < count; i++)
                {
                    writer.WriteValueSafe(states[i].AffixId);
                    writer.WriteValueSafe(states[i].Stacks);
                }

                if (targetClientId.HasValue)
                {
                    m_NetworkManager.CustomMessagingManager.SendNamedMessage(
                        SnapshotMessageName,
                        targetClientId.Value,
                        writer,
                        NetworkDelivery.ReliableSequenced);
                }
                else
                {
                    m_NetworkManager.CustomMessagingManager.SendNamedMessageToAll(
                        SnapshotMessageName,
                        writer,
                        NetworkDelivery.ReliableSequenced);
                }
            }
            finally
            {
                writer.Dispose();
            }
        }

        private void HandleSnapshotMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (m_NetworkManager == null || m_NetworkManager.IsServer ||
                senderClientId != NetworkManager.ServerClientId) return;
            reader.ReadValueSafe(out uint revision);
            reader.ReadValueSafe(out ushort count);
            var states = new EnemyAffixStackNetworkState[count];
            for (int i = 0; i < count; i++)
            {
                reader.ReadValueSafe(out states[i].AffixId);
                reader.ReadValueSafe(out states[i].Stacks);
            }
            ApplySnapshot(states, revision);
        }

        private void ApplySnapshot(
            IReadOnlyList<EnemyAffixStackNetworkState> states,
            uint revision,
            bool force = false)
        {
            if (!force && revision < m_LocalRevision) return;
            m_LocalRevision = revision;
            m_LocalStacks.Clear();
            if (states != null)
                for (int i = 0; i < states.Count; i++) m_LocalStacks.Add(states[i]);
            m_LocalStacks.Sort((left, right) => left.AffixId.CompareTo(right.AffixId));
            AffixesChanged?.Invoke();
        }
    }
}
