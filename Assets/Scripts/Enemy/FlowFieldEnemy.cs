using Unity.Netcode;
using UnityEngine;
using VampireHunt.Core;
using VampireHunt.Enemies.Contracts;
using EntityId = VampireHunt.Core.EntityId;

/// <summary>
/// Network/Unity motor adapter for the domain EnemyBrain.  It keeps the
/// serialized tuning and animation/network state used by existing prefabs, but
/// all target decisions and attack intents come from EnemyRuntimeController.
/// </summary>
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
    [Tooltip("Retained for serialized prefab compatibility; hit timing is an application concern.")]
    [SerializeField, Min(0f)] private float attackHitDelay = 0.35f;
    [Tooltip("Retained for serialized prefab compatibility; animation is presentation only.")]
    [SerializeField, Min(0.01f)] private float attackAnimationDuration = 1.25f;

    private Rigidbody body;
    private EnemyAnimationController animationController;
    private EnemyCombat combat;
    private EnemyHealth enemyHealth;
    private Transform targetPlayer;
    private float attackCooldownTimer;
    private EnemyState offlineState = EnemyState.Idle;

    private readonly NetworkVariable<EnemyState> networkState = new(
        EnemyState.Idle,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public Transform CurrentTarget => targetPlayer;
    public EnemyState State => UseNetworkState ? networkState.Value : offlineState;
    private bool UseNetworkState => NetworkAuthority.IsNetworkActive && IsSpawned;

    private void Awake()
    {
        if (GetComponent<NetworkObject>() == null && !NetworkAuthority.IsNetworkActive)
            gameObject.AddComponent<NetworkObject>();

        body = GetComponent<Rigidbody>();
        animationController = GetComponent<EnemyAnimationController>();
        combat = GetComponent<EnemyCombat>();
        enemyHealth = GetComponent<EnemyHealth>();
        offlineState = EnemyState.Idle;
        animationController?.ApplyState(State, true);
    }

    public override void OnNetworkSpawn()
    {
        networkState.OnValueChanged += HandleNetworkStateChanged;
        if (IsServer) networkState.Value = EnemyState.Idle;
        if (!IsServer && body != null) body.isKinematic = true;
        animationController?.ApplyState(State, true);
    }

    public override void OnNetworkDespawn()
    {
        networkState.OnValueChanged -= HandleNetworkStateChanged;
    }

    public void OnTakenFromNetworkPool() => ResetRuntimeState();
    public void OnReturnedToNetworkPool() => ResetRuntimeState();

    private void ResetRuntimeState()
    {
        StopAllCoroutines();
        targetPlayer = null;
        attackCooldownTimer = 0f;
        offlineState = EnemyState.Idle;
        SetVelocity(Vector3.zero);
        animationController?.ApplyState(EnemyState.Idle, true);
    }

    private void Update()
    {
        animationController?.ApplyState(State);
        if (!NetworkAuthority.IsServerOrOffline(this)) return;
        if (enemyHealth == null || enemyHealth.Runtime == null || enemyHealth.IsDead)
        {
            SetVelocity(Vector3.zero);
            return;
        }
        if (enemyHealth.IsFrozen)
        {
            SetVelocity(Vector3.zero);
            ChangeState(EnemyState.Idle);
            return;
        }

        ResolveTargetAndState();
        attackCooldownTimer = Mathf.Max(0f, attackCooldownTimer - Time.deltaTime);
    }

    public void ChangeState(EnemyState newState)
    {
        if (!NetworkAuthority.IsServerOrOffline(this)) return;
        EnemyState previous = State;
        if (previous == newState) return;

        if (UseNetworkState) networkState.Value = newState;
        else
        {
            offlineState = newState;
            HandleStateChanged(previous, newState);
        }
    }

    public void EnterKnockbackState()
    {
        enemyHealth?.Runtime?.SetState(VampireHunt.Enemies.Contracts.EnemyState.Stunned);
        ChangeState(EnemyState.Knockback);
    }

    private void ResolveTargetAndState()
    {
        if (targetPlayer == null)
            targetPlayer = ResolveTargetTransform();
        if (targetPlayer == null)
        {
            SetVelocity(Vector3.zero);
            ChangeState(EnemyState.Idle);
            return;
        }

        EntityId targetId = EnemyLegacyEntityIds.Resolve(targetPlayer.gameObject);
        if (!targetId.IsValid)
        {
            SetVelocity(Vector3.zero);
            ChangeState(EnemyState.Idle);
            return;
        }

        Vector3 position = transform.position;
        Vector3 targetPosition = targetPlayer.position;
        float distance = Vector3.Distance(position, targetPosition);
        WorldPosition self = ToWorldPosition(position);
        WorldPosition target = ToWorldPosition(targetPosition);
        EnemyPerceptionData perception = new EnemyPerceptionData(
            enemyHealth.Runtime.Id,
            self,
            targetId,
            target,
            distance,
            true,
            true);
        EnemyIntent intent = enemyHealth.Runtime.Decide(in perception);

        if (intent.ShouldAttack)
        {
            SetVelocity(Vector3.zero);
            ChangeState(EnemyState.isAttacking);
            if (attackCooldownTimer <= 0f)
            {
                combat?.Attack();
                enemyHealth.Runtime.RecordAttack(Time.time);
                attackCooldownTimer = enemyHealth.GetStatValue(EnemyStatType.AttackCooldown);
            }
            return;
        }

        if (intent.ShouldMove)
        {
            Vector3 direction = new Vector3(intent.Direction.X, 0f, intent.Direction.Y).normalized;
            float speed = enemyHealth.Runtime.MoveSpeed;
            SetVelocity(direction * speed);
            if (direction.sqrMagnitude > 0.0001f)
            {
                Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
                transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, Time.deltaTime * turnSmooth);
            }
            ChangeState(EnemyState.isChasing);
            return;
        }

        SetVelocity(Vector3.zero);
        ChangeState(intent.State == VampireHunt.Enemies.Contracts.EnemyState.Recovering
            ? EnemyState.isChasing
            : EnemyState.Idle);
    }

    private Transform ResolveTargetTransform()
    {
        PlayerNetworkState player = NetworkPlayerRegistry.GetClosestAlive(transform.position);
        if (player != null) return player.transform;

        FlowFieldManager manager = FindFirstObjectByType<FlowFieldManager>();
        return manager != null ? manager.player : null;
    }

    public void FinishAttack()
    {
        if (!NetworkAuthority.IsServerOrOffline(this)) return;
        ChangeState(targetPlayer == null ? EnemyState.Idle : EnemyState.isChasing);
    }

    private void HandleNetworkStateChanged(EnemyState previous, EnemyState next) =>
        HandleStateChanged(previous, next);

    private void HandleStateChanged(EnemyState previous, EnemyState next)
    {
        animationController?.ApplyState(next, true);
    }

    private void SetVelocity(Vector3 velocity)
    {
        if (body != null) body.linearVelocity = velocity;
    }

    private static WorldPosition ToWorldPosition(Vector3 position) =>
        new WorldPosition(position.x, position.y, position.z);
}

/// <summary>Legacy serialized state enum retained for prefab/animation APIs.</summary>
public enum EnemyState
{
    Idle = 0,
    isChasing = 1,
    isAttacking = 2,
    Knockback = 3
}
