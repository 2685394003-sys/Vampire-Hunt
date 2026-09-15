using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Integration;

namespace VampireHunt.Player
{
    /// <summary>Owner-only input adapter. Ability rules and networking live outside this addon.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class CombatAbilityInputAddon : NetworkBehaviour, IPlayerAddon
    {
        [SerializeField] private AbilitySlot slot = AbilitySlot.Primary;
        [SerializeField] private GameEvent onActionPressed;
        [SerializeField] private CombatAbilityHost abilityHost;

        private CorePlayerManager m_PlayerManager;
        private bool m_CombatEnabled = true;

        public void Initialize(CorePlayerManager playerManager)
        {
            m_PlayerManager = playerManager;
            if (abilityHost == null) abilityHost = GetComponent<CombatAbilityHost>();
            abilityHost?.Initialize(playerManager);
        }

        public void OnPlayerSpawn()
        {
            abilityHost?.ResetAbilities();
            if (m_PlayerManager == null || !m_PlayerManager.IsOwner) return;

            if (onActionPressed == null)
            {
                Debug.LogWarning("[CombatAbilityInputAddon] Action event is not assigned.", this);
                return;
            }

            onActionPressed.RegisterListener(HandleAction);
        }

        public void OnPlayerDespawn()
        {
            if (m_PlayerManager != null && m_PlayerManager.IsOwner && onActionPressed != null)
            {
                onActionPressed.UnregisterListener(HandleAction);
            }
        }

        public void OnLifeStateChanged(PlayerLifeState previousState, PlayerLifeState newState)
        {
            m_CombatEnabled = newState != PlayerLifeState.Eliminated;
        }

        private void HandleAction()
        {
            if (!IsSpawned || !IsOwner || !m_CombatEnabled) return;
            abilityHost?.TryActivate(slot);
        }
    }
}
