using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Server-authoritative melee resolution with client-only presentation.
/// Damage timing no longer depends exclusively on an Animator event, which is
/// essential for headless dedicated servers.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerNetworkState))]
public sealed class PlayerAttact : MonoBehaviour
{
    private static readonly int AttackParameter = Animator.StringToHash("Attack");
    private static readonly int AttackPointParameter = Animator.StringToHash("isAttacking");

    public Animator anim;
    public Animator attackPointAnim;
    public Transform AttackPoint;

    [Header("服务器判定 / Server Hit Timing")]
    [SerializeField, Min(0f)] private float hitDelay = 0.12f;
    [SerializeField, Min(0.01f)] private float attackAnimationDuration = 1.25f;

    [Header("特效大小跟随攻击范围 / VFX Scale Follows Weapon Range")]
    [SerializeField, Min(0.001f)] private float vfxVisualRadiusAtScaleOne = 2f;

    private PlayerNetworkState playerState;
    private PlayerController playerController;
    private Coroutine serverHitCoroutine;
    private Coroutine attackResetCoroutine;
    private int activeAttackSequence;
    private int lastResolvedAttackSequence;

    public bool IsAttackAnimationPlaying { get; private set; }

    private void Awake()
    {
        playerState = PlayerNetworkState.EnsureForMigration(gameObject);
        playerController = GetComponent<PlayerController>();
    }

    private void Update()
    {
        if (NetworkAuthority.IsOwnerOrOffline(playerState))
        {
            SyncVfxScale();
        }
    }

    /// <summary>Compatibility entry point used by older animation/controller code.</summary>
    public void Attack()
    {
        playerController?.RequestAttack();
    }

    public void ServerBeginAttack(int sequence)
    {
        if (!NetworkAuthority.IsServerOrOffline(playerState) ||
            playerState == null ||
            !playerState.IsAlive ||
            sequence <= activeAttackSequence)
        {
            return;
        }

        activeAttackSequence = sequence;
        playerController?.StopMoveAnimationForAttack();
        SetAttackState(true);
        RestartAttackResetTimer();
        if (serverHitCoroutine != null)
        {
            StopCoroutine(serverHitCoroutine);
        }
        serverHitCoroutine = StartCoroutine(ServerResolveAfterDelay(sequence));
    }

    public void PlayAttackPresentation()
    {
        playerController?.StopMoveAnimationForAttack();
        SetAttackState(true);
        RestartAttackResetTimer();
        SFXManager.Instance?.PlayAttackSFX();
    }

    public void Attackfalse()
    {
        SetAttackState(false);
    }

    private void SetAttackState(bool attacking)
    {
        IsAttackAnimationPlaying = attacking;
        if (anim != null)
        {
            anim.SetBool(AttackParameter, attacking);
        }
        if (attackPointAnim != null &&
            attackPointAnim.isActiveAndEnabled &&
            attackPointAnim.runtimeAnimatorController != null)
        {
            attackPointAnim.SetBool(AttackPointParameter, attacking);
        }
    }

    private void RestartAttackResetTimer()
    {
        if (attackResetCoroutine != null)
        {
            StopCoroutine(attackResetCoroutine);
        }
        attackResetCoroutine = StartCoroutine(ResetAttackAfterDelay());
    }

    private IEnumerator ResetAttackAfterDelay()
    {
        yield return new WaitForSeconds(attackAnimationDuration);
        SetAttackState(false);
        attackResetCoroutine = null;
    }

    /// <summary>
    /// AnimationEvent fallback. Sequence de-duplication guarantees that the server
    /// coroutine and animation event cannot apply the same swing twice.
    /// </summary>
    public void DealDamage()
    {
        ServerDealDamageOnce(activeAttackSequence);
    }

    private IEnumerator ServerResolveAfterDelay(int sequence)
    {
        if (hitDelay > 0f)
        {
            yield return new WaitForSeconds(hitDelay);
        }

        ServerDealDamageOnce(sequence);
        serverHitCoroutine = null;
    }

    private void ServerDealDamageOnce(int sequence)
    {
        if (!NetworkAuthority.IsServerOrOffline(playerState) ||
            playerState == null ||
            !playerState.IsAlive ||
            AttackPoint == null ||
            sequence <= 0 ||
            sequence <= lastResolvedAttackSequence)
        {
            return;
        }

        lastResolvedAttackSequence = sequence;
        Collider[] hits = Physics.OverlapSphere(
            AttackPoint.position,
            playerState.WeaponRange,
            playerState.EnemyLayer,
            QueryTriggerInteraction.Collide);

        HashSet<Component> damaged = new();
        foreach (Collider hit in hits)
        {
            if (hit == null)
            {
                continue;
            }

            if (BossCombatTarget.TryGetInParent<IDamageable>(hit, out IDamageable damageable))
            {
                Component damageComponent = damageable as Component;
                if (damageComponent != null && !damaged.Add(damageComponent))
                {
                    continue;
                }

                Vector3 damagePosition = damageComponent != null
                    ? damageComponent.transform.position
                    : hit.transform.position;
                int bossCombatTextTargetKey = damageComponent != null
                    ? damageComponent.GetInstanceID()
                    : hit.GetInstanceID();
                int bossAttackDamage = playerState.RollAttackDamage(out bool bossWasCritical);
                bool tracksHealth = TryReadCurrentHealth(damageComponent, out int previousHealth);
                damageable.TakeDamage(bossAttackDamage);
                if (tracksHealth &&
                    damageComponent != null &&
                    TryReadCurrentHealth(damageComponent, out int currentHealth))
                {
                    playerState.ReportAttackHit(
                        null,
                        Mathf.Max(0, previousHealth - currentHealth),
                        bossWasCritical,
                        damagePosition,
                        bossCombatTextTargetKey);
                }
                if (playerState.KnockbackForce > 0f &&
                    BossCombatTarget.TryGetInParent<IKnockbackReceiver>(hit, out IKnockbackReceiver receiver))
                {
                    receiver.ApplyKnockback(
                        transform,
                        playerState.KnockbackForce,
                        playerState.KnockbackTime);
                }
                continue;
            }

            EnemyHealth enemyHealth = hit.GetComponentInParent<EnemyHealth>();
            if (enemyHealth == null || !damaged.Add(enemyHealth))
            {
                continue;
            }

            Vector3 enemyPosition = enemyHealth.transform.position;
            int combatTextTargetKey = enemyHealth.GetInstanceID();
            int enemyAttackDamage = playerState.RollAttackDamage(out bool enemyWasCritical);
            int damageDealt = enemyHealth.ApplyDamage(
                enemyAttackDamage,
                playerState,
                out bool killed);
            playerState.ReportAttackHit(
                null,
                damageDealt,
                enemyWasCritical,
                enemyPosition,
                combatTextTargetKey);
            if (killed)
            {
                playerState.ReportEnemyKilled(enemyPosition);
            }
            else
            {
                EnemyKnockBack enemyKnockBack = hit.GetComponentInParent<EnemyKnockBack>();
                enemyKnockBack?.EnemyKnockback(
                    transform,
                    playerState.KnockbackForce,
                    playerState.StunTime,
                    playerState.KnockbackTime);
            }
        }
    }

    private static bool TryReadCurrentHealth(Component damageComponent, out int currentHealth)
    {
        switch (damageComponent)
        {
            case BossHealth bossHealth:
                currentHealth = bossHealth.CurrentHealth;
                return true;
            case BossGuard bossGuard:
                currentHealth = bossGuard.CurrentHealth;
                return true;
            default:
                currentHealth = 0;
                return false;
        }
    }

    private void SyncVfxScale()
    {
        if (AttackPoint == null || playerState == null)
        {
            return;
        }

        float scale = playerState.WeaponRange / Mathf.Max(0.001f, vfxVisualRadiusAtScaleOne);
        AttackPoint.localScale = Vector3.one * scale;
    }

    [ContextMenu("测试/触发一次服务器挥砍伤害")]
    private void DebugDealDamage()
    {
        if (!Application.isPlaying || !NetworkAuthority.IsServerOrOffline(playerState))
        {
            return;
        }

        activeAttackSequence++;
        ServerDealDamageOnce(activeAttackSequence);
    }

    private void OnDrawGizmos()
    {
        PlayerNetworkState state = playerState != null
            ? playerState
            : GetComponent<PlayerNetworkState>();
        if (AttackPoint == null || state == null)
        {
            return;
        }

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(AttackPoint.position, state.WeaponRange);
    }
}
