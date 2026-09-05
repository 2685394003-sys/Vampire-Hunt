using Unity.Netcode;
using UnityEngine;
using VampireHunt.Infrastructure.Unity.Boss;
using VampireHunt.Navigation;

namespace VampireHunt.Infrastructure.Netcode
{
    /// <summary>
    /// Server-only debug facade. It exposes explicit reversible commands without putting
    /// UI concerns into the encounter director or ability driver.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class BossDebugController : MonoBehaviour
    {
        [SerializeField] private NetworkObject networkObject;
        [SerializeField] private BossEncounterDirector encounterDirector;
        [SerializeField] private BossAbilityServerDriver abilityDriver;
        [SerializeField] private BossRoamingMovement roamingMovement;
        [SerializeField] private BossAbilityPhaseProvider phaseProvider;

        private bool m_DebugEnabled;
        private bool m_MovementPaused;

        public bool HasServerAuthority
        {
            get
            {
                if (!isActiveAndEnabled || networkObject == null || !networkObject.IsSpawned) return false;
                NetworkManager manager = networkObject.NetworkManager;
                return manager != null && manager.IsListening && manager.IsServer;
            }
        }
        public bool DebugEnabled => m_DebugEnabled;
        public bool MovementPaused => m_MovementPaused;
        public BossAbilityPhaseProvider PhaseProvider => phaseProvider;

        private void Awake()
        {
            if (networkObject == null) networkObject = GetComponent<NetworkObject>();
            if (encounterDirector == null) encounterDirector = GetComponent<BossEncounterDirector>();
            if (abilityDriver == null) abilityDriver = GetComponent<BossAbilityServerDriver>();
            if (roamingMovement == null) roamingMovement = GetComponent<BossRoamingMovement>();
            if (phaseProvider == null) phaseProvider = GetComponent<BossAbilityPhaseProvider>();
        }

        public bool SetDebugEnabled(bool enabled)
        {
            if (!enabled)
            {
                ReleaseOverrides();
                return true;
            }

            if (!HasServerAuthority) return false;
            m_DebugEnabled = true;
            return true;
        }

        public bool TrySetMovementPaused(bool paused)
        {
            if (!CanIssueCommand() || roamingMovement == null) return false;
            roamingMovement.SetDebugPaused(paused);
            m_MovementPaused = paused;
            return true;
        }

        public bool TryForceStage(int stageNumber)
        {
            return CanIssueCommand() && encounterDirector != null &&
                   encounterDirector.TryForceStageForDebugServer(stageNumber);
        }

        public bool TryForceAbility(uint abilityId)
        {
            if (!CanIssueCommand() || abilityDriver == null || abilityId == 0) return false;
            if (abilityDriver.HasActiveCast && !abilityDriver.TryCancelActiveCastServer()) return false;
            return abilityDriver.TryForceAbilityServer(abilityId);
        }

        public bool TryCancelAbility()
        {
            return CanIssueCommand() && abilityDriver != null &&
                   (!abilityDriver.HasActiveCast || abilityDriver.TryCancelActiveCastServer());
        }

        private bool CanIssueCommand() => m_DebugEnabled && HasServerAuthority;

        private void OnDisable()
        {
            ReleaseOverrides();
        }

        private void ReleaseOverrides()
        {
            if (roamingMovement != null) roamingMovement.SetDebugPaused(false);
            m_MovementPaused = false;
            m_DebugEnabled = false;
        }
    }
}
