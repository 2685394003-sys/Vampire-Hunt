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
        [SerializeField] private GameEvent onActionReleased;
        [SerializeField] private CombatAbilityHost abilityHost;

        private CorePlayerManager m_PlayerManager;
        private bool m_CombatEnabled = true;
        private bool m_ActionHeld;

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

            onActionPressed.RegisterListener(HandleActionPressed);
            if (onActionReleased != null) onActionReleased.RegisterListener(HandleActionReleased);
        }

        public void OnPlayerDespawn()
        {
            if (m_PlayerManager != null && m_PlayerManager.IsOwner)
            {
                if (onActionPressed != null) onActionPressed.UnregisterListener(HandleActionPressed);
                if (onActionReleased != null) onActionReleased.UnregisterListener(HandleActionReleased);
            }
            m_ActionHeld = false;
        }

        public void OnLifeStateChanged(PlayerLifeState previousState, PlayerLifeState newState)
        {
            m_CombatEnabled = newState != PlayerLifeState.Eliminated;
        }

        private void Update()
        {
            // Hold-to-fire: while the action is held, keep requesting activation each frame.
            // The ability's cooldown gates the actual fire rate (auto-rifle fires fast, sniper slow).
            if (!m_ActionHeld || !IsSpawned || !IsOwner || !m_CombatEnabled) return;
            abilityHost?.TryActivate(slot);
        }

        private void HandleActionPressed()
        {
            m_ActionHeld = true;
            if (!IsSpawned || !IsOwner || !m_CombatEnabled) return;
            abilityHost?.TryActivate(slot);
        }

        private void HandleActionReleased()
        {
            m_ActionHeld = false;
        }
    }
}
