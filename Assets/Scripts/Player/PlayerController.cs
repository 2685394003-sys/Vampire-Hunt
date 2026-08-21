using System;
using System.Collections;
using Unity.Netcode;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using VampireHunt.Core;
using VampireHunt.Infrastructure.Input;
using VampireHunt.Infrastructure.Input.Contracts;
using VampireHunt.Player.Contracts;
using EntityId = VampireHunt.Core.EntityId;

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
    [FormerlySerializedAs("playerAttack")]
    [SerializeField] private PlayerAttackPresenter _attackPresenter;
    [SerializeField] private PlayerNetworkState playerState;
    [SerializeField] private Rigidbody body;

    [Header("所有者移动 / Owner Movement")]
    [SerializeField, Min(0f)] private float turnSpeed = 720f;

    [Header("动画过渡 / Animation Blending")]
    [SerializeField, Min(0f)] private float moveDampTime = 0.2f;
    [SerializeField, Min(0f)] private float attackWindupDuration = 0.18f;

    [Header("状态 / State")]
    public bool isKnockedBack;
    public Vector3 knockbackVelocity;
    public Vector3 facingDirection = Vector3.forward;

    private Camera viewCamera;
    private Vector3 localMoveWorld;
    private Vector3 localAimWorld = Vector3.forward;
    private Vector3 lastMoveDirection = Vector3.forward;
    private Vector3 ownerDashDirection;
    private float ownerDashRemaining;
    private float animatorMoveValue;
    private Coroutine knockbackCoroutine;
    private PlayerInputAdapter inputAdapter;
    private OwnerMovementMotor movementMotor;
    private bool inputEnabled;

    private void Awake()
    {
        if (playerState == null)
            playerState = PlayerNetworkState.EnsureForMigration(gameObject);
        if (body == null)
            body = GetComponent<Rigidbody>();
        viewCamera = Camera.main;
        if (_attackPresenter == null) _attackPresenter = GetComponent<PlayerAttackPresenter>();
        if (anim != null) animatorMoveValue = anim.GetFloat(MoveParameter);
        if (!NetworkAuthority.IsNetworkActive) ConfigurePhysicsAuthority(true);
    }

    private void OnEnable() => RefreshLocalInputState();

    private void OnDisable()
    {
        DisableLocalInput();
        inputAdapter = null;
        movementMotor = null;
        if (knockbackCoroutine != null) StopCoroutine(knockbackCoroutine);
        knockbackCoroutine = null;
        isKnockedBack = false;
    }

    public override void OnNetworkSpawn()
    {
        ConfigurePhysicsAuthority(IsOwner);
        RefreshLocalInputState();
        if (IsOwner) BindLocalPresentation();
    }

    public override void OnNetworkDespawn()
    {
        DisableLocalInput();
        inputAdapter = null;
        movementMotor = null;
    }

    private void Update()
    {
        UpdateMoveAnimation(Time.deltaTime);
        if (!NetworkAuthority.IsOwnerOrOffline(this) || playerState == null || !playerState.IsAlive)
            return;

        EnsureInputAdapter();
        inputAdapter?.Tick();
        MoveVector sampledInput = inputAdapter?.SampleInput() ?? default;
        localMoveWorld = ConvertInputToWorld(new Vector2(sampledInput.X, sampledInput.Z));
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
            movementMotor?.ReportPose(new MoveVector(knockbackVelocity.x, 0f, knockbackVelocity.z));
            UpdateFacingAndMoveTarget(Vector3.zero, Time.fixedDeltaTime);
            return;
        }

        if (ownerDashRemaining > 0f)
        {
            ownerDashRemaining = Mathf.Max(0f, ownerDashRemaining - Time.fixedDeltaTime);
            movementMotor?.Dash(
                new MoveVector(ownerDashDirection.x, ownerDashDirection.y, ownerDashDirection.z),
                Time.fixedDeltaTime);
            UpdateFacingAndMoveTarget(ownerDashDirection * playerState.MoveSpeed * playerState.DashSpeedMultiplier, Time.fixedDeltaTime);
            return;
        }

        MoveVector movement = new(localMoveWorld.x, localMoveWorld.z);
        if (movementMotor != null)
            movementMotor.Drive(movement, Time.fixedDeltaTime);
        else
            MoveOwner(localMoveWorld * playerState.MoveSpeed, Time.fixedDeltaTime);
        UpdateFacingAndMoveTarget(localMoveWorld * playerState.MoveSpeed, Time.fixedDeltaTime);
    }

    public void RequestAttack()
    {
        if (!NetworkAuthority.IsOwnerOrOffline(this) || playerState == null || !playerState.IsAlive) return;
        EnsureInputAdapter();
        CommandResult result = inputAdapter != null
            ? inputAdapter.SubmitAttack(ToWorldPosition(transform.position + localAimWorld))
            : playerState.RequestAttackIntent(transform.position + localAimWorld);
        if (result.Accepted) _attackPresenter?.PlayAttackPresentation();
    }

    /// <summary>
    /// Owner movement adapter hook called by PlayerDash after server/application
    /// acceptance. It does not spend stamina or decide cooldowns.
    /// </summary>
    public bool StartOwnerDash(Vector3 worldDirection)
    {
        if (playerState == null || !NetworkAuthority.IsOwnerOrOffline(this) || !playerState.IsAlive)
            return false;
        EnsureInputAdapter();
        Vector3 planar = Vector3.ProjectOnPlane(worldDirection, Vector3.up);
        if (planar.sqrMagnitude <= 0.0001f) planar = lastMoveDirection;
        if (planar.sqrMagnitude <= 0.0001f) planar = transform.forward;
        ownerDashDirection = planar.normalized;
        ownerDashRemaining = Mathf.Max(0f, playerState.DashDuration);
        return ownerDashRemaining > 0f;
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
        Vector3 planar = Vector3.ProjectOnPlane(velocity, Vector3.up);
        if (body != null && !body.isKinematic)
        {
            Vector3 current = body.linearVelocity;
            body.linearVelocity = new Vector3(planar.x, current.y, planar.z);
        }
        else
        {
            transform.position += planar * deltaTime;
        }
    }

    /// <summary>Root-motion adapter; authoritative gameplay still comes from Commands.</summary>
    public void ApplyAnimatorRootMotion(Vector3 deltaPosition)
    {
        if (!NetworkAuthority.IsOwnerOrOffline(this) || playerState == null || !playerState.IsAlive ||
            isKnockedBack || ownerDashRemaining > 0f) return;
        // The Animator still consumes its authored root motion, but translation
        // of the network root is owned exclusively by the FixedUpdate motor.
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
        // OwnerMovementMotor drives the local pose. This compatibility hook
        // intentionally only keeps the visual parameter alive while a prefab is idle.
        if (deltaTime <= 0f) return;
    }

    private void ConfigurePhysicsAuthority(bool simulateLocally)
    {
        if (body == null) return;
        if (!simulateLocally)
        {
            if (!body.isKinematic) body.linearVelocity = Vector3.zero;
            body.useGravity = false;
            body.isKinematic = true;
            return;
        }

        body.isKinematic = false;
        body.useGravity = true;
    }

    private void RefreshLocalInputState()
    {
        if (!isActiveAndEnabled || !NetworkAuthority.IsOwnerOrOffline(this))
        {
            DisableLocalInput();
            return;
        }
        if (inputEnabled) return;
        moveAction?.Enable();
        attackAction?.Enable();
        inputEnabled = true;
    }

    private void DisableLocalInput()
    {
        if (!inputEnabled) return;
        moveAction?.Disable();
        attackAction?.Disable();
        inputEnabled = false;
    }

    /// <summary>
    /// Builds the new Input/OwnerMovement adapter lazily. Runtime binding is
    /// performed by Bootstrap after Unity Awake; delaying id resolution prevents
    /// the compatibility shell from allocating an id before it can be bound to
    /// the server-owned PlayerAggregate.
    /// </summary>
    private void EnsureInputAdapter()
    {
        if (inputAdapter != null || playerState == null) return;
        if (NetworkAuthority.IsNetworkActive && (!IsSpawned || !IsOwner)) return;

        if (!playerState.TryGetAuthoritativeLogicalPlayerId(out EntityId playerId)) return;
        LegacyPlayerCommandGateway gateway = new(playerState);
        movementMotor = new OwnerMovementMotor(
            transform,
            playerId,
            poseTransport: playerState,
            moveSpeed: playerState.MoveSpeed,
            dashSpeedMultiplier: playerState.DashSpeedMultiplier,
            body: body,
            moveSpeedSource: () => playerState != null ? playerState.MoveSpeed : 0f,
            dashSpeedMultiplierSource: () => playerState != null ? playerState.DashSpeedMultiplier : 1f);
        inputAdapter = new PlayerInputAdapter(
            playerId,
            movementMotor,
            gateway,
            moveAction,
            dashAction: null,
            attackAction: attackAction,
            aimSource: new DelegateAimWorldPositionSource(_ =>
                ToWorldPosition(transform.position + GetPointerAimDirection())),
            moveResolver: ResolveMovementInput,
            attackAccepted: () => _attackPresenter?.PlayAttackPresentation());
    }

    private MoveVector ResolveMovementInput(MoveVector input)
    {
        if (isKnockedBack || ownerDashRemaining > 0f) return default;
        Vector3 world = ConvertInputToWorld(new Vector2(input.X, input.Z));
        return new MoveVector(world.x, world.z);
    }

    private static WorldPosition ToWorldPosition(Vector3 value) =>
        new(value.x, value.y, value.z);

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

    private sealed class LegacyPlayerCommandGateway : IPlayerCommandGateway
    {
        private readonly PlayerNetworkState state;

        public LegacyPlayerCommandGateway(PlayerNetworkState state) =>
            this.state = state ?? throw new ArgumentNullException(nameof(state));

        public CommandResult SubmitDash(DashCommand command) =>
            state.RequestDashIntent(new Vector3(command.Direction.X, command.Direction.Y, command.Direction.Z));

        public CommandResult SubmitAttack(AttackCommand command) =>
            state.RequestAttackIntent(new Vector3(command.AimAt.X, command.AimAt.Y, command.AimAt.Z));

        public CommandResult SelectBloodPact(SelectBloodPactCommand command)
        {
            bool accepted = state.RequestBloodPactSelection(command.Selection.Value);
            return accepted
                ? CommandResult.Accept(command.Sequence)
                : new CommandResult(CommandResultStatus.Rejected, "Blood Pact selection was rejected", command.Sequence);
        }
    }
}
