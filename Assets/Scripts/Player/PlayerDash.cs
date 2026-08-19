using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using VampireHunt.Player.Contracts;

/// <summary>
/// Legacy Player Prefab dash input adapter. The application/domain validates
/// the command and owns stamina/cooldown policy; this component only executes
/// the accepted local motor movement.
/// </summary>
[RequireComponent(typeof(PlayerController))]
[RequireComponent(typeof(PlayerNetworkState))]
public sealed class PlayerDash : NetworkBehaviour
{
    // Serialized names are kept for existing prefab bindings.
    public InputAction shiftAction;
    public InputAction moveAction;

    private PlayerController playerController;
    private PlayerNetworkState playerState;
    private bool inputEnabled;

    private void Awake()
    {
        playerController = GetComponent<PlayerController>();
        playerState = PlayerNetworkState.EnsureForMigration(gameObject);
    }

    private void OnEnable() => RefreshInputState();

    private void OnDisable() => DisableInput();

    public override void OnNetworkSpawn() => RefreshInputState();

    public override void OnNetworkDespawn() => DisableInput();

    public void OnShiftPressed(InputAction.CallbackContext context)
    {
        if (!context.performed ||
            !NetworkAuthority.IsOwnerOrOffline(this) ||
            playerController == null ||
            playerState == null ||
            !playerState.IsAlive)
            return;

        SubmitDashIntent(playerController.GetLocalDesiredMoveWorld());
    }

    /// <summary>Animation/input compatibility entry; submits intent only.</summary>
    [System.Obsolete("Dash validation moved to PlayerMobilityState/PlayerCommandService.")]
    public void RequestDash(Vector3 desiredDirection) => SubmitDashIntent(desiredDirection);

    private void SubmitDashIntent(Vector3 desiredDirection)
    {
        if (playerState == null || !NetworkAuthority.IsOwnerOrOffline(this)) return;

        CommandResult result = playerState.RequestDashIntent(desiredDirection);
        if (result.Accepted)
            playerController?.StartOwnerDash(desiredDirection);
    }

    private void RefreshInputState()
    {
        if (!isActiveAndEnabled || !NetworkAuthority.IsOwnerOrOffline(this))
        {
            DisableInput();
            return;
        }

        if (inputEnabled) return;
        shiftAction.Enable();
        moveAction.Enable();
        shiftAction.performed += OnShiftPressed;
        inputEnabled = true;
    }

    private void DisableInput()
    {
        if (!inputEnabled) return;
        shiftAction.performed -= OnShiftPressed;
        shiftAction.Disable();
        moveAction.Disable();
        inputEnabled = false;
    }
}
