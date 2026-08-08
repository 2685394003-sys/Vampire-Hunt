using System.Collections;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class FlowFieldEnemy : NetworkBehaviour
{
    [Header("流场寻路参数 / Flow Field Pathfinding")]
    public float turnSmooth = 6f;
    public float dirBlendSpeed = 7f;
    public float slowDeceleration = 3f;

    private FlowFieldManager flowField;
    private Vector3 smoothDirection;
    private EnemyState enemyState;
    private Rigidbody body;
    private Animator animator;
    private PlayerNetworkState targetPlayer;
    private Coroutine slowCoroutine;
    private float attackCooldownTimer;

    public Transform CurrentTarget => targetPlayer != null ? targetPlayer.transform : null;
    public EnemyState State => enemyState;

    private void Awake()
    {
        if (GetComponent<NetworkObject>() == null && !NetworkAuthority.IsNetworkActive)
        {
            gameObject.AddComponent<NetworkObject>();
        }

        body = GetComponent<Rigidbody>();
        animator = GetComponent<Animator>();
        flowField = FindFirstObjectByType<FlowFieldManager>();
        smoothDirection = Vector3.forward;
        ChangeState(EnemyState.Idle);
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer && body != null)
        {
            body.isKinematic = true;
        }
    }

    private void Update()
    {
        if (!NetworkAuthority.IsServerOrOffline(this) || enemyState == EnemyState.Knockback)
            return;

        ResolveTargetAndState();
        attackCooldownTimer = Mathf.Max(0f, attackCooldownTimer - Time.deltaTime);

        if (enemyState == EnemyState.isChasing) Chase();
        else if (enemyState == EnemyState.isAttacking || enemyState == EnemyState.Idle) SetVelocity(Vector3.zero);
    }

    public void ChangeState(EnemyState newState)
    {
        if (!NetworkAuthority.IsServerOrOffline(this))
            return;

        if (animator != null)
        {
            animator.SetBool("isIdle", newState == EnemyState.Idle);
            animator.SetBool("isChasing", newState == EnemyState.isChasing);
            animator.SetBool("isAttacking", newState == EnemyState.isAttacking);
        }
        enemyState = newState;
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
            if (enemyState != EnemyState.Idle)
            {
                ChangeState(EnemyState.Idle);
                StartSlowStop();
            }
            return;
        }

        float attackRange = StatsManager.Instance != null ? StatsManager.Instance.enemyAttackRange : 1f;
        float distance = Vector3.Distance(transform.position, targetPlayer.transform.position);
        if (distance <= attackRange)
        {
            if (attackCooldownTimer <= 0f)
            {
                StopMovement();
                ChangeState(EnemyState.isAttacking);
                attackCooldownTimer = StatsManager.Instance != null
                    ? StatsManager.Instance.enemyattaCooldown
                    : 1f;
            }
            else if (enemyState != EnemyState.isAttacking)
            {
                ChangeState(EnemyState.Idle);
                StopMovement();
            }
        }
        else if (enemyState != EnemyState.isAttacking && enemyState != EnemyState.isChasing)
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
            rawDirection = targetPlayer.transform.position - transform.position;
            rawDirection.y = 0f;
        }

        smoothDirection = Vector3.Lerp(
            smoothDirection,
            rawDirection.normalized,
            Time.deltaTime * dirBlendSpeed);
        float speed = StatsManager.Instance != null ? StatsManager.Instance.enemyspeed : 1f;
        SetVelocity(smoothDirection.normalized * speed);

        if (smoothDirection.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(smoothDirection, Vector3.up);
            transform.rotation = Quaternion.Lerp(transform.rotation, targetRotation, Time.deltaTime * turnSmooth);
        }
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

        float range = StatsManager.Instance != null ? StatsManager.Instance.enemyAttackRange : 1f;
        ChangeState(Vector3.Distance(transform.position, targetPlayer.transform.position) <= range
            ? EnemyState.Idle
            : EnemyState.isChasing);
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
}

public enum EnemyState
{
    Idle,
    isChasing,
    isAttacking,
    Knockback
}
