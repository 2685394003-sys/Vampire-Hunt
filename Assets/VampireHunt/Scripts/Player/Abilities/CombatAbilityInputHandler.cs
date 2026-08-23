using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace VampireHunt.Player
{
    /// <summary>
    /// Owner-only input source for combat ability buttons. This component only
    /// translates Input System actions into local presentation-layer events;
    /// ability rules and network execution remain in the ability adapters.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class CombatAbilityInputHandler : NetworkBehaviour
    {
        [Header("Combat Ability Events")]
        [Tooltip("Raised when combat ability button 1 is pressed.")]
        [SerializeField] private GameEvent onCombatAbility1Pressed;
        [Tooltip("Raised when combat ability button 2 is pressed.")]
        [SerializeField] private GameEvent onCombatAbility2Pressed;
        [Tooltip("Raised when combat ability button 3 is pressed.")]
        [SerializeField] private GameEvent onCombatAbility3Pressed;
        [Tooltip("Raised when combat ability button 4 is pressed.")]
        [SerializeField] private GameEvent onCombatAbility4Pressed;

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
            m_InputActions.Player.CombatAbility1.performed += HandleCombatAbility1Pressed;
            m_InputActions.Player.CombatAbility2.performed += HandleCombatAbility2Pressed;
            m_InputActions.Player.CombatAbility3.performed += HandleCombatAbility3Pressed;
            m_InputActions.Player.CombatAbility4.performed += HandleCombatAbility4Pressed;
        }

        private void UnregisterInputActions()
        {
            m_InputActions.Player.CombatAbility1.performed -= HandleCombatAbility1Pressed;
            m_InputActions.Player.CombatAbility2.performed -= HandleCombatAbility2Pressed;
            m_InputActions.Player.CombatAbility3.performed -= HandleCombatAbility3Pressed;
            m_InputActions.Player.CombatAbility4.performed -= HandleCombatAbility4Pressed;
        }

        private void HandleCombatAbility1Pressed(InputAction.CallbackContext context) =>
            onCombatAbility1Pressed?.Raise();

        private void HandleCombatAbility2Pressed(InputAction.CallbackContext context) =>
            onCombatAbility2Pressed?.Raise();

        private void HandleCombatAbility3Pressed(InputAction.CallbackContext context) =>
            onCombatAbility3Pressed?.Raise();

        private void HandleCombatAbility4Pressed(InputAction.CallbackContext context) =>
            onCombatAbility4Pressed?.Raise();
    }
}
