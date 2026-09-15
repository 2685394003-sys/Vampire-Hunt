using System.Collections.Generic;
using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Combat;
using VampireHunt.Contracts;

namespace VampireHunt.Infrastructure.Integration
{
    /// <summary>Server composition root for dynamic Health/Stamina capacities.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CoreStatsHandler))]
    public sealed class PlayerResourceCapacityHost : NetworkBehaviour, IResourceCapacityModifierTarget
    {
        private readonly struct PendingChange
        {
            public CapacityChangePolicy Policy { get; }
            public PendingChange(CapacityChangePolicy policy) => Policy = policy;
        }

        [SerializeField] private CoreStatsHandler coreStats;

        private readonly ResourceCapacityModifierCollection m_Modifiers =
            new ResourceCapacityModifierCollection();
        private readonly Dictionary<int, PendingChange> m_Pending =
            new Dictionary<int, PendingChange>();
        private readonly List<int> m_ResourceBuffer = new List<int>();

        public int ModifierCount => m_Modifiers.Count;

        private void Awake()
        {
            if (coreStats == null) coreStats = GetComponent<CoreStatsHandler>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            FlushPending();
        }

        private void Update()
        {
            if (m_Pending.Count > 0) FlushPending();
        }

        public bool RegisterCapacityModifier(IResourceCapacityModifier modifier)
        {
            if (!m_Modifiers.Register(modifier)) return false;
            MarkPending(modifier.ResourceId, modifier.ChangePolicy);
            return true;
        }

        public bool UpdateCapacityModifier(IResourceCapacityModifier modifier)
        {
            if (!m_Modifiers.Contains(modifier)) return false;
            MarkPending(modifier.ResourceId, modifier.ChangePolicy);
            return true;
        }

        public bool UnregisterCapacityModifier(IResourceCapacityModifier modifier)
        {
            if (!m_Modifiers.Unregister(modifier)) return false;
            MarkPending(modifier.ResourceId, modifier.ChangePolicy);
            return true;
        }

        private void MarkPending(int resourceId, CapacityChangePolicy policy)
        {
            if (resourceId == 0) return;
            m_Pending[resourceId] = new PendingChange(policy);
            FlushPending();
        }

        private void FlushPending()
        {
            if (!IsSpawned || !IsServer || coreStats == null || m_Pending.Count == 0) return;
            m_ResourceBuffer.Clear();
            foreach (int resourceId in m_Pending.Keys) m_ResourceBuffer.Add(resourceId);
            for (int i = 0; i < m_ResourceBuffer.Count; i++)
            {
                int resourceId = m_ResourceBuffer[i];
                if (!m_Pending.TryGetValue(resourceId, out PendingChange change)) continue;
                if (Recompute(resourceId, change.Policy)) m_Pending.Remove(resourceId);
            }
        }

        private bool Recompute(int resourceId, CapacityChangePolicy policy)
        {
            float baseMax = coreStats.GetBaseMaxValue(resourceId);
            float oldMax = coreStats.GetMaxValue(resourceId);
            float newMax = Mathf.Max(0f, m_Modifiers.Resolve(resourceId, baseMax));
            float current = coreStats.GetCurrentValue(resourceId);
            float adjustedCurrent;
            switch (policy)
            {
                case CapacityChangePolicy.PreserveCurrent:
                    adjustedCurrent = current;
                    break;
                case CapacityChangePolicy.GrantIncreaseOnly:
                    adjustedCurrent = current + Mathf.Max(0f, newMax - oldMax);
                    break;
                default:
                    adjustedCurrent = oldMax > 0f ? newMax * Mathf.Clamp01(current / oldMax) : current;
                    break;
            }
            return coreStats.TrySetRuntimeMaxValue(
                resourceId, newMax, adjustedCurrent, OwnerClientId, ModificationSource.Direct);
        }

        public override void OnNetworkDespawn()
        {
            m_Pending.Clear();
            m_Modifiers.Clear();
            base.OnNetworkDespawn();
        }
    }
}
