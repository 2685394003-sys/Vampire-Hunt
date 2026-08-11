using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyMovement : MonoBehaviour
{
    private EnemyState enemyState;
    private float shoottimer;
    private float attackCooldownTimer;
    private EnemyStatsConfig stats;

    private Rigidbody2D rb;
    public Transform EnemyDetectionPonint;
    private Transform player;
    private Animator anim;
    private Coroutine slowCoroutine; // 记录减速协程，防止重复启动

    // Start is called before the first frame update
    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        anim = GetComponent<Animator>();
        stats = EnemyStatsResolver.Resolve(this);
        if (!NetworkAuthority.IsServerOrOffline())
        {
            if (rb != null) rb.simulated = false;
            enabled = false;
            return;
        }
        ChangeState(EnemyState.Idle);
    }

    // Update is called once per frame
    void Update()
    {
        if (!NetworkAuthority.IsServerOrOffline()) return;
        if (enemyState != EnemyState.Knockback)
        {
            if(player != null && shoottimer <= 0 && Vector2.Distance(transform.position, player.position) > stats.shootRange)
            {
                Stop();
                shoottimer = stats.shootCooldown;
                return;
            }

            CheckForPlayer();

            if(attackCooldownTimer > 0)
            {
                attackCooldownTimer -= Time.deltaTime;
            }

            if(shoottimer > 0)
            {
                shoottimer -= Time.deltaTime;
            }


            if (player != null && enemyState == EnemyState.isChasing && Vector2.Distance(transform.position, player.position) > stats.attackRange)
            {
                Chase();
            }
            else if(enemyState == EnemyState.isAttacking)
            {
                rb.linearVelocity = Vector2.zero;
            }
            
        }
    }

    private void CheckForPlayer()
    {   
        if (stats == null || EnemyDetectionPonint == null) return;
        Collider2D[] hits = Physics2D.OverlapCircleAll(EnemyDetectionPonint.position, stats.playerDetectRange, stats.playerLayer);
        
        if(hits.Length > 0)
        {
            player = hits[0].transform;
        
            if(Vector2.Distance(transform.position, player.position) <= stats.attackRange && attackCooldownTimer <= 0)
            {
                Stop();
                ChangeState(EnemyState.isAttacking);
               attackCooldownTimer = stats.attackCooldown;
            }

            else if(Vector2.Distance(transform.position, player.position) > stats.attackRange && enemyState != EnemyState.isAttacking)
            {
                ChangeState(EnemyState.isChasing);
                // 玩家重新进入，立刻停止减速协程，恢复追逐
                if (slowCoroutine != null)
                {
                StopCoroutine(slowCoroutine);
                slowCoroutine = null;
                }

            }
        }
        else
        {
            ChangeState(EnemyState.Idle);
            // 丢失玩家，开启线性减速协程
            Stop();

        }
    }

     // 线性匀减速，一次执行到停止就结束，不占用Update
    IEnumerator SlowDownToStop()
    {
        Vector2 currentVel = rb.linearVelocity;
        // 持续匀速减小速度，直到接近0
        while (currentVel.magnitude > 0.05f)
        {
            currentVel = Vector2.MoveTowards(currentVel, Vector2.zero, stats.slowDeceleration * Time.deltaTime);
            rb.linearVelocity = currentVel;
            yield return null; // 等待下一帧再执行
        }
        // 速度几乎为0，直接清零彻底停止
        rb.linearVelocity = Vector2.zero;
        slowCoroutine = null;
    }

    public void ChangeState(EnemyState newState)
    {
        if (!NetworkAuthority.IsServerOrOffline()) return;
        //退出当前动画
        if (enemyState == EnemyState.Idle)
            anim.SetBool("isIdle", false);
        else if (enemyState == EnemyState.isChasing)
            anim.SetBool("isChasing", false);
        else if (enemyState == EnemyState.isAttacking)
            anim.SetBool("isAttacking", false);
            anim.SetBool("isShooting", false);


        //更新当前状态
        enemyState = newState;

        //更新新动画
        if (enemyState == EnemyState.Idle)
            anim.SetBool("isIdle", true);
        else if (enemyState == EnemyState.isChasing)
            anim.SetBool("isChasing", true);
        else if (enemyState == EnemyState.isAttacking)
            anim.SetBool("isAttacking", true);
            anim.SetBool("isShooting", true);

    }

    void Chase()
    {
        if(Vector2.Distance(transform.position, player.transform.position) <= stats.attackRange && attackCooldownTimer <= 0)
        {
            ChangeState(EnemyState.isAttacking);
            attackCooldownTimer = stats.attackCooldown;
        }

        Vector2 direction = (player.position - transform.position).normalized;
        rb.linearVelocity = direction * stats.moveSpeed;

        // 翻转逻辑
        float dirX = player.position.x - transform.position.x;
        if(Mathf.Abs(dirX) > 0.01f)
        {
            transform.localScale = new Vector3(-Mathf.Sign(dirX), 1, 1);
        }

    }

    void Stop()
    {
        slowCoroutine = StartCoroutine(SlowDownToStop());
    }

    public Transform GetPlayerTarget()
    {
        return player;
    }


}

