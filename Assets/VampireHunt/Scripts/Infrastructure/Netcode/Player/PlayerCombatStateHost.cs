using System;
using System.Collections.Generic;
using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Player;

namespace VampireHunt.Infrastructure.Netcode.Player
{
    [DisallowMultipleComponent, RequireComponent(typeof(CoreStatsHandler))]
    public sealed class PlayerCombatStateHost : NetworkBehaviour, IPlayerCombatStateReader, IStatRegenerationPolicy
    {
        [SerializeField, Min(0)] private float combatExitDelay = 5f;
        [SerializeField, Min(0)] private float outOfCombatHealthFraction = 1f / 60f;
        [SerializeField, Min(0)] private float outOfCombatStaminaMultiplier = 2f;
        [SerializeField, Min(0)] private float dashRecoveryDelay = 0.2f;
        // 0 = not initialized; 1 = non-combat; 2 = combat. One atomic late-join snapshot.
        private readonly NetworkVariable<byte> m_Snapshot = new NetworkVariable<byte>();
        private CoreStatsHandler m_Stats;
        private PlayerCombatStateController m_Controller;
        private PlayerRecoveryScheduler m_Scheduler;
        private bool m_Advancing;
        private readonly HashSet<(CombatActivityKind, ulong, ulong, uint, ulong)> m_Seen =
            new HashSet<(CombatActivityKind, ulong, ulong, uint, ulong)>();
        private readonly Queue<(CombatActivityKind, ulong, ulong, uint, ulong)> m_Recent =
            new Queue<(CombatActivityKind, ulong, ulong, uint, ulong)>();
        public PlayerCombatState State => IsServer && m_Controller != null ? m_Controller.State :
            m_Snapshot.Value == 2 ? PlayerCombatState.Combat : PlayerCombatState.NonCombat;
        public bool IsInitialized => IsSpawned && m_Snapshot.Value != 0;
        public uint Generation => m_Controller?.Generation ?? 0;
        public event Action<PlayerCombatState, PlayerCombatState> StateChanged;

        private void Awake() { m_Stats = GetComponent<CoreStatsHandler>(); }

        private void OnValidate()
        {
            combatExitDelay = Mathf.Max(0f, combatExitDelay);
            outOfCombatHealthFraction = Mathf.Max(0f, outOfCombatHealthFraction);
            outOfCombatStaminaMultiplier = Mathf.Max(0f, outOfCombatStaminaMultiplier);
            dashRecoveryDelay = Mathf.Max(0f, dashRecoveryDelay);
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            m_Snapshot.OnValueChanged += OnSnapshot;
            if (!IsServer) return;
            m_Controller = new PlayerCombatStateController(Math.Max(0, combatExitDelay));
            m_Controller.Reset(m_Stats.IsAlive);
            m_Stats.RegenerationPolicy = this;
            m_Stats.ExternallyScheduledRegeneration = true;
            m_Stats.AuthorityStatChanged += OnStatChanged;
            m_Scheduler = PlayerRecoveryScheduler.GetOrCreate(NetworkManager);
            m_Scheduler.Register(this);
            Publish();
        }

        public override void OnNetworkDespawn()
        {
            m_Snapshot.OnValueChanged -= OnSnapshot;
            if (m_Stats != null)
            {
                m_Stats.AuthorityStatChanged -= OnStatChanged;
                if (ReferenceEquals(m_Stats.RegenerationPolicy, this)) m_Stats.RegenerationPolicy = null;
                m_Stats.ExternallyScheduledRegeneration = false;
            }
            if (m_Scheduler != null) m_Scheduler.Unregister(this);
            m_Controller?.Reset(false);
            m_Seen.Clear();
            m_Recent.Clear();
            base.OnNetworkDespawn();
        }

        private void OnSnapshot(byte before, byte after)
        {
            if (IsServer || before == after) return;
            StateChanged?.Invoke(before == 2 ? PlayerCombatState.Combat : PlayerCombatState.NonCombat,
                after == 2 ? PlayerCombatState.Combat : PlayerCombatState.NonCombat);
        }

        private void Publish()
        {
            byte next = m_Controller.State == PlayerCombatState.Combat ? (byte)2 : (byte)1;
            byte before = m_Snapshot.Value;
            if (before == next) return;
            m_Snapshot.Value = next;
            StateChanged?.Invoke(before == 2 ? PlayerCombatState.Combat : PlayerCombatState.NonCombat, State);
        }

        private void OnStatChanged(AuthorityStatChange change)
        {
            if (change.StatId != StatKeys.Health || m_Controller == null) return;
            bool isAlive = change.After > 0f;
            if (m_Controller.IsAlive == isAlive) return;
            m_Controller.Reset(isAlive);
            m_Seen.Clear();
            m_Recent.Clear();
            Publish();
        }

        public void RecordActivityServer(CombatActivityKind kind, ulong source, ulong target, uint action, ulong sequence)
        {
            if (!IsSpawned || !IsServer || m_Controller == null || !m_Stats.IsAlive) return;
            var key = (kind, source, target, action, sequence);
            if (!m_Seen.Add(key)) return;
            m_Recent.Enqueue(key);
            if (m_Recent.Count > 512) m_Seen.Remove(m_Recent.Dequeue());
            double now = Time.timeAsDouble;
            // Repeated combat activity does not force extra network stat writes.
            if (State == PlayerCombatState.NonCombat || (!m_Controller.HasSustainedActions && now >= m_Controller.ExitAt))
                AdvanceServer(now, false);
            m_Controller.Record(now);
            Publish();
        }

        public void BeginActionServer(ulong handle, uint generation)
        {
            if (!IsServer || !IsSpawned || !m_Stats.IsAlive) return;
            AdvanceServer(Time.timeAsDouble);
            m_Controller.Begin(handle, generation, Time.timeAsDouble);
            Publish();
        }

        public void EndActionServer(ulong handle, uint generation)
        {
            if (!IsServer || !IsSpawned || m_Controller == null) return;
            AdvanceServer(Time.timeAsDouble);
            m_Controller.End(handle, generation, Time.timeAsDouble);
            Publish();
        }

        internal void AdvanceServer(double now, bool publish = true)
        {
            if (m_Advancing || !IsSpawned || !IsServer || m_Controller == null) return;
            m_Advancing = true;
            try
            {
                if (m_Controller.State == PlayerCombatState.Combat && !m_Controller.HasSustainedActions &&
                    now >= m_Controller.ExitAt)
                {
                    // Integrate each rate on its own side of the exact state transition.
                    m_Stats.TickRegeneration(m_Controller.ExitAt);
                    m_Controller.Tick(now);
                    if (publish) Publish();
                }
                m_Stats.TickRegeneration(now);
            }
            finally { m_Advancing = false; }
        }

        public float GetRate(int statId, float maximum, float configuredRate)
        {
            if (State == PlayerCombatState.Combat) return configuredRate;
            if (statId == StatKeys.Health) return maximum * outOfCombatHealthFraction;
            if (statId == StatKeys.Stamina) return configuredRate * outOfCombatStaminaMultiplier;
            return configuredRate;
        }

        public float GetDelay(int statId, StatUseKind useKind, float configuredDelay)
        {
            if (useKind == StatUseKind.Continuous) return 0;
            if (statId == StatKeys.Stamina && useKind == StatUseKind.Burst) return dashRecoveryDelay;
            return configuredDelay;
        }
    }
}
