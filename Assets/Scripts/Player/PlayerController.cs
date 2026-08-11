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
    private static readonly int MoveParameter = Animator.StringToHash("move");

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
    [SerializeField, Min(0f)] private float turnSpeed = 720f;

    [Header("动画过渡 / Animation Blending")]
    [SerializeField, Min(0f)] private float moveDampTime = 0.1f;
    [SerializeField, Min(0f)] private float attackWindupDuration = 0.12f;

    [Header("根运动 / Root Motion")]
    [SerializeField, Min(0.01f)] private float authoredWalkSpeed = 1.43f;
    [SerializeField, Min(0.01f)] private float authoredRunSpeed = 3.95f;
    [SerializeField, Range(0f, 5f)] private float walkBlendThreshold = 2f;
    [SerializeField, Range(0f, 5f)] private float runBlendThreshold = 5f;
    [SerializeField, Min(0f)] private float attackRootMotionScale = 1f;

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
    private float animatorMoveValue;
    private int attackSequence;
    private Coroutine knockbackCoroutine;
    private Coroutine attackWindupCoroutine;
    private bool isAttackWindingUp;
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
        if (anim != null)
        {
            animatorMoveValue = anim.GetFloat(MoveParameter);
        }
    }

    private void OnEnable()
    {
        RefreshLocalInputState();
    }

    private void OnDisable()
    {
        CancelAttackWindup();
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
            UpdateFacingAndAnimation(Vector3.zero, Time.fixedDeltaTime);
            return;
        }

        Vector3 desiredVelocity;
        if (isAttackWindingUp ||
            (playerAttack != null && playerAttack.IsAttackAnimationPlaying))
        {
            // Wind-up decelerates through the smoothed move parameter. Once the
            // swing begins, attack translation is supplied by root motion.
            desiredVelocity = Vector3.zero;
        }
        else if (serverDashRemaining > 0f)
        {
            serverDashRemaining = Mathf.Max(0f, serverDashRemaining - Time.fixedDeltaTime);
            desiredVelocity = serverDashDirection * playerState.MoveSpeed * dashSpeedMultiplier;
            MoveAuthoritatively(desiredVelocity, Time.fixedDeltaTime);
            if (serverDashRemaining <= 0f)
            {
                playerState.SetStaminaRecoveryPaused(false);
            }
        }
        else
        {
            // Normal locomotion no longer translates the Rigidbody here. The
            // Animator supplies the actual displacement through root motion.
            desiredVelocity = serverMoveWorld * playerState.MoveSpeed;
        }

        UpdateFacingAndAnimation(desiredVelocity, Time.fixedDeltaTime);
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
            isAttackWindingUp ||
            (playerAttack != null && playerAttack.IsAttackAnimationPlaying) ||
            Time.time < nextServerAttackTime)
        {
            return;
        }

        nextServerAttackTime = Time.time + playerState.AttackCooldown;
        serverDashRemaining = 0f;
        playerState.SetStaminaRecoveryPaused(false);

        isAttackWindingUp = true;
        if (attackWindupCoroutine != null)
        {
            StopCoroutine(attackWindupCoroutine);
        }
        attackWindupCoroutine = StartCoroutine(ServerAttackAfterWindup());
    }

    private IEnumerator ServerAttackAfterWindup()
    {
        if (attackWindupDuration > 0f)
        {
            yield return new WaitForSeconds(attackWindupDuration);
        }

        attackWindupCoroutine = null;
        if (!NetworkAuthority.IsServerOrOffline(this) ||
            playerState == null ||
            !playerState.IsAlive ||
            isKnockedBack)
        {
            isAttackWindingUp = false;
            yield break;
        }

        // Guarantee that the Animator enters Attack from a stationary blend
        // state even when the last damped fraction has not quite reached zero.
        SetMoveAnimatorImmediate(0f);
        isAttackWindingUp = false;
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
            isAttackWindingUp ||
            (playerAttack != null && playerAttack.IsAttackAnimationPlaying) ||
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
        CancelAttackWindup();
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

    private void CancelAttackWindup()
    {
        if (attackWindupCoroutine != null)
        {
            StopCoroutine(attackWindupCoroutine);
            attackWindupCoroutine = null;
        }
        isAttackWindingUp = false;
    }

    public void AttackTrigger(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            RequestAttack();
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

    /// <summary>
    /// Called by PlayerRootMotionDriver on the yin2 Animator object. Only the
    /// server applies the delta; NetworkTransform distributes the result.
    /// </summary>
    public void ApplyAnimatorRootMotion(Vector3 deltaPosition)
    {
        if (!NetworkAuthority.IsServerOrOffline(this) ||
            playerState == null ||
            !playerState.IsAlive ||
            isKnockedBack ||
            serverDashRemaining > 0f)
        {
            return;
        }

        bool isAttacking = playerAttack != null && playerAttack.IsAttackAnimationPlaying;
        Vector3 planarDelta = Vector3.ProjectOnPlane(deltaPosition, Vector3.up);

        if (isAttacking)
        {
            MoveByRootMotionDelta(planarDelta * attackRootMotionScale);
            return;
        }

        // Drive displacement from the same damped parameter as the blend tree.
        // This lets root motion coast naturally while move approaches zero.
        float animatedSpeed = anim != null
            ? Mathf.Clamp(anim.GetFloat(MoveParameter), 0f, runBlendThreshold)
            : (isAttackWindingUp ? 0f : serverMoveWorld.magnitude * playerState.MoveSpeed);
        if (animatedSpeed <= 0.001f || planarDelta.sqrMagnitude <= 0.0000001f)
        {
            return;
        }

        MoveByRootMotionDelta(planarDelta * CalculateLocomotionRootMotionScale(animatedSpeed));
    }

    private float CalculateLocomotionRootMotionScale(float desiredSpeed)
    {
        float parameterSpeed = Mathf.Clamp(desiredSpeed, 0f, runBlendThreshold);
        float authoredSpeed;
        if (parameterSpeed <= walkBlendThreshold || runBlendThreshold <= walkBlendThreshold)
        {
            // The blend tree now interpolates Idle(0) -> Walk(2). Account for
            // that authored interpolation once; applying parameterSpeed again
            // would make low-speed root motion fade quadratically.
            float walkBlend = walkBlendThreshold <= 0.001f
                ? 1f
                : Mathf.InverseLerp(0f, walkBlendThreshold, parameterSpeed);
            authoredSpeed = Mathf.Lerp(0f, authoredWalkSpeed, walkBlend);
        }
        else
        {
            float blend = Mathf.InverseLerp(walkBlendThreshold, runBlendThreshold, parameterSpeed);
            authoredSpeed = Mathf.Lerp(authoredWalkSpeed, authoredRunSpeed, blend);
        }

        return parameterSpeed / Mathf.Max(0.01f, authoredSpeed);
    }

    private void MoveByRootMotionDelta(Vector3 delta)
    {
        if (delta.sqrMagnitude <= 0.0000001f)
        {
            return;
        }

        Vector3 nextPosition = transform.position + delta;
        if (body != null && !body.isKinematic)
        {
            body.MovePosition(nextPosition);
        }
        else
        {
            transform.position = nextPosition;
        }
    }

    private void UpdateFacingAndAnimation(Vector3 velocity, float deltaTime)
    {
        Vector3 planarVelocity = Vector3.ProjectOnPlane(velocity, Vector3.up);
        if (planarVelocity.sqrMagnitude > 0.0001f)
        {
            facingDirection = planarVelocity.normalized;
            Quaternion targetRotation = Quaternion.LookRotation(facingDirection, Vector3.up);
            Quaternion nextRotation = turnSpeed <= 0f
                ? targetRotation
                : Quaternion.RotateTowards(transform.rotation, targetRotation, turnSpeed * deltaTime);

            if (body != null && !body.isKinematic)
            {
                body.MoveRotation(nextRotation);
            }
            else
            {
                transform.rotation = nextRotation;
            }
        }

        if (anim != null)
        {
            // The yin2 blend tree is authored for a 0..5 speed parameter.
            float targetMove = Mathf.Clamp(planarVelocity.magnitude, 0f, 5f);
            if (isAttackWindingUp)
            {
                float stopSpeed = attackWindupDuration <= 0f
                    ? 5f
                    : 5f / attackWindupDuration;
                animatorMoveValue = Mathf.MoveTowards(
                    animatorMoveValue,
                    0f,
                    stopSpeed * deltaTime);
                anim.SetFloat(MoveParameter, animatorMoveValue);
            }
            else if (moveDampTime > 0f)
            {
                anim.SetFloat(MoveParameter, targetMove, moveDampTime, deltaTime);
                animatorMoveValue = anim.GetFloat(MoveParameter);
            }
            else
            {
                SetMoveAnimatorImmediate(targetMove);
            }
        }
    }

    private void SetMoveAnimatorImmediate(float value)
    {
        animatorMoveValue = Mathf.Clamp(value, 0f, 5f);
        anim?.SetFloat(MoveParameter, animatorMoveValue);
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
