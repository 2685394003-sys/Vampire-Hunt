using System.Collections;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class FlowFieldEnemy : NetworkBehaviour, INetworkPoolLifecycle
{
    [Header("流场寻路参数 / Flow Field Pathfinding")]
    public float turnSmooth = 6f;
    public float dirBlendSpeed = 7f;
    public float slowDeceleration = 3f;

    [Header("局部避障 / Local Obstacle Avoidance")]
    [SerializeField, Min(0.05f)] private float obstacleProbeRadius = 0.3f;
    [SerializeField, Min(0.1f)] private float obstacleProbeDistance = 0.8f;
    [SerializeField, Min(0.02f)] private float obstacleProbeInterval = 0.08f;
    [SerializeField, Range(10f, 60f)] private float avoidanceAngleStep = 30f;
    [SerializeField, Range(1, 5)] private int avoidanceChecksPerSide = 4;
    [SerializeField, Min(0.01f)] private float stuckSpeedThreshold = 0.2f;
    [SerializeField, Min(0.1f)] private float stuckSideSwitchDelay = 0.4f;

    [Header("攻击节奏 / Attack Timing")]
    [Tooltip("进入攻击动画后，等待多久结算伤害。动画只负责表现，不再依赖 Animation Event。")]
    [SerializeField, Min(0f)] private float attackHitDelay = 0.35f;
    [Tooltip("临时沿用 Player Controller 时，一次攻击表现持续的总时长。")]
    [SerializeField, Min(0.01f)] private float attackAnimationDuration = 1.25f;

    private FlowFieldManager flowField;
    private Vector3 smoothDirection;
    private Rigidbody body;
    private EnemyAnimationController animationController;
    private EnemyCombat combat;
    private EnemyHealth enemyHealth;
    private PlayerNetworkState targetPlayer;
    private Coroutine slowCoroutine;
    private float attackCooldownTimer;
    private float attackElapsed;
    private bool attackDamageResolved;
    private EnemyStatsConfig stats;
    private float stuckTimer;
    private int avoidanceSide;
    private float obstacleProbeTimer;
    private Vector3 cachedAvoidanceDirection;
    private bool cachedAvoiding;

    private readonly NetworkVariable<EnemyState> networkState = new(
        EnemyState.Idle,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private EnemyState offlineState = EnemyState.Idle;

    public Transform CurrentTarget => targetPlayer != null ? targetPlayer.transform : null;
    public EnemyState State => UseNetworkState ? networkState.Value : offlineState;
    private bool UseNetworkState => NetworkAuthority.IsNetworkActive && IsSpawned;

    private void Awake()
    {
        if (GetComponent<NetworkObject>() == null && !NetworkAuthority.IsNetworkActive)
        {
            gameObject.AddComponent<NetworkObject>();
        }

        body = GetComponent<Rigidbody>();
        animationController = GetComponent<EnemyAnimationController>();
        combat = GetComponent<EnemyCombat>();
        enemyHealth = GetComponent<EnemyHealth>();
        flowField = FindFirstObjectByType<FlowFieldManager>();
        stats = EnemyStatsResolver.Resolve(this);
        smoothDirection = Vector3.forward;
        avoidanceSide = (GetInstanceID() & 1) == 0 ? 1 : -1;
        obstacleProbeTimer = Mathf.Abs(GetInstanceID() % 17) / 17f * obstacleProbeInterval;
        offlineState = EnemyState.Idle;
        animationController?.ApplyState(State, true);
    }

    public override void OnNetworkSpawn()
    {
        networkState.OnValueChanged += HandleNetworkStateChanged;
        if (IsServer)
            networkState.Value = EnemyState.Idle;
        if (!IsServer && body != null)
        {
            body.isKinematic = true;
        }
        animationController?.ApplyState(State, true);
    }

    public override void OnNetworkDespawn()
    {
        networkState.OnValueChanged -= HandleNetworkStateChanged;
    }

    public void OnTakenFromNetworkPool()
    {
        ResetRuntimeState();
    }

    public void OnReturnedToNetworkPool()
    {
        ResetRuntimeState();
    }

    private void ResetRuntimeState()
    {
        CancelSlowStop();
        StopAllCoroutines();
        targetPlayer = null;
        attackCooldownTimer = 0f;
        attackElapsed = 0f;
        attackDamageResolved = false;
        stuckTimer = 0f;
        obstacleProbeTimer = 0f;
        cachedAvoiding = false;
        cachedAvoidanceDirection = Vector3.zero;
        smoothDirection = Vector3.forward;
        offlineState = EnemyState.Idle;
        SetVelocity(Vector3.zero);
        animationController?.ApplyState(EnemyState.Idle, true);
    }

    private void Update()
    {
        animationController?.ApplyState(State);

        if (!NetworkAuthority.IsServerOrOffline(this))
            return;

        if (enemyHealth != null && enemyHealth.IsFrozen)
        {
            CancelSlowStop();
            SetVelocity(Vector3.zero);
            if (State != EnemyState.Idle) ChangeState(EnemyState.Idle);
            return;
        }

        if (State == EnemyState.Knockback) return;

        ResolveTargetAndState();
        attackCooldownTimer = Mathf.Max(0f, attackCooldownTimer - Time.deltaTime);

        if (State == EnemyState.isChasing)
        {
            Chase();
        }
        else
        {
            SetVelocity(Vector3.zero);
            if (State == EnemyState.isAttacking)
                UpdateAttack(Time.deltaTime);
        }
    }

    public void ChangeState(EnemyState newState)
    {
        if (!NetworkAuthority.IsServerOrOffline(this))
            return;

        EnemyState previousState = State;
        if (previousState == newState)
            return;

        if (UseNetworkState)
        {
            networkState.Value = newState;
        }
        else
        {
            offlineState = newState;
            HandleStateChanged(previousState, newState);
        }
    }

    public void EnterKnockbackState() => ChangeState(EnemyState.Knockback);

    private void ResolveTargetAndState()
    {
        if (targetPlayer == null || !targetPlayer.IsAlive)
        {
            targetPlayer = NetworkPlayerRegistry.GetClosestAlive(transform.position);
        }

        if (targetPlayer == null && flowField != null && flowField.player != null)
        {
            targetPlayer = flowField.player.GetComponentInParent<PlayerNetworkState>();
        }

        if (targetPlayer == null)
        {
            if (State != EnemyState.Idle)
            {
                ChangeState(EnemyState.Idle);
                StartSlowStop();
            }
            return;
        }

        float attackRange = GetStatValue(EnemyStatType.WeaponRange, 1f);
        float distance = Vector3.Distance(transform.position, targetPlayer.transform.position);
        if (distance <= attackRange)
        {
            if (attackCooldownTimer <= 0f && State != EnemyState.isAttacking)
            {
                StopMovement();
                ChangeState(EnemyState.isAttacking);
                attackCooldownTimer = GetStatValue(EnemyStatType.AttackCooldown, 1f);
            }
            else if (State != EnemyState.isAttacking)
            {
                ChangeState(EnemyState.Idle);
                StopMovement();
            }
        }
        else if (State != EnemyState.isAttacking && State != EnemyState.isChasing)
        {
            ChangeState(EnemyState.isChasing);
            CancelSlowStop();
        }
    }

    private void Chase()
    {
        if (targetPlayer == null)
            return;

        Vector3 rawDirection = flowField != null
            ? flowField.GetFlowDirection(transform.position, targetPlayer.transform)
            : targetPlayer.transform.position - transform.position;
        rawDirection.y = 0f;
        if (rawDirection.sqrMagnitude < 0.0001f)
        {
            SetVelocity(Vector3.zero);
            return;
        }

        float speed = GetStatValue(EnemyStatType.MoveSpeed, 1f);
        Vector3 desiredDirection = GetObstacleAvoidedDirection(
            rawDirection.normalized,
            speed,
            out bool avoiding,
            out bool probePerformed);
        if (desiredDirection.sqrMagnitude < 0.0001f)
        {
            SetVelocity(Vector3.zero);
            return;
        }

        Vector3 nextDirection = avoiding
            ? desiredDirection
            : Vector3.Lerp(
                smoothDirection,
                desiredDirection,
                Time.deltaTime * dirBlendSpeed).normalized;

        // The previous smoothed heading may still point into a wall after the flow
        // changes. Snap to the newly validated direction instead of cutting the corner.
        if (probePerformed && IsDirectionBlocked(nextDirection, GetProbeDistance(speed)))
            nextDirection = desiredDirection;

        smoothDirection = nextDirection;
        SetVelocity(nextDirection * speed);

        if (smoothDirection.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(smoothDirection, Vector3.up);
            transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, Time.deltaTime * turnSmooth);
        }
    }

    private Vector3 GetObstacleAvoidedDirection(
        Vector3 desiredDirection,
        float speed,
        out bool avoiding,
        out bool probePerformed)
    {
        obstacleProbeTimer -= Time.deltaTime;
        probePerformed = obstacleProbeTimer <= 0f;
        if (probePerformed)
        {
            obstacleProbeTimer += Mathf.Max(0.02f, obstacleProbeInterval);
            cachedAvoidanceDirection = ResolveObstacleAvoidance(
                desiredDirection,
                speed,
                out cachedAvoiding);
        }

        avoiding = cachedAvoiding;
        return cachedAvoiding ? cachedAvoidanceDirection : desiredDirection;
    }

    private Vector3 ResolveObstacleAvoidance(
        Vector3 desiredDirection,
        float speed,
        out bool avoiding)
    {
        avoiding = false;
        float probeDistance = GetProbeDistance(speed);
        if (!IsDirectionBlocked(desiredDirection, probeDistance))
        {
            stuckTimer = 0f;
            return desiredDirection;
        }

        avoiding = true;
        Vector3 planarVelocity = body != null ? body.linearVelocity : Vector3.zero;
        planarVelocity.y = 0f;
        if (planarVelocity.sqrMagnitude <= stuckSpeedThreshold * stuckSpeedThreshold)
        {
            stuckTimer += Mathf.Max(Time.deltaTime, obstacleProbeInterval);
            if (stuckTimer >= stuckSideSwitchDelay)
            {
                avoidanceSide = -avoidanceSide;
                stuckTimer = 0f;
            }
        }
        else
        {
            stuckTimer = 0f;
        }

        for (int step = 1; step <= avoidanceChecksPerSide; step++)
        {
            float angle = avoidanceAngleStep * step;
            Vector3 preferred = Quaternion.AngleAxis(angle * avoidanceSide, Vector3.up) *
                                desiredDirection;
            if (!IsDirectionBlocked(preferred, probeDistance))
                return preferred.normalized;

            Vector3 alternate = Quaternion.AngleAxis(-angle * avoidanceSide, Vector3.up) *
                                desiredDirection;
            if (!IsDirectionBlocked(alternate, probeDistance))
                return alternate.normalized;
        }

        return Vector3.zero;
    }

    private float GetProbeDistance(float speed) =>
        Mathf.Max(obstacleProbeDistance, speed * 0.15f);

    private bool IsDirectionBlocked(Vector3 direction, float distance)
    {
        if (flowField == null ||
            flowField.obstacleLayer.value == 0 ||
            direction.sqrMagnitude < 0.0001f)
        {
            return false;
        }

        // Probe near the capsule's feet so low props are detected as reliably as
        // tall walls. The obstacle mask excludes the ground, so this stays clear
        // of the floor while still catching small crates, stones, and decorations.
        Vector3 origin = transform.position + Vector3.up * obstacleProbeRadius;
        return Physics.SphereCast(
            origin,
            obstacleProbeRadius,
            direction.normalized,
            out _,
            distance,
            flowField.obstacleLayer,
            QueryTriggerInteraction.Ignore);
    }

    public void FinishAttack()
    {
        if (!NetworkAuthority.IsServerOrOffline(this))
            return;

        if (targetPlayer == null || !targetPlayer.IsAlive)
        {
            ChangeState(EnemyState.Idle);
            return;
        }

        float range = GetStatValue(EnemyStatType.WeaponRange, 1f);
        ChangeState(Vector3.Distance(transform.position, targetPlayer.transform.position) <= range
            ? EnemyState.Idle
            : EnemyState.isChasing);
    }

    private void UpdateAttack(float deltaTime)
    {
        attackElapsed += deltaTime;
        FaceCurrentTarget();

        float hitTime = Mathf.Min(attackHitDelay, attackAnimationDuration);
        if (!attackDamageResolved && attackElapsed >= hitTime)
        {
            attackDamageResolved = true;
            combat?.Attack();
        }

        if (attackElapsed >= attackAnimationDuration)
            FinishAttack();
    }

    private void FaceCurrentTarget()
    {
        if (targetPlayer == null)
            return;

        Vector3 direction = targetPlayer.transform.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude <= 0.001f)
            return;

        Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        transform.rotation = Quaternion.Lerp(
            transform.rotation,
            targetRotation,
            Time.deltaTime * turnSmooth);
    }

    private void HandleNetworkStateChanged(EnemyState previousState, EnemyState newState)
    {
        HandleStateChanged(previousState, newState);
    }

    private void HandleStateChanged(EnemyState previousState, EnemyState newState)
    {
        if (newState == EnemyState.isAttacking)
        {
            attackElapsed = 0f;
            attackDamageResolved = false;
        }
        else if (previousState == EnemyState.isAttacking)
        {
            attackElapsed = 0f;
            attackDamageResolved = false;
        }

        animationController?.ApplyState(newState, true);
    }

    private void StartSlowStop()
    {
        CancelSlowStop();
        slowCoroutine = StartCoroutine(SlowStop());
    }

    private IEnumerator SlowStop()
    {
        Vector3 velocity = body != null ? body.linearVelocity : Vector3.zero;
        while (velocity.magnitude > 0.05f)
        {
            velocity = Vector3.MoveTowards(velocity, Vector3.zero, slowDeceleration * Time.deltaTime);
            SetVelocity(velocity);
            yield return null;
        }
        SetVelocity(Vector3.zero);
        slowCoroutine = null;
    }

    private void StopMovement() => StartSlowStop();

    private void CancelSlowStop()
    {
        if (slowCoroutine == null)
            return;
        StopCoroutine(slowCoroutine);
        slowCoroutine = null;
    }

    private void SetVelocity(Vector3 velocity)
    {
        if (body != null)
            body.linearVelocity = velocity;
    }

    private float GetStatValue(EnemyStatType stat, float fallback)
    {
        if (enemyHealth != null) return enemyHealth.GetStatValue(stat);
        return stats != null ? EnemyRunStats.GetValue(stats, stat) : fallback;
    }
}

public enum EnemyState
{
    Idle,
    isChasing,
    isAttacking,
    Knockback
}
