using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace VampireHunt.Player
{
    /// <summary>Owner-only input source for manually requesting a level up.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class LevelUpInputHandler : NetworkBehaviour
    {
        [Header("Progression Game Events")]
        [Tooltip("Raised when the level-up button is pressed.")]
        [SerializeField] private GameEvent onTriggerLevelup;

        private GameplayInputSystem_Actions m_InputActions;

        private void Awake()
        {
            m_InputActions = new GameplayInputSystem_Actions();
        }

        public override void OnNetworkSpawn()
        {
            if (IsOwner && m_InputActions != null)
            {
                RegisterInputActions();
                m_InputActions.Player.Enable();
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner && m_InputActions != null)
            {
                m_InputActions.Player.Disable();
                UnregisterInputActions();
            }
        }

        private void RegisterInputActions()
        {
            m_InputActions.Player.TriggerLevelup.performed += HandleTriggerLevelup;
        }

        private void UnregisterInputActions()
        {
            m_InputActions.Player.TriggerLevelup.performed -= HandleTriggerLevelup;
        }

        private void HandleTriggerLevelup(InputAction.CallbackContext context) =>
            onTriggerLevelup?.Raise();
    }
}
