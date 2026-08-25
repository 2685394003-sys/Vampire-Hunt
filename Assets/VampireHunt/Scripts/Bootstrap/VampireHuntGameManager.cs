using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Infrastructure.Unity;
using VampireHunt.Run;

namespace VampireHunt.Bootstrap
{
    /// <summary>
    /// Scene composition root for the authoritative run loop. The existing
    /// template GameManager remains responsible for session UI and local respawn
    /// while this component owns VampireHunt phase/clock state.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    [DisallowMultipleComponent]
    public sealed class VampireHuntGameManager : MonoBehaviour
    {
        private const string SnapshotMessageName = "VampireHunt.RunSnapshot.v1";
        private const int SnapshotMessageCapacity = 64;

        [Header("Configuration")]
        [SerializeField] private RunRulesAsset runRules;
        [SerializeField] private bool autoStartWhenMinimumPlayersConnected = true;

        [Header("Diagnostics")]
        [SerializeField] private bool verboseLogging;

        private NetworkManager m_NetworkManager;
        private RunApplicationService m_RunService;
        private bool m_MessageHandlerRegistered;
        private double m_NextSnapshotHeartbeat;

        public RunSnapshot CurrentSnapshot { get; private set; }
        public bool IsServerAuthority => m_NetworkManager != null && m_NetworkManager.IsServer;

        /// <summary>Presentation/read-model sinks subscribe locally to snapshots.</summary>
        public event Action<RunSnapshot> SnapshotChanged;

        private void Awake()
        {
            if (runRules == null)
            {
                Debug.LogError("[VampireHuntGameManager] RunRulesAsset is not assigned.", this);
                enabled = false;
            }
        }

        private void OnEnable()
        {
            TryBindNetworkManager();
        }

        private void Start()
        {
            TryBindNetworkManager();
        }

        private void Update()
        {
            if (m_NetworkManager == null)
            {
                TryBindNetworkManager();
                return;
            }

            TryRegisterSnapshotHandler();
            if (!m_NetworkManager.IsListening || !m_NetworkManager.IsServer) return;

            EnsureServerRunService();
            double serverTime = m_NetworkManager.ServerTime.Time;
            bool stateChanged = m_RunService.Tick(serverTime);

            if (stateChanged || serverTime >= m_NextSnapshotHeartbeat)
            {
                PublishSnapshot();
            }
        }

        private void OnDisable()
        {
            UnbindNetworkManager();
        }

        public double GetEstimatedRemainingSeconds()
        {
            if (m_NetworkManager == null || !m_NetworkManager.IsListening)
            {
                return CurrentSnapshot.PausedRemainingSeconds;
            }

            return CurrentSnapshot.GetRemainingSeconds(m_NetworkManager.ServerTime.Time);
        }

        public bool TryStartRun()
        {
            return TryServerMutation((service, time) => service.TryStartRun(time));
        }

        public bool TryEnterBossEncounter()
        {
            return TryTransition(RunPhase.BossEncounter);
        }

        public bool TryBeginBossPhaseTransition()
        {
            return TryTransition(RunPhase.BossPhaseTransition);
        }

        public bool TryResumeBossEncounter()
        {
            return TryTransition(RunPhase.BossEncounter);
        }

        public bool TryResumeExploration()
        {
            return TryTransition(RunPhase.Exploring);
        }

        public bool TryCompleteRun()
        {
            return TryTransition(RunPhase.Victory);
        }

        public bool TryFailRun()
        {
            return TryTransition(RunPhase.Defeat);
        }

        public bool TryReturnToLobby()
        {
            return TryTransition(RunPhase.Lobby);
        }

        public bool TryExtendClock(double seconds)
        {
            return TryServerMutation((service, time) => service.TryExtendClock(seconds, time));
        }

        public bool TrySetClockDrainRate(double drainRate)
        {
            return TryServerMutation((service, time) => service.TrySetDrainRate(drainRate, time));
        }

        public bool TryPauseClock()
        {
            return TryServerMutation((service, time) => service.TryPauseClock(time));
        }

        public bool TryResumeClock()
        {
            return TryServerMutation((service, time) => service.TryResumeClock(time));
        }

        private bool TryTransition(RunPhase nextPhase)
        {
            return TryServerMutation((service, time) => service.TryTransition(nextPhase, time));
        }

        private bool TryServerMutation(Func<RunApplicationService, double, bool> mutation)
        {
            if (m_NetworkManager == null || !m_NetworkManager.IsListening || !m_NetworkManager.IsServer)
            {
                return false;
            }

            EnsureServerRunService();
            bool changed = mutation(m_RunService, m_NetworkManager.ServerTime.Time);
            if (changed) PublishSnapshot();
            return changed;
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
            TryRegisterSnapshotHandler();

            if (m_NetworkManager.IsListening && m_NetworkManager.IsServer)
            {
                EnsureServerRunService();
                TryAutoStartRun();
                PublishSnapshot();
            }
        }

        private void UnbindNetworkManager()
        {
            if (m_NetworkManager == null) return;

            if (m_MessageHandlerRegistered && m_NetworkManager.CustomMessagingManager != null)
            {
                m_NetworkManager.CustomMessagingManager.UnregisterNamedMessageHandler(SnapshotMessageName);
            }

            m_NetworkManager.OnServerStarted -= HandleServerStarted;
            m_NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
            m_NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            m_MessageHandlerRegistered = false;
            m_NetworkManager = null;
        }

        private void TryRegisterSnapshotHandler()
        {
            if (m_MessageHandlerRegistered || m_NetworkManager?.CustomMessagingManager == null) return;
            m_NetworkManager.CustomMessagingManager.RegisterNamedMessageHandler(
                SnapshotMessageName,
                HandleSnapshotMessage);
            m_MessageHandlerRegistered = true;
        }

        private void HandleServerStarted()
        {
            EnsureServerRunService();
            TryAutoStartRun();
            PublishSnapshot();
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (!m_NetworkManager.IsServer) return;

            EnsureServerRunService();
            bool runStarted = TryAutoStartRun();
            if (runStarted)
            {
                PublishSnapshot();
            }
            else
            {
                PublishSnapshot(clientId);
            }
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            if (m_NetworkManager == null || m_NetworkManager.IsServer) return;
            if (clientId != m_NetworkManager.LocalClientId) return;

            ApplySnapshot(default, true);
        }

        private bool TryAutoStartRun()
        {
            if (!autoStartWhenMinimumPlayersConnected || m_RunService == null) return false;
            if (m_RunService.Phase != RunPhase.Lobby) return false;
            if (m_NetworkManager.ConnectedClientsIds.Count < runRules.MinimumPlayersToStart) return false;

            return m_RunService.TryStartRun(m_NetworkManager.ServerTime.Time);
        }

        private void EnsureServerRunService()
        {
            if (m_RunService != null) return;

            m_RunService = new RunApplicationService(
                runRules.CreateRules(),
                m_NetworkManager.ServerTime.Time);
        }

        private void PublishSnapshot(ulong? targetClientId = null)
        {
            if (m_RunService == null || m_NetworkManager?.CustomMessagingManager == null) return;

            double serverTime = m_NetworkManager.ServerTime.Time;
            RunSnapshot snapshot = m_RunService.CaptureSnapshot(serverTime);
            ApplySnapshot(snapshot);
            m_NextSnapshotHeartbeat = serverTime + runRules.SnapshotHeartbeatSeconds;

            if (!m_NetworkManager.IsListening || !m_NetworkManager.IsServer) return;

            var writer = new FastBufferWriter(SnapshotMessageCapacity, Allocator.Temp);
            try
            {
                WriteSnapshot(ref writer, snapshot);
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
            // The server owns and applies its local snapshot before sending. Clients
            // only accept the authoritative server endpoint as a snapshot source.
            if (m_NetworkManager == null || m_NetworkManager.IsServer) return;
            if (senderClientId != NetworkManager.ServerClientId) return;

            reader.ReadValueSafe(out byte phaseValue);
            reader.ReadValueSafe(out double endServerTime);
            reader.ReadValueSafe(out double pausedRemainingSeconds);
            reader.ReadValueSafe(out double drainRate);
            reader.ReadValueSafe(out bool isPaused);
            reader.ReadValueSafe(out uint revision);

            if (!Enum.IsDefined(typeof(RunPhase), phaseValue)) return;

            ApplySnapshot(new RunSnapshot(
                (RunPhase)phaseValue,
                endServerTime,
                pausedRemainingSeconds,
                drainRate,
                isPaused,
                revision));
        }

        private static void WriteSnapshot(ref FastBufferWriter writer, RunSnapshot snapshot)
        {
            writer.WriteValueSafe((byte)snapshot.Phase);
            writer.WriteValueSafe(snapshot.EndServerTime);
            writer.WriteValueSafe(snapshot.PausedRemainingSeconds);
            writer.WriteValueSafe(snapshot.DrainRate);
            writer.WriteValueSafe(snapshot.IsPaused);
            writer.WriteValueSafe(snapshot.Revision);
        }

        private void ApplySnapshot(RunSnapshot snapshot, bool force = false)
        {
            if (!force && snapshot.Revision < CurrentSnapshot.Revision) return;

            CurrentSnapshot = snapshot;
            SnapshotChanged?.Invoke(snapshot);

            if (verboseLogging)
            {
                Debug.Log(
                    $"[VampireHuntGameManager] Phase={snapshot.Phase}, " +
                    $"Remaining={GetEstimatedRemainingSeconds():F1}, Revision={snapshot.Revision}",
                    this);
            }
        }
    }
}
