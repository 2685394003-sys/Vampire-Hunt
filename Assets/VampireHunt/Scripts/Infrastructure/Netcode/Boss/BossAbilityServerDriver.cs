using Unity.Netcode;
using UnityEngine;
using VampireHunt.Boss.Abilities;
using VampireHunt.Infrastructure.Integration;
using VampireHunt.Infrastructure.Unity.Boss;
using VampireHunt.Infrastructure.Unity;

namespace VampireHunt.Infrastructure.Netcode
{
    /// <summary>
    /// Authoritative lifecycle driver for Boss abilities. It is the only component that
    /// advances the host each frame; replicated state remains owned by the replicator.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(BossAbilityHost))]
    [RequireComponent(typeof(BossAbilityStateReplicator))]
    [RequireComponent(typeof(BossAbilityPhaseProvider))]
    [RequireComponent(typeof(BossAbilityContextProvider))]
    public sealed class BossAbilityServerDriver : NetworkBehaviour
    {
        [SerializeField] private BossAbilityHost host;
        [SerializeField] private BossAbilityPhaseProvider phaseProvider;
        [SerializeField] private BossAbilityContextProvider contextProvider;
        [SerializeField] private BossAbilityStateReplicator stateReplicator;

        [Header("Scheduling")]
        [SerializeField] private bool allowAutomaticCasts = true;
        [SerializeField] private int runSeed = 1337;

        [Header("Editor Preview")]
        [Tooltip("Runs the same authoritative driver locally before a network session starts.")]
        [SerializeField] private bool offlinePreview;

        private uint m_SelectionOrdinal;
        private bool m_OfflineInitialized;

        public bool HasActiveCast => host != null && host.Snapshot.IsCasting;
        public bool AutomaticCastsEnabled => allowAutomaticCasts;

        private void Awake()
        {
            if (host == null) host = GetComponent<BossAbilityHost>();
            if (phaseProvider == null) phaseProvider = GetComponent<BossAbilityPhaseProvider>();
            if (contextProvider == null) contextProvider = GetComponent<BossAbilityContextProvider>();
            if (stateReplicator == null) stateReplicator = GetComponent<BossAbilityStateReplicator>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            m_OfflineInitialized = false;
            m_SelectionOrdinal = 0;

            if (IsServer)
                InitializeAndPublish(NetworkManager.ServerTime.Time, publishOffline: false);
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && host != null)
                host.ResetServer(NetworkManager != null ? NetworkManager.ServerTime.Time : 0d);
            m_SelectionOrdinal = 0;
            m_OfflineInitialized = false;
            base.OnNetworkDespawn();
        }

        private void Update()
        {
            // 单人模式菜单暂停时冻结 Boss 能力调度（ServerTime 是墙钟，不受 Time.timeScale 影响）。
            if (MenuPauseController.IsPaused) return;

            if (IsSpawned)
            {
                if (!IsServer || host == null || !host.IsInitialized) return;
                TickAndPublish(NetworkManager.ServerTime.Time, publishOffline: false);
                return;
            }

            if (!offlinePreview || NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
                return;

            double previewTime = Time.unscaledTimeAsDouble;
            if (!m_OfflineInitialized)
                m_OfflineInitialized = InitializeAndPublish(previewTime, publishOffline: true);
            if (!m_OfflineInitialized) return;

            TickAndPublish(previewTime, publishOffline: true);
        }

        public bool TrySetPhaseServer(int phaseNumber)
        {
            if (!IsServer || host == null || !host.IsInitialized || phaseProvider == null ||
                !phaseProvider.TryCreatePhase(phaseNumber, out BossPhaseDefinition phase)) return false;

            double serverTime = NetworkManager.ServerTime.Time;
            bool changed = host.TrySetPhaseServer(phase, serverTime);
            if (!changed) return false;

            m_SelectionOrdinal = 0;
            stateReplicator.PublishServer(host.Snapshot, force: true);
            return true;
        }

        public bool SetAutomaticCastsServer(bool enabled)
        {
            if (IsSpawned && !IsServer) return false;
            if (allowAutomaticCasts == enabled) return false;
            allowAutomaticCasts = enabled;
            return true;
        }

        public bool TryCancelActiveCastServer()
        {
            if (!IsServer || host == null || !host.IsInitialized || stateReplicator == null) return false;

            double serverTime = NetworkManager.ServerTime.Time;
            if (!host.CancelActiveCastServer(serverTime)) return false;
            stateReplicator.PublishServer(host.Snapshot, force: true);
            return true;
        }

        public bool TryParryActiveAbilityServer()
        {
            if (!IsServer || host == null || !host.IsInitialized || stateReplicator == null) return false;

            double serverTime = NetworkManager.ServerTime.Time;
            if (!host.TryParryActiveCastServer(serverTime)) return false;
            stateReplicator.PublishServer(host.Snapshot, force: true);
            return true;
        }

        public bool TryForceAbilityServer(uint abilityId)
        {
            if (abilityId == 0 || !IsServer || host == null || !host.IsInitialized ||
                phaseProvider == null || contextProvider == null || stateReplicator == null ||
                !phaseProvider.TryGetAbility(abilityId, out BossAbilityAsset ability)) return false;

            double serverTime = NetworkManager.ServerTime.Time;
            BossAbilityExecutionInput input = contextProvider.Capture();
            uint seed = HashSeed(unchecked((uint)runSeed), ++m_SelectionOrdinal);
            int participantCount = CountSpawnedParticipants();
            if (!host.TryStartAbilityServer(
                    ability.CreateDefinition(), serverTime, input, seed, participantCount)) return false;
            stateReplicator.PublishServer(host.Snapshot, force: true);
            return true;
        }

        private bool InitializeAndPublish(double serverTime, bool publishOffline)
        {
            if (host == null || phaseProvider == null || stateReplicator == null ||
                !phaseProvider.TryCreateStartingPhase(out BossPhaseDefinition phase))
            {
                Debug.LogError("[BossAbilityServerDriver] Boss phase configuration is incomplete.", this);
                return false;
            }

            if (!host.InitializeServer(phase, serverTime)) return false;
            m_SelectionOrdinal = 0;
            Publish(host.Snapshot, publishOffline, force: true);
            return true;
        }

        private void TickAndPublish(double serverTime, bool publishOffline)
        {
            if (contextProvider == null || stateReplicator == null) return;
            BossAbilityExecutionInput input = contextProvider.Capture();
            uint seed = HashSeed(unchecked((uint)runSeed), ++m_SelectionOrdinal);
            int participantCount = publishOffline ? 1 : CountSpawnedParticipants();
            bool changed = host.TickServer(
                serverTime, input, seed, allowAutomaticCasts, participantCount);
            if (changed) Publish(host.Snapshot, publishOffline, force: false);
        }

        private int CountSpawnedParticipants()
        {
            NetworkManager manager = NetworkManager;
            if (manager == null || !manager.IsListening) return 1;

            int count = 0;
            var clients = manager.ConnectedClientsList;
            for (int i = 0; i < clients.Count; i++)
            {
                NetworkObject playerObject = clients[i]?.PlayerObject;
                if (playerObject != null && playerObject.IsSpawned) count++;
            }

            return Mathf.Clamp(count, 1, 64);
        }

        private void Publish(in BossAbilitySnapshot snapshot, bool offline, bool force)
        {
            if (offline) stateReplicator.PublishOffline(snapshot, force);
            else stateReplicator.PublishServer(snapshot, force);
        }

        private static uint HashSeed(uint seed, uint ordinal)
        {
            unchecked
            {
                uint value = seed ^ (ordinal * 0x9E3779B9u);
                value ^= value >> 16;
                value *= 0x7FEB352Du;
                value ^= value >> 15;
                value *= 0x846CA68Bu;
                value ^= value >> 16;
                return value;
            }
        }
    }
}
