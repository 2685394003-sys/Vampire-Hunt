using System.Collections;
using Unity.Netcode;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Owner gathers intent; server simulates movement, dash, knockback and attacks.
/// The server never reads a player's camera or input devices.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(PlayerNetworkState))]
public sealed class PlayerController : NetworkBehaviour
{
    [Header("输入绑定 / Input Bindings")]
    public InputAction moveAction;
    public InputAction attackAction;

    [Header("组件引用 / Component References")]
    public Animator anim;
    public PlayerAttact playerAttack;

    [Header("服务器移动 / Server Movement")]
    [SerializeField, Min(1f)] private float inputSendRate = 20f;
    [SerializeField, Min(0.1f)] private float inputTimeout = 0.5f;
    [SerializeField, Min(1f)] private float dashSpeedMultiplier = 2f;

    [Header("状态 / State")]
    public bool isKnockedBack;
    public Vector3 knockbackVelocity;
    public Vector3 facingDirection = Vector3.forward;

    private PlayerNetworkState playerState;
    private Rigidbody body;
    private Camera viewCamera;
    private Vector3 localMoveWorld;
    private Vector3 serverMoveWorld;
    private Vector3 serverDashDirection;
    private float serverDashRemaining;
    private float nextInputSendTime;
    private float lastServerInputTime;
    private float nextServerAttackTime;
    private int attackSequence;
    private Coroutine knockbackCoroutine;
    private bool inputEnabled;

    private void Awake()
    {
        playerState = PlayerNetworkState.EnsureForMigration(gameObject);
        body = GetComponent<Rigidbody>();
        viewCamera = Camera.main;
        if (playerAttack == null)
        {
            playerAttack = GetComponent<PlayerAttact>();
        }
    }

    private void OnEnable()
    {
        RefreshLocalInputState();
    }

    private void OnDisable()
    {
        DisableLocalInput();
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer && body != null) body.isKinematic = true;
        RefreshLocalInputState();
        if (IsOwner)
        {
            BindLocalPresentation();
        }
    }

    public override void OnNetworkDespawn()
    {
        DisableLocalInput();
    }

    private void Update()
    {
        if (!NetworkAuthority.IsOwnerOrOffline(this) || playerState == null || !playerState.IsAlive)
        {
            return;
        }

        Vector2 rawInput = moveAction.ReadValue<Vector2>();
        localMoveWorld = ConvertInputToWorld(rawInput);

        if (!NetworkAuthority.IsNetworkActive)
        {
            serverMoveWorld = localMoveWorld;
            lastServerInputTime = Time.unscaledTime;
            return;
        }

        if (Time.unscaledTime >= nextInputSendTime)
        {
            nextInputSendTime = Time.unscaledTime + 1f / Mathf.Max(1f, inputSendRate);
            SubmitMoveInputRpc(localMoveWorld);
        }
    }

    private void FixedUpdate()
    {
        if (!NetworkAuthority.IsServerOrOffline(this) || playerState == null || !playerState.IsAlive)
        {
            return;
        }

        if (NetworkAuthority.IsNetworkActive && Time.unscaledTime - lastServerInputTime > inputTimeout)
        {
            serverMoveWorld = Vector3.zero;
        }

        if (isKnockedBack)
        {
            MoveAuthoritatively(knockbackVelocity, Time.fixedDeltaTime);
            return;
        }

        Vector3 velocity;
        if (serverDashRemaining > 0f)
        {
            serverDashRemaining = Mathf.Max(0f, serverDashRemaining - Time.fixedDeltaTime);
            velocity = serverDashDirection * playerState.MoveSpeed * dashSpeedMultiplier;
            if (serverDashRemaining <= 0f)
            {
                playerState.SetStaminaRecoveryPaused(false);
            }
        }
        else
        {
            velocity = serverMoveWorld * playerState.MoveSpeed;
        }

        MoveAuthoritatively(velocity, Time.fixedDeltaTime);
        UpdateFacingAndAnimation(serverMoveWorld);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void SubmitMoveInputRpc(Vector3 worldDirection)
    {
        AcceptMoveInput(worldDirection);
    }

    private void AcceptMoveInput(Vector3 worldDirection)
    {
        worldDirection.y = 0f;
        serverMoveWorld = Vector3.ClampMagnitude(worldDirection, 1f);
        lastServerInputTime = Time.unscaledTime;
    }

    public void RequestAttack()
    {
        if (!NetworkAuthority.IsOwnerOrOffline(this) || playerState == null || !playerState.IsAlive)
        {
            return;
        }

        if (NetworkAuthority.IsNetworkActive)
        {
            RequestAttackRpc();
        }
        else
        {
            ServerTryStartAttack();
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestAttackRpc()
    {
        ServerTryStartAttack();
    }

    private void ServerTryStartAttack()
    {
        if (!NetworkAuthority.IsServerOrOffline(this) ||
            playerState == null ||
            !playerState.IsAlive ||
            isKnockedBack ||
            Time.time < nextServerAttackTime)
        {
            return;
        }

        nextServerAttackTime = Time.time + playerState.AttackCooldown;
        attackSequence++;
        playerAttack?.ServerBeginAttack(attackSequence);

        if (NetworkAuthority.IsNetworkActive)
        {
            PlayAttackPresentationRpc(attackSequence);
        }
        else
        {
            playerAttack?.PlayAttackPresentation();
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void PlayAttackPresentationRpc(int sequence)
    {
        playerAttack?.PlayAttackPresentation();
    }

    public bool ServerTryStartDash(Vector3 worldDirection, float duration)
    {
        if (!NetworkAuthority.IsServerOrOffline(this) ||
            playerState == null ||
            !playerState.IsAlive ||
            isKnockedBack ||
            serverDashRemaining > 0f)
        {
            return false;
        }

        worldDirection.y = 0f;
        if (worldDirection.sqrMagnitude < 0.01f)
        {
            worldDirection = facingDirection;
        }

        serverDashDirection = worldDirection.normalized;
        serverDashRemaining = Mathf.Max(0f, duration);
        playerState.SetStaminaRecoveryPaused(serverDashRemaining > 0f);
        return serverDashRemaining > 0f;
    }

    public void RequestDash(Vector3 worldDirection, float duration)
    {
        if (!NetworkAuthority.IsOwnerOrOffline(this))
        {
            return;
        }

        if (NetworkAuthority.IsNetworkActive)
        {
            RequestDashRpc(worldDirection, duration);
        }
        else
        {
            ServerTryStartDash(worldDirection, duration);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestDashRpc(Vector3 worldDirection, float duration)
    {
        ServerTryStartDash(worldDirection, Mathf.Clamp(duration, 0f, 1f));
    }

    public void Knockback(Transform enemy, float force, float stunTime)
    {
        if (!NetworkAuthority.IsServerOrOffline(this) || !gameObject.activeSelf || enemy == null)
        {
            return;
        }

        isKnockedBack = true;
        serverDashRemaining = 0f;
        playerState?.SetStaminaRecoveryPaused(false);
        Vector3 direction = Vector3.ProjectOnPlane(transform.position - enemy.position, Vector3.up).normalized;
        knockbackVelocity = direction * Mathf.Max(0f, force);

        if (knockbackCoroutine != null)
        {
            StopCoroutine(knockbackCoroutine);
        }
        knockbackCoroutine = StartCoroutine(KnockbackCounter(stunTime));
    }

    private IEnumerator KnockbackCounter(float stunTime)
    {
        yield return new WaitForSeconds(Mathf.Max(0f, stunTime));
        knockbackVelocity = Vector3.zero;
        isKnockedBack = false;
        knockbackCoroutine = null;
    }

    public void AttackTrigger(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            RequestAttack();
        }
    }

    /// <summary>
    /// Applies root motion delta from the visual animator to the authoritative root.
    /// Called by PlayerRootMotionDriver during OnAnimatorMove.
    /// </summary>
    public void ApplyAnimatorRootMotion(Vector3 deltaPosition)
    {
        if (!NetworkAuthority.IsOwnerOrOffline(this) || playerState == null || !playerState.IsAlive || isKnockedBack)
        {
            return;
        }

        Vector3 nextPosition = transform.position + deltaPosition;
        if (body != null && !body.isKinematic)
        {
            body.MovePosition(nextPosition);
        }
        else
        {
            transform.position = nextPosition;
        }
    }

    public Vector3 GetLocalDesiredMoveWorld()
    {
        return localMoveWorld;
    }

    private Vector3 ConvertInputToWorld(Vector2 input)
    {
        if (viewCamera == null)
        {
            viewCamera = Camera.main;
        }

        if (viewCamera == null)
        {
            return Vector3.ClampMagnitude(new Vector3(input.x, 0f, input.y), 1f);
        }

        Vector3 cameraForward = Vector3.ProjectOnPlane(viewCamera.transform.forward, Vector3.up).normalized;
        Vector3 cameraRight = Vector3.ProjectOnPlane(viewCamera.transform.right, Vector3.up).normalized;
        return Vector3.ClampMagnitude(cameraForward * input.y + cameraRight * input.x, 1f);
    }

    private void MoveAuthoritatively(Vector3 velocity, float deltaTime)
    {
        Vector3 nextPosition = transform.position + velocity * deltaTime;
        if (body != null && !body.isKinematic)
        {
            body.MovePosition(nextPosition);
        }
        else
        {
            transform.position = nextPosition;
        }
    }

    private void UpdateFacingAndAnimation(Vector3 moveDirection)
    {
        if (moveDirection.x > 0.01f && transform.localScale.x < 0f ||
            moveDirection.x < -0.01f && transform.localScale.x > 0f)
        {
            facingDirection *= -1f;
            Vector3 scale = transform.localScale;
            scale.x *= -1f;
            transform.localScale = scale;
        }

        if (anim != null)
        {
            anim.SetFloat("horizontal", moveDirection.x);
            anim.SetFloat("vertical", moveDirection.z);
        }
    }

    private void RefreshLocalInputState()
    {
        if (!isActiveAndEnabled || !NetworkAuthority.IsOwnerOrOffline(this))
        {
            DisableLocalInput();
            return;
        }

        if (inputEnabled)
        {
            return;
        }

        moveAction.Enable();
        attackAction.Enable();
        attackAction.performed += AttackTrigger;
        inputEnabled = true;
    }

    private void DisableLocalInput()
    {
        if (!inputEnabled)
        {
            return;
        }

        attackAction.performed -= AttackTrigger;
        moveAction.Disable();
        attackAction.Disable();
        inputEnabled = false;
    }

    private void BindLocalPresentation()
    {
        viewCamera = Camera.main;
        TopDownCamera topDownCamera = FindFirstObjectByType<TopDownCamera>();
        topDownCamera?.SetTarget(transform);

        CinemachineCamera cinematicCamera = FindFirstObjectByType<CinemachineCamera>();
        if (cinematicCamera != null)
        {
            CameraTarget target = cinematicCamera.Target;
            target.TrackingTarget = transform;
            if (!target.CustomLookAtTarget) target.LookAtTarget = transform;
            cinematicCamera.Target = target;
        }
    }
}
