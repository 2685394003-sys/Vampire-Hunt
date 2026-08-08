using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Owner-side dash input adapter. Stamina validation and movement are performed
/// by PlayerNetworkState and PlayerController on the server.
/// </summary>
[RequireComponent(typeof(PlayerController))]
[RequireComponent(typeof(PlayerNetworkState))]
public sealed class PlayerDash : NetworkBehaviour
{
    [Header("冲刺设置 / Dash Settings")]
    [Min(0f)] public float dashDuration = 0.15f;
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

    private void OnEnable()
    {
        RefreshInputState();
    }

    private void OnDisable()
    {
        DisableInput();
    }

    public override void OnNetworkSpawn()
    {
        RefreshInputState();
    }

    public override void OnNetworkDespawn()
    {
        DisableInput();
    }

    public void OnShiftPressed(InputAction.CallbackContext context)
    {
        if (!context.performed ||
            !NetworkAuthority.IsOwnerOrOffline(this) ||
            playerController == null ||
            playerState == null ||
            !playerState.IsAlive)
        {
            return;
        }

        Vector3 desiredDirection = playerController.GetLocalDesiredMoveWorld();
        RequestDash(desiredDirection);
    }

    private void RequestDash(Vector3 desiredDirection)
    {
        if (NetworkAuthority.IsNetworkActive)
        {
            RequestDashRpc(desiredDirection);
        }
        else
        {
            ServerTryDash(desiredDirection);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestDashRpc(Vector3 desiredDirection)
    {
        ServerTryDash(desiredDirection);
    }

    private void ServerTryDash(Vector3 desiredDirection)
    {
        if (!NetworkAuthority.IsServerOrOffline(this) || playerState == null)
        {
            return;
        }

        StatsManager config = playerState.Balance;
        float cost = config != null ? config.dashStaminaCost : 0f;
        if (!playerState.TryConsumeStamina(cost))
        {
            return;
        }

        if (!playerController.ServerTryStartDash(desiredDirection, dashDuration))
        {
            // Dash was rejected after stamina validation; refund on the server.
            playerState.RestoreStamina(cost);
        }
    }

    private void RefreshInputState()
    {
        if (!isActiveAndEnabled || !NetworkAuthority.IsOwnerOrOffline(this))
        {
            DisableInput();
            return;
        }

        if (inputEnabled)
        {
            return;
        }

        shiftAction.Enable();
        moveAction.Enable();
        shiftAction.performed += OnShiftPressed;
        inputEnabled = true;
    }

    private void DisableInput()
    {
        if (!inputEnabled)
        {
            return;
        }

        shiftAction.performed -= OnShiftPressed;
        shiftAction.Disable();
        moveAction.Disable();
        inputEnabled = false;
    }
}
