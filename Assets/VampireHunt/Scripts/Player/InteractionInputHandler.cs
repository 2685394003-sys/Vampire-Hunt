using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace VampireHunt.Player
{
    /// <summary>Owner-only input source for requesting an interaction.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class InteractionInputHandler : NetworkBehaviour
    {
        [Header("Interaction Game Events")]
        [Tooltip("Raised when the interact button is pressed.")]
        [SerializeField] private GameEvent onInteractPressed;

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
            m_InputActions.Player.Interact.performed += HandleInteractPressed;
        }

        private void UnregisterInputActions()
        {
            m_InputActions.Player.Interact.performed -= HandleInteractPressed;
        }

        private void HandleInteractPressed(InputAction.CallbackContext context) =>
            onInteractPressed?.Raise();
    }
}
