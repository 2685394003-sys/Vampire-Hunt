using System;
using System.Collections;
using Unity.Netcode;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using VampireHunt.Player.Contracts;

/// <summary>
/// Legacy Player Prefab input/movement adapter. Attack and Dash are submitted as
/// Commands; this component never owns cooldown, stamina, damage or health rules.
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

    [Header("服务器移动 / Owner Movement Adapter")]
    [SerializeField, Min(1f)] private float inputSendRate = 20f;
    [SerializeField, Min(0.1f)] private float inputTimeout = 0.5f;
    [SerializeField, Min(0f)] private float turnSpeed = 720f;

    [Header("动画过渡 / Animation Blending")]
    [SerializeField, Min(0f)] private float moveDampTime = 0.2f;
    [SerializeField, Min(0f)] private float attackWindupDuration = 0.18f;

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
    private Vector3 localAimWorld = Vector3.forward;
    private Vector3 lastMoveDirection = Vector3.forward;
    private Vector3 ownerDashDirection;
    private float ownerDashRemaining;
    private float animatorMoveValue;
    private Coroutine knockbackCoroutine;
    private bool inputEnabled;

    private void Awake()
    {
        playerState = PlayerNetworkState.EnsureForMigration(gameObject);
        body = GetComponent<Rigidbody>();
        viewCamera = Camera.main;
        if (playerAttack == null) playerAttack = GetComponent<PlayerAttact>();
        if (anim != null) animatorMoveValue = anim.GetFloat(MoveParameter);
    }

    private void OnEnable() => RefreshLocalInputState();

    private void OnDisable()
    {
        DisableLocalInput();
        if (knockbackCoroutine != null) StopCoroutine(knockbackCoroutine);
        knockbackCoroutine = null;
        isKnockedBack = false;
    }

    public override void OnNetworkSpawn()
    {
        RefreshLocalInputState();
        if (IsOwner) BindLocalPresentation();
    }

    public override void OnNetworkDespawn() => DisableLocalInput();

    private void Update()
    {
        UpdateMoveAnimation(Time.deltaTime);
        if (!NetworkAuthority.IsOwnerOrOffline(this) || playerState == null || !playerState.IsAlive)
            return;

        localMoveWorld = ConvertInputToWorld(moveAction.ReadValue<Vector2>());
        localAimWorld = GetPointerAimDirection();
        if (localMoveWorld.sqrMagnitude > 0.0001f) lastMoveDirection = localMoveWorld.normalized;
    }

    private void FixedUpdate()
    {
        if (!NetworkAuthority.IsOwnerOrOffline(this) || playerState == null || !playerState.IsAlive)
            return;

        if (isKnockedBack)
        {
            MoveOwner(knockbackVelocity, Time.fixedDeltaTime);
            UpdateFacingAndMoveTarget(Vector3.zero, Time.fixedDeltaTime);
            return;
        }

        Vector3 desiredVelocity;
        if (ownerDashRemaining > 0f)
        {
            ownerDashRemaining = Mathf.Max(0f, ownerDashRemaining - Time.fixedDeltaTime);
            desiredVelocity = ownerDashDirection * playerState.MoveSpeed * playerState.DashSpeedMultiplier;
        }
        else
        {
            desiredVelocity = localMoveWorld * playerState.MoveSpeed;
        }

        MoveOwner(desiredVelocity, Time.fixedDeltaTime);
        UpdateFacingAndMoveTarget(desiredVelocity, Time.fixedDeltaTime);
    }

    public void RequestAttack()
    {
        if (!NetworkAuthority.IsOwnerOrOffline(this) || playerState == null || !playerState.IsAlive) return;
        CommandResult result = playerState.RequestAttackIntent(transform.position + localAimWorld);
        if (result.Accepted) playerAttack?.PlayAttackPresentation();
    }

    /// <summary>AnimationEvent compatibility entry; still submits intent only.</summary>
    [Obsolete("Animation events cannot apply damage; use PlayerController.RequestAttack.")]
    public void SubmitAttackIntentFromAnimation() => RequestAttack();

    /// <summary>
    /// Owner movement adapter hook called by PlayerDash after server/application
    /// acceptance. It does not spend stamina or decide cooldowns.
    /// </summary>
    public bool StartOwnerDash(Vector3 worldDirection)
    {
        if (playerState == null || !NetworkAuthority.IsOwnerOrOffline(this) || !playerState.IsAlive)
            return false;
        Vector3 planar = Vector3.ProjectOnPlane(worldDirection, Vector3.up);
        if (planar.sqrMagnitude <= 0.0001f) planar = lastMoveDirection;
        if (planar.sqrMagnitude <= 0.0001f) planar = transform.forward;
        ownerDashDirection = planar.normalized;
        ownerDashRemaining = Mathf.Max(0f, playerState.DashDuration);
        return ownerDashRemaining > 0f;
    }

    [Obsolete("Dash validation moved to PlayerMobilityState/PlayerCommandService.")]
    public bool ServerTryStartDash(Vector3 worldDirection, float duration)
    {
        return StartOwnerDash(worldDirection);
    }

    [Obsolete("Use PlayerNetworkState.RequestDashIntent from the input adapter.")]
    public void RequestDash(Vector3 worldDirection, float duration)
    {
        if (playerState == null) return;
        CommandResult result = playerState.RequestDashIntent(worldDirection);
        if (result.Accepted) StartOwnerDash(worldDirection);
    }

    /// <summary>Presentation/motor execution only; knockback is decided by Combat.</summary>
    public void Knockback(Transform enemy, float force, float stunTime)
    {
        if (enemy == null || !NetworkAuthority.IsOwnerOrOffline(this)) return;
        Vector3 direction = Vector3.ProjectOnPlane(transform.position - enemy.position, Vector3.up);
        if (direction.sqrMagnitude <= 0.0001f) direction = -transform.forward;
        knockbackVelocity = direction.normalized * Mathf.Max(0f, force);
        isKnockedBack = true;
        ownerDashRemaining = 0f;
        if (knockbackCoroutine != null) StopCoroutine(knockbackCoroutine);
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
        if (context.performed) RequestAttack();
    }

    public Vector3 GetLocalDesiredMoveWorld() => localMoveWorld;

    private Vector3 ConvertInputToWorld(Vector2 input)
    {
        if (viewCamera == null) viewCamera = Camera.main;
        if (viewCamera == null) return Vector3.ClampMagnitude(new Vector3(input.x, 0f, input.y), 1f);
        Vector3 forward = Vector3.ProjectOnPlane(viewCamera.transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(viewCamera.transform.right, Vector3.up).normalized;
        return Vector3.ClampMagnitude(forward * input.y + right * input.x, 1f);
    }

    private Vector3 GetPointerAimDirection()
    {
        if (viewCamera == null) viewCamera = Camera.main;
        if (viewCamera == null || Mouse.current == null)
            return localAimWorld.sqrMagnitude > 0.0001f ? localAimWorld : facingDirection;

        Ray ray = viewCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
        Plane plane = new(Vector3.up, transform.position);
        if (!plane.Raycast(ray, out float distance)) return localAimWorld;
        Vector3 direction = ray.GetPoint(distance) - transform.position;
        direction.y = 0f;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : localAimWorld;
    }

    private void MoveOwner(Vector3 velocity, float deltaTime)
    {
        Vector3 next = transform.position + velocity * deltaTime;
        if (body != null && !body.isKinematic) body.MovePosition(next);
        else transform.position = next;
    }

    /// <summary>Root-motion adapter; authoritative gameplay still comes from Commands.</summary>
    public void ApplyAnimatorRootMotion(Vector3 deltaPosition)
    {
        if (!NetworkAuthority.IsOwnerOrOffline(this) || playerState == null || !playerState.IsAlive ||
            isKnockedBack || ownerDashRemaining > 0f) return;
        if (deltaPosition.sqrMagnitude <= 0.0000001f) return;
        float speed = anim != null ? Mathf.Clamp(anim.GetFloat(MoveParameter), 0f, runBlendThreshold) : 0f;
        if (speed <= 0.001f) return;
        Vector3 direction = localMoveWorld.sqrMagnitude > 0.0001f ? localMoveWorld.normalized : lastMoveDirection;
        float authoredSpeed = speed <= walkBlendThreshold
            ? Mathf.Lerp(0f, authoredWalkSpeed, walkBlendThreshold <= 0.001f ? 1f : speed / walkBlendThreshold)
            : Mathf.Lerp(authoredWalkSpeed, authoredRunSpeed, Mathf.InverseLerp(walkBlendThreshold, runBlendThreshold, speed));
        MoveOwner(
            direction * (deltaPosition.magnitude * speed * attackRootMotionScale /
                         Mathf.Max(0.01f, authoredSpeed)),
            1f);
    }

    private void UpdateFacingAndMoveTarget(Vector3 velocity, float deltaTime)
    {
        Vector3 planar = Vector3.ProjectOnPlane(velocity, Vector3.up);
        if (localAimWorld.sqrMagnitude > 0.0001f)
        {
            facingDirection = localAimWorld.normalized;
            Quaternion target = Quaternion.LookRotation(facingDirection, Vector3.up);
            Quaternion next = turnSpeed <= 0f ? target : Quaternion.RotateTowards(transform.rotation, target, turnSpeed * deltaTime);
            if (body != null && !body.isKinematic) body.MoveRotation(next);
            else transform.rotation = next;
        }
        if (anim == null) return;
        float targetMove = Mathf.Clamp(planar.magnitude, 0f, 5f);
        if (moveDampTime > 0f)
        {
            anim.SetFloat(MoveParameter, targetMove, moveDampTime, deltaTime);
            animatorMoveValue = anim.GetFloat(MoveParameter);
        }
        else
        {
            animatorMoveValue = targetMove;
            anim.SetFloat(MoveParameter, animatorMoveValue);
        }
    }

    private void UpdateMoveAnimation(float deltaTime)
    {
        if (anim == null || !NetworkAuthority.IsOwnerOrOffline(this)) return;
        // FixedUpdate drives the target. This method intentionally only keeps
        // the visual parameter alive while a prefab is idle.
        if (deltaTime <= 0f) return;
    }

    private void RefreshLocalInputState()
    {
        if (!isActiveAndEnabled || !NetworkAuthority.IsOwnerOrOffline(this))
        {
            DisableLocalInput();
            return;
        }
        if (inputEnabled) return;
        moveAction.Enable();
        attackAction.Enable();
        attackAction.performed += AttackTrigger;
        inputEnabled = true;
    }

    private void DisableLocalInput()
    {
        if (!inputEnabled) return;
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
        if (cinematicCamera == null) return;
        CameraTarget target = cinematicCamera.Target;
        target.TrackingTarget = transform;
        if (!target.CustomLookAtTarget) target.LookAtTarget = transform;
        cinematicCamera.Target = target;
    }
}
