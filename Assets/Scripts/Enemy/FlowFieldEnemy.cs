using System;
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

    [Header("组件引用 / Component References")]
    [SerializeField] private Rigidbody body;
    [SerializeField] private EnemyAnimationController animationController;
    [SerializeField] private EnemyCombat combat;
    [SerializeField] private EnemyHealth enemyHealth;
    private EnemyState offlineState = EnemyState.Idle;

    private readonly NetworkVariable<EnemyState> networkState = new(
        EnemyState.Idle,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>
    /// Legacy view property. Target transforms are no longer discovered by the
    /// enemy motor; the composed runtime exposes an immutable target id and
    /// position instead.
    /// </summary>
    [Obsolete("Use CurrentTargetId and CurrentTargetPosition from the enemy runtime.")]
    public Transform CurrentTarget => null;
    public EntityId CurrentTargetId =>
        enemyHealth != null && enemyHealth.Runtime != null
            ? enemyHealth.Runtime.Snapshot.TargetId
            : default(EntityId);
    public WorldPosition CurrentTargetPosition =>
        enemyHealth != null && enemyHealth.Runtime != null
            ? enemyHealth.Runtime.Snapshot.TargetPosition
            : default(WorldPosition);
    public EnemyState State => UseNetworkState ? networkState.Value : offlineState;
    private bool UseNetworkState => NetworkAuthority.IsNetworkActive && IsSpawned;

    private void Awake()
    {
        if (GetComponent<NetworkObject>() == null && !NetworkAuthority.IsNetworkActive)
            gameObject.AddComponent<NetworkObject>();

        if (body == null) body = GetComponent<Rigidbody>();
        if (animationController == null)
            animationController = GetComponent<EnemyAnimationController>();
        if (combat == null) combat = GetComponent<EnemyCombat>();
        if (enemyHealth == null) enemyHealth = GetComponent<EnemyHealth>();
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

        ResolveDomainIntent();
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

    private void ResolveDomainIntent()
    {
        EnemyIntent intent = enemyHealth.Runtime.Decide();
        if (!intent.HasTarget)
        {
            SetVelocity(Vector3.zero);
            ChangeState(EnemyState.Idle);
            return;
        }

        if (intent.ShouldAttack)
        {
            SetVelocity(Vector3.zero);
            ChangeState(EnemyState.isAttacking);
            if (combat != null && combat.TryAttack())
                enemyHealth.Runtime.RecordAttack(Time.time);
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

    public void FinishAttack()
    {
        if (!NetworkAuthority.IsServerOrOffline(this)) return;
        ChangeState(CurrentTargetId.IsValid ? EnemyState.isChasing : EnemyState.Idle);
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
