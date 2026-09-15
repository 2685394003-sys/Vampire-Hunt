using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Infrastructure.Netcode;

namespace VampireHunt.Infrastructure.Integration
{
    /// <summary>Presentation/control adapter that freezes only the owning player's movement.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerStatusControlAdapter : NetworkBehaviour
    {
        [SerializeField] private CoreMovement coreMovement;
        [SerializeField] private CombatStatusHost statusHost;

        private bool m_WasBlocked;
        private bool m_PreviousMovementEnabled = true;

        private void Awake()
        {
            if (coreMovement == null) coreMovement = GetComponent<CoreMovement>();
            if (statusHost == null) statusHost = GetComponent<CombatStatusHost>();
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner || coreMovement == null || statusHost == null) return;
            bool blocked = statusHost.IsMovementBlocked;
            if (blocked == m_WasBlocked) return;

            if (blocked)
            {
                m_PreviousMovementEnabled = coreMovement.IsMovementEnabled;
                coreMovement.IsMovementEnabled = false;
                coreMovement.ResetMovementForces();
            }
            else
            {
                coreMovement.IsMovementEnabled = m_PreviousMovementEnabled;
            }
            m_WasBlocked = blocked;
        }

        public override void OnNetworkDespawn()
        {
            if (m_WasBlocked && IsOwner && coreMovement != null)
                coreMovement.IsMovementEnabled = m_PreviousMovementEnabled;
            m_WasBlocked = false;
            base.OnNetworkDespawn();
        }
    }
}
