using UnityEngine;
using VampireHunt.Boss.Abilities;

namespace VampireHunt.Infrastructure.Integration
{
    /// <summary>
    /// Owns only the pure boss ability runtime. Phase lookup, target sampling, ticking,
    /// deterministic random seeds, networking and presentation are separate components.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossAbilityHost : MonoBehaviour
    {
        [SerializeField] private BossAbilityServiceHost serviceHost;

        private BossAbilityController m_Controller;
        private bool m_Initialized;

        public bool IsInitialized => m_Initialized;
        public BossAbilitySnapshot Snapshot => m_Controller != null
            ? m_Controller.CaptureSnapshot()
            : default;

        public bool InitializeServer(BossPhaseDefinition startingPhase, double serverTime)
        {
            ResetServer(serverTime);
            if (startingPhase == null)
            {
                Debug.LogError("[BossAbilityHost] Cannot initialize without a phase definition.", this);
                return false;
            }

            if (serviceHost == null) serviceHost = GetComponent<BossAbilityServiceHost>();
            if (serviceHost == null || !serviceHost.TryBuild(out BossAbilityServices services))
            {
                Debug.LogError("[BossAbilityHost] Boss ability services are incomplete.", this);
                return false;
            }

            m_Controller = new BossAbilityController(services);
            m_Controller.LoadPhase(startingPhase, serverTime);
            m_Initialized = true;
            return true;
        }

        public bool TickServer(
            double serverTime,
            in BossAbilityExecutionInput input,
            uint randomSeed,
            bool allowNewCast,
            int participantCount = 1)
        {
            if (!m_Initialized || m_Controller == null) return false;
            return m_Controller.Tick(
                serverTime,
                input.Selection,
                input.TargetEntityId,
                input.SourcePosition,
                input.TargetPosition,
                input.Direction,
                randomSeed,
                allowNewCast,
                participantCount);
        }

        public bool TryStartAbilityServer(
            BossAbilityDefinition ability,
            double serverTime,
            in BossAbilityExecutionInput input,
            uint randomSeed,
            int participantCount = 1)
        {
            return m_Initialized && m_Controller != null &&
                   m_Controller.TryStartAbility(
                       ability,
                       serverTime,
                       input.TargetEntityId,
                       input.SourcePosition,
                       input.TargetPosition,
                       input.Direction,
                       randomSeed,
                       participantCount);
        }

        public bool TrySetPhaseServer(BossPhaseDefinition phase, double serverTime)
        {
            if (!m_Initialized || m_Controller == null || phase == null) return false;
            m_Controller.LoadPhase(phase, serverTime);
            return true;
        }

        public bool CancelActiveCastServer(double serverTime)
        {
            if (!m_Initialized || m_Controller == null || !m_Controller.HasActiveCast) return false;
            m_Controller.Cancel(serverTime);
            return true;
        }

        public bool TryParryActiveCastServer(double serverTime)
        {
            return m_Initialized && m_Controller != null && m_Controller.TryParry(serverTime);
        }

        public void ResetServer(double serverTime)
        {
            if (m_Controller != null)
            {
                m_Controller.Cancel(serverTime);
                m_Controller.Dispose();
            }
            m_Controller = null;
            m_Initialized = false;
        }

        private void OnDestroy()
        {
            ResetServer(0d);
        }

    }
}
