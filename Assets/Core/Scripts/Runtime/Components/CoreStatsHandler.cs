using UnityEngine;
using Unity.Netcode;
using System.Collections.Generic;

namespace Blocks.Gameplay.Core
{
    /// <summary>
    /// Manages a character's stats (e.g., Health, Stamina) based on a <see cref="StatsConfig"/> asset.
    /// It handles stat initialization, modification, consumption, and regeneration.
    /// Stats are synchronized over the network using a <see cref="NetworkList{T}"/>.
    /// It also determines the "alive" state of the character based on a designated primary stat (e.g., Health).
    /// </summary>
    [RequireComponent(typeof(NetworkObject))]
    public class CoreStatsHandler : NetworkBehaviour
    {
        #region Fields & Properties

        [Header("Configuration")]
        [Tooltip("The ScriptableObject defining the stats for this character.")]
        [SerializeField] private StatsConfig statsConfig;

        [Header("Broadcasting on")]
        [Tooltip("GameEvent raised when any stat's value changes (only for stats with broadcastNetworkedEvents enabled).")]
        [SerializeField] private StatChangeEvent onStatChangedEvent;
        [Tooltip("GameEvent raised when any stat reaches zero (only for stats with broadcastNetworkedEvents enabled).")]
        [SerializeField] private StatDepletedEvent onStatDepletedEvent;

        /// <summary>
        /// Gets a value indicating whether the character is currently alive.
        /// The character is considered alive if their primary stat (e.g., Health) is greater than zero.
        /// </summary>
        public bool IsAlive { get; private set; } = true;

        // Networked list that synchronizes stat values across all clients
        private NetworkList<RuntimeStat> m_RuntimeStats;

        // Tracks the last time each stat was consumed to enforce regeneration delays
        private readonly Dictionary<int, float> m_LastStatUseTime = new Dictionary<int, float>();

        // Caches stat definitions by hash for fast lookup without config access
        private readonly Dictionary<int, StatDefinition> m_StatDefinitions = new Dictionary<int, StatDefinition>();
        private bool m_UseServerAuthority = true;

        private bool HasStatAuthority => m_UseServerAuthority ? IsServer : IsOwner;

        #endregion

        #region Unity & Network Lifecycle

        private void Awake()
        {
            if (statsConfig == null)
            {
                Debug.LogError($"[CoreStatsHandler] StatsConfig not assigned", this);
                enabled = false;
                return;
            }

            // Preserve the official template's DA sample scenes while using a
            // single server writer in the VampireHunt Client-Server scene.
            NetworkManager manager = NetworkManager.Singleton;
            m_UseServerAuthority = manager == null || !manager.DistributedAuthorityMode;
            NetworkVariableWritePermission writePermission = m_UseServerAuthority
                ? NetworkVariableWritePermission.Server
                : NetworkVariableWritePermission.Owner;
            m_RuntimeStats = new NetworkList<RuntimeStat>(
                default,
                NetworkVariableReadPermission.Everyone,
                writePermission);

            // Populate the definitions dictionary from the config for fast lookups
            foreach (var def in statsConfig.stats)
            {
                m_StatDefinitions[Animator.StringToHash(def.statName)] = def;
            }
        }

        public override void OnNetworkSpawn()
        {
            if (HasStatAuthority)
            {
                if (m_RuntimeStats.Count == 0)
                {
                    if (statsConfig.stats == null || statsConfig.stats.Count == 0)
                    {
                        Debug.LogWarning($"[CoreStatsHandler] No stats defined in StatsConfig on {gameObject.name}", this);
                        return;
                    }

                    foreach (var def in statsConfig.stats)
                    {
                        m_RuntimeStats.Add(new RuntimeStat
                        {
                            StatHash = Animator.StringToHash(def.statName),
                            CurrentValue = def.startingValue,
                            MaxValue = def.maxValue
                        });
                    }
                }
            }

            // Subscribe to changes in the stat list to update UI and game logic
            m_RuntimeStats.OnListChanged += OnStatsListChanged;

            // Broadcast the initial state of all stats for late-joining clients or initialization
            foreach (var stat in m_RuntimeStats)
            {
                BroadcastStatChange(stat);
            }

            // Set the initial alive state
            UpdateAliveState();
        }

        public override void OnNetworkDespawn()
        {
            // Unsubscribe to prevent memory leaks
            m_RuntimeStats.OnListChanged -= OnStatsListChanged;
        }

        private void Update()
        {
            // Regeneration is authoritative and runs once on the server copy of
            // each player object.
            if (!HasStatAuthority || !IsAlive) return;

            HandleRegeneration();
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Modifies a stat by a given amount. The server applies immediately;
        /// the owning client submits a trusted request to the server.
        /// </summary>
        /// <param name="statHash">The hash of the stat to modify (use StatKeys).</param>
        /// <param name="amount">The amount to add or subtract.</param>
        /// <param name="sourcePlayerId">Who caused this change.</param>
        /// <param name="sourceType">What type of modification this is.</param>
        public void ModifyStat(int statHash, float amount, ulong sourcePlayerId = 0, ModificationSource sourceType = ModificationSource.Direct)
        {
            if (HasStatAuthority)
            {
                ModifyStatOnAuthority(statHash, amount, sourcePlayerId, sourceType);
                return;
            }

            if (m_UseServerAuthority && IsOwner && IsSpawned)
            {
                RequestModifyStatRpc(statHash, amount, sourcePlayerId, sourceType);
            }
        }

        private void ModifyStatOnAuthority(int statHash, float amount, ulong sourcePlayerId, ModificationSource sourceType)
        {
            if (!HasStatAuthority) return;

            int statIndex = FindStatIndex(statHash);

            if (statIndex != -1)
            {
                ModifyStat(statIndex, amount, true, sourcePlayerId, sourceType);
            }
            else
            {
                Debug.LogWarning($"[CoreStatsHandler] ModifyStat failed: Stat with hash {statHash} not found on {gameObject.name}", this);
            }
        }

        /// <summary>
        /// Attempts to consume a certain amount from a stat.
        /// </summary>
        /// <param name="statHash">The hash of the stat to consume (use StatKeys).</param>
        /// <param name="amount">The amount to consume.</param>
        /// <param name="sourcePlayerId">Who caused this consumption.</param>
        /// <returns>True if the stat had enough value to consume, false otherwise.</returns>
        public bool TryConsumeStat(int statHash, float amount, ulong sourcePlayerId = 0)
        {
            if (amount <= 0f) return true;

            if (HasStatAuthority)
            {
                return TryConsumeStatOnAuthority(statHash, amount, sourcePlayerId);
            }

            if (!m_UseServerAuthority || !IsOwner || !IsSpawned) return false;

            int statIndex = FindStatIndex(statHash);

            if (statIndex == -1)
            {
                Debug.LogWarning($"[CoreStatsHandler] TryConsumeStat failed: Stat with hash {statHash} not found on {gameObject.name}", this);
                return false;
            }

            if (m_RuntimeStats[statIndex].CurrentValue >= amount)
            {
                // Predict only the command result so movement/abilities remain
                // responsive. The server rechecks and owns the actual value.
                RequestConsumeStatRpc(statHash, amount, sourcePlayerId);
                return true;
            }

            return false;
        }

        private bool TryConsumeStatOnAuthority(int statHash, float amount, ulong sourcePlayerId)
        {
            if (!HasStatAuthority || amount <= 0f) return amount <= 0f;

            int statIndex = FindStatIndex(statHash);
            if (statIndex == -1)
            {
                Debug.LogWarning($"[CoreStatsHandler] TryConsumeStat failed: Stat with hash {statHash} not found on {gameObject.name}", this);
                return false;
            }

            if (m_RuntimeStats[statIndex].CurrentValue < amount) return false;

            ModifyStat(statIndex, -amount, true, sourcePlayerId, ModificationSource.Consumption);
            return true;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestModifyStatRpc(
            int statHash,
            float amount,
            ulong sourcePlayerId,
            ModificationSource sourceType)
        {
            ModifyStatOnAuthority(statHash, amount, sourcePlayerId, sourceType);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestConsumeStatRpc(int statHash, float amount, ulong sourcePlayerId)
        {
            TryConsumeStatOnAuthority(statHash, amount, sourcePlayerId);
        }

        /// <summary>
        /// Attempts to consume a certain amount from a stat.
        /// </summary>
        /// <param name="statName">The name of the stat to consume.</param>
        /// <param name="amount">The amount to consume.</param>
        /// <param name="sourcePlayerId">Who caused this consumption.</param>
        /// <returns>True if the stat had enough value to consume, false otherwise.</returns>
        [System.Obsolete("Use TryConsumeStat(int statHash, ...) instead with StatKeys to avoid string hashing overhead")]
        public bool TryConsumeStat(string statName, float amount, ulong sourcePlayerId = 0)
        {
            return TryConsumeStat(Animator.StringToHash(statName), amount, sourcePlayerId);
        }

        /// <summary>
        /// Gets an enumerable collection of all stats with their current and max values.
        /// </summary>
        /// <returns>An IEnumerable of tuples containing the stat name, current value, and max value.</returns>
        public IEnumerable<(string name, float current, float max)> GetAllStats()
        {
            foreach (var runtimeStat in m_RuntimeStats)
            {
                if (m_StatDefinitions.TryGetValue(runtimeStat.StatHash, out var def))
                {
                    yield return (def.statName, runtimeStat.CurrentValue, runtimeStat.MaxValue);
                }
            }
        }

        /// <summary>
        /// Gets the current value of a specific stat.
        /// </summary>
        /// <param name="statHash">The hash of the stat (use StatKeys).</param>
        /// <returns>The current value, or 0 if the stat is not found.</returns>
        public float GetCurrentValue(int statHash)
        {
            int index = FindStatIndex(statHash);
            if (index != -1)
            {
                return m_RuntimeStats[index].CurrentValue;
            }

            Debug.LogWarning($"[CoreStatsHandler] GetCurrentValue: Stat with hash {statHash} not found on {gameObject.name}, returning 0", this);
            return 0;
        }

        /// <summary>
        /// Gets the maximum value of a specific stat.
        /// </summary>
        /// <param name="statHash">The hash of the stat (use StatKeys).</param>
        /// <returns>The maximum value, or 0 if the stat is not found.</returns>
        public float GetMaxValue(int statHash)
        {
            if (m_RuntimeStats != null)
            {
                int index = FindStatIndex(statHash);
                if (index >= 0) return m_RuntimeStats[index].MaxValue;
            }
            if (m_StatDefinitions.TryGetValue(statHash, out var def))
            {
                return def.maxValue;
            }
            return 0f;
        }

        /// <summary>Returns the immutable configured capacity used as the modifier baseline.</summary>
        public float GetBaseMaxValue(int statHash) =>
            m_StatDefinitions.TryGetValue(statHash, out var definition) ? definition.maxValue : 0f;

        /// <summary>
        /// Server-only atomic update of a resource's effective capacity and adjusted current value.
        /// Capacity policy is decided by the gameplay module before entering this template boundary.
        /// </summary>
        public bool TrySetRuntimeMaxValue(
            int statHash,
            float maxValue,
            float adjustedCurrentValue,
            ulong sourcePlayerId = 0,
            ModificationSource sourceType = ModificationSource.Direct)
        {
            if (!HasStatAuthority || m_RuntimeStats == null ||
                float.IsNaN(maxValue) || float.IsInfinity(maxValue) ||
                float.IsNaN(adjustedCurrentValue) || float.IsInfinity(adjustedCurrentValue)) return false;

            int index = FindStatIndex(statHash);
            if (index < 0 || !m_StatDefinitions.TryGetValue(statHash, out StatDefinition definition)) return false;

            RuntimeStat stat = m_RuntimeStats[index];
            stat.MaxValue = Mathf.Max(definition.minValue, maxValue);
            stat.CurrentValue = Mathf.Clamp(adjustedCurrentValue, definition.minValue, stat.MaxValue);
            stat.SourcePlayerId = sourcePlayerId;
            stat.SourceType = sourceType;
            m_RuntimeStats[index] = stat;
            return true;
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Called when the NetworkList of stats changes. This can be an add, remove, or value change.
        /// Runs on all peers when the server modifies a stat value.
        /// </summary>
        private void OnStatsListChanged(NetworkListEvent<RuntimeStat> changeEvent)
        {
            UpdateAliveState();
            var stat = changeEvent.Value;
            BroadcastStatChange(stat, stat.SourcePlayerId, stat.SourceType);
        }

        /// <summary>
        /// Handles the regeneration of stats over time, respecting regeneration delays.
        /// </summary>
        private void HandleRegeneration()
        {
            if (m_RuntimeStats == null || m_RuntimeStats.Count == 0)
            {
                return;
            }

            for (int i = 0; i < m_RuntimeStats.Count; i++)
            {
                var stat = m_RuntimeStats[i];
                if (m_StatDefinitions.TryGetValue(stat.StatHash, out var def))
                {
                    if (def.regenRate > 0 && stat.CurrentValue < stat.MaxValue)
                    {
                        // Only regenerate if the stat has never been used or enough time has passed since last consumption
                        if (!m_LastStatUseTime.ContainsKey(stat.StatHash) ||
                            Time.time - m_LastStatUseTime[stat.StatHash] > def.regenDelay)
                        {
                            // Don't record use time for regeneration to avoid resetting the delay
                            ModifyStat(i, def.regenRate * Time.deltaTime, false, 0, ModificationSource.Regeneration);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Updates the character's alive state based on the primary stat.
        /// Runs on all clients whenever stats change.
        /// </summary>
        private void UpdateAliveState()
        {
            var primaryStatValue = GetCurrentValue(statsConfig.PrimaryStatHash);
            IsAlive = primaryStatValue > 0;
        }

        /// <summary>
        /// Raises the appropriate events with a payload containing the updated stat information.
        /// This runs on all clients when the NetworkList updates, not just the owner.
        /// Use the OwnerClientId in the payload to identify which player the stat change belongs to.
        /// For local player updates, listeners can filter using IsOwner.
        /// </summary>
        /// <param name="stat">The stat that has changed.</param>
        /// <param name="sourcePlayerId">The player who caused this stat change.</param>
        /// <param name="sourceType">The type of modification that occurred.</param>
        private void BroadcastStatChange(RuntimeStat stat, ulong sourcePlayerId = 0, ModificationSource sourceType = ModificationSource.Unknown)
        {
            if (m_StatDefinitions.TryGetValue(stat.StatHash, out var def))
            {
                if (def.eventFlags.HasFlag(StatEventFlags.OnChanged))
                {
                    var statPayload = new StatChangePayload
                    {
                        targetPlayerId = OwnerClientId,
                        sourcePlayerId = sourcePlayerId,
                        sourceType = sourceType,
                        statName = def.statName,
                        statID = stat.StatHash,
                        currentValue = stat.CurrentValue,
                        maxValue = stat.MaxValue
                    };
                    onStatChangedEvent?.Raise(statPayload);
                }

                if (def.eventFlags.HasFlag(StatEventFlags.OnDepleted))
                {
                    if (stat.CurrentValue <= 0)
                    {
                        var depletedPayload = new StatDepletedPayload
                        {
                            playerId = OwnerClientId,
                            statName = def.statName,
                            statID = stat.StatHash
                        };
                        onStatDepletedEvent?.Raise(depletedPayload);
                    }
                }
            }
            else
            {
                Debug.LogWarning($"[CoreStatsHandler] BroadcastStatChange: No definition found for stat hash {stat.StatHash} on {gameObject.name}", this);
            }
        }

        /// <summary>
        /// Internal method to modify a stat by its index in the NetworkList.
        /// Clamps the value to the stat's min and max values and triggers network synchronization.
        /// </summary>
        /// <param name="index">The index of the stat in the NetworkList.</param>
        /// <param name="amount">The amount to add or subtract.</param>
        /// <param name="recordUseTime">Whether to record the use time for regeneration delay tracking.</param>
        /// <param name="sourcePlayerId">The player who caused this change.</param>
        /// <param name="sourceType">The type of modification.</param>
        private void ModifyStat(int index, float amount, bool recordUseTime, ulong sourcePlayerId = 0, ModificationSource sourceType = ModificationSource.Unknown)
        {
            if (!HasStatAuthority) return;

            if (index < 0 || index >= m_RuntimeStats.Count)
            {
                Debug.LogError($"[CoreStatsHandler] ModifyStat: Index {index} out of bounds (count: {m_RuntimeStats.Count}) on {gameObject.name}", this);
                return;
            }

            var stat = m_RuntimeStats[index];
            if (m_StatDefinitions.TryGetValue(stat.StatHash, out var def))
            {
                stat.CurrentValue = Mathf.Clamp(stat.CurrentValue + amount, def.minValue, stat.MaxValue);
                stat.SourcePlayerId = sourcePlayerId;
                stat.SourceType = sourceType;

                // Assignment to NetworkList triggers network synchronization to all clients
                m_RuntimeStats[index] = stat;

                // Record use time only for consumption to enforce regeneration delays
                if (recordUseTime && amount < 0)
                {
                    m_LastStatUseTime[stat.StatHash] = Time.time;
                }
            }
            else
            {
                Debug.LogWarning($"[CoreStatsHandler] ModifyStat: No definition found for stat hash {stat.StatHash} on {gameObject.name}", this);
            }
        }

        /// <summary>
        /// Finds the index of a stat in the NetworkList using its hash.
        /// </summary>
        /// <param name="statHash">The hash of the stat to find.</param>
        /// <returns>The index of the stat, or -1 if not found.</returns>
        private int FindStatIndex(int statHash)
        {
            for (int i = 0; i < m_RuntimeStats.Count; i++)
            {
                if (m_RuntimeStats[i].StatHash == statHash)
                {
                    return i;
                }
            }
            return -1;
        }

        #endregion
    }
}
