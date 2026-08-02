using System.Collections;
using UnityEngine;

public class FlowFieldEnemy : MonoBehaviour
{
    [Header("流场寻路参数 / Flow Field Pathfinding")]
    public float turnSmooth = 6f;
    public float dirBlendSpeed = 7f;
    [Header("丢失目标减速参数 / Lost-target Deceleration")]
    public float slowDeceleration = 3f;

    private FlowFieldManager flowField;
    private Vector3 smoothDirection;
    private EnemyState enemyState;
    private Rigidbody rb;
    private Animator anim;
    private Transform player;
    private Coroutine slowCoroutine;
    private float attackCooldownTimer; // 每只小怪自己的攻击冷却计时器(上限由StatsManager.enemyattaCooldown统一管理)

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        anim = GetComponent<Animator>();
        ChangeState(EnemyState.Idle);
        flowField = FindObjectOfType<FlowFieldManager>();
        smoothDirection = Vector3.forward;
    }

    void Update()
    {
        if (enemyState != EnemyState.Knockback)
        {
            CheckForPlayer();

            if (attackCooldownTimer > 0)
            {
                attackCooldownTimer -= Time.deltaTime;
            }

            if (enemyState == EnemyState.isChasing)
            {
                Chase();
            }
            else if (enemyState == EnemyState.isAttacking)
            {
                rb.linearVelocity = Vector3.zero;
            }
            else if(enemyState == EnemyState.Idle)
            {
                rb.linearVelocity = Vector3.zero;
            }
        }
    }

    public void ChangeState(EnemyState newState)
    {
        if (enemyState == EnemyState.Idle)
            anim.SetBool("isIdle", false);
        else if (enemyState == EnemyState.isChasing)
            anim.SetBool("isChasing", false);
        else if (enemyState == EnemyState.isAttacking)
            anim.SetBool("isAttacking", false);

        enemyState = newState;

        if (enemyState == EnemyState.Idle)
            anim.SetBool("isIdle", true);
        else if (enemyState == EnemyState.isChasing)
            anim.SetBool("isChasing", true);
        else if (enemyState == EnemyState.isAttacking)
            anim.SetBool("isAttacking", true);
    }

    private void Chase()
    {
        if (flowField == null)
        {
            flowField = FindObjectOfType<FlowFieldManager>();
            return;
        }

        Vector3 rawDir = flowField.GetFlowDirection(transform.position);
        if (rawDir.magnitude < 0.01f && player != null)
            rawDir = (player.position - transform.position).normalized;

        smoothDirection = Vector3.Lerp(smoothDirection, rawDir.normalized, Time.deltaTime * dirBlendSpeed);
        rb.linearVelocity = smoothDirection * StatsManager.Instance.enemyspeed;

        if (smoothDirection.magnitude > 0.01f)
        {
            Vector3 flatDir = Vector3.ProjectOnPlane(smoothDirection, Vector3.up);
            Quaternion targetRot = Quaternion.LookRotation(flatDir);
            transform.rotation = Quaternion.Lerp(transform.rotation, targetRot, Time.deltaTime * turnSmooth);
        }

        FacePlayer();
    }

    void FacePlayer()
    {
        if (player == null) return;

        Vector3 lookDir = player.position - transform.position;
        lookDir.y = 0; // 只水平转向，不上下仰头
        if(lookDir.magnitude > 0.01f)
        {
            Quaternion targetRot = Quaternion.LookRotation(lookDir);
            transform.rotation = Quaternion.Lerp(transform.rotation, targetRot, Time.deltaTime * turnSmooth);
        }
    }


    private void CheckForPlayer()
    {
        if (flowField == null) return;
        player = flowField.player;

        if (player == null)
        {
            if (enemyState != EnemyState.Idle)
            {
                ChangeState(EnemyState.Idle);
                StartSlowStop();
            }
            return;
        }

        float distance = Vector3.Distance(transform.position, player.position);

        // 玩家在攻击范围内
        if (distance <= StatsManager.Instance.enemyAttackRange)
        {
            // CD就绪 → 攻击
            if (attackCooldownTimer <= 0)
            {
                Stop();
                ChangeState(EnemyState.isAttacking);
                attackCooldownTimer = StatsManager.Instance.enemyattaCooldown;
            }
            // CD没就绪 → 切Idle待机，不再追击
            else
            {
                if(enemyState != EnemyState.isAttacking)
                {
                    ChangeState(EnemyState.Idle);
                    Stop();
                }
            }
        }
        // 玩家超出攻击范围 → 持续追逐
        else
        {
            if (enemyState != EnemyState.isAttacking && enemyState != EnemyState.isChasing)
            {
                ChangeState(EnemyState.isChasing);
                if (slowCoroutine != null)
                {
                    StopCoroutine(slowCoroutine);
                    slowCoroutine = null;
                }
            }
        }
    }

    // 攻击动画帧事件调用：攻击动作播放完毕
    public void FinishAttack()
    {
        if (player == null)
        {
            ChangeState(EnemyState.Idle);
            StartSlowStop();
            return;
        }

        float distance = Vector3.Distance(transform.position, player.position);
        // 攻击结束，如果玩家还在圈内 → 保持Idle等待CD
        if (distance <= StatsManager.Instance.enemyAttackRange)
        {
            ChangeState(EnemyState.Idle);
        }
        // 玩家跑出攻击范围 → 继续追
        else
        {
            ChangeState(EnemyState.isChasing);
        }
    }

    void StartSlowStop()
    {
        if (slowCoroutine != null)
            StopCoroutine(slowCoroutine);
        slowCoroutine = StartCoroutine(SlowStop());
    }
    IEnumerator SlowStop()
    {
        Vector3 vel = rb.linearVelocity;
        while (vel.magnitude > 0.05f)
        {
            vel = Vector3.MoveTowards(vel, Vector3.zero, slowDeceleration * Time.deltaTime);
            rb.linearVelocity = vel;
            yield return null;
        }
        rb.linearVelocity = Vector3.zero;
        slowCoroutine = null;
    }

    void Stop()
    {
        if (slowCoroutine != null)
            StopCoroutine(slowCoroutine);
        slowCoroutine = StartCoroutine(SlowStop());
    }
}

public enum EnemyState
{
    Idle,
    isChasing,
    isAttacking,
    Knockback
}
