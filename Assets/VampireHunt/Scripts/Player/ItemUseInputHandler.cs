using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using VampireHunt.Infrastructure.Unity;

namespace VampireHunt.Player
{
    /// <summary>Owner-only input source that translates item-slot actions into a local event.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class ItemUseInputHandler : NetworkBehaviour
    {
        [Header("Item Use Events")]
        [Tooltip("Raised with the zero-based usable-item slot index requested by the owner.")]
        [SerializeField] private ItemSlotUseEvent onUseItemSlotRequested;

        private Blocks.Gameplay.Core.GameplayInputSystem_Actions m_InputActions;

        private void Awake()
        {
            m_InputActions = new Blocks.Gameplay.Core.GameplayInputSystem_Actions();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsOwner || m_InputActions == null) return;
            RegisterInputActions();
            m_InputActions.Player.Enable();
        }

        public override void OnNetworkDespawn()
        {
            if (IsOwner && m_InputActions != null)
            {
                m_InputActions.Player.Disable();
                UnregisterInputActions();
            }
            base.OnNetworkDespawn();
        }

        private void RegisterInputActions()
        {
            m_InputActions.Player.UseItem1.performed += HandleUseItem1;
            m_InputActions.Player.UseItem2.performed += HandleUseItem2;
            m_InputActions.Player.UseItem3.performed += HandleUseItem3;
            m_InputActions.Player.UseItem4.performed += HandleUseItem4;
        }

        private void UnregisterInputActions()
        {
            m_InputActions.Player.UseItem1.performed -= HandleUseItem1;
            m_InputActions.Player.UseItem2.performed -= HandleUseItem2;
            m_InputActions.Player.UseItem3.performed -= HandleUseItem3;
            m_InputActions.Player.UseItem4.performed -= HandleUseItem4;
        }

        private void HandleUseItem1(InputAction.CallbackContext context) => RaiseSlot(0);
        private void HandleUseItem2(InputAction.CallbackContext context) => RaiseSlot(1);
        private void HandleUseItem3(InputAction.CallbackContext context) => RaiseSlot(2);
        private void HandleUseItem4(InputAction.CallbackContext context) => RaiseSlot(3);

        private void RaiseSlot(int slotIndex) => onUseItemSlotRequested?.Raise(slotIndex);
    }
}
