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
    public Animator anim;
    public Animator attackPointAnim;
    public Transform AttackPoint;

    [Header("服务器判定 / Server Hit Timing")]
    [SerializeField, Min(0f)] private float hitDelay = 0.12f;

    [Header("特效大小跟随攻击范围 / VFX Scale Follows Weapon Range")]
    [SerializeField, Min(0.001f)] private float vfxVisualRadiusAtScaleOne = 2f;

    private PlayerNetworkState playerState;
    private PlayerController playerController;
    private Coroutine serverHitCoroutine;
    private int activeAttackSequence;
    private int lastResolvedAttackSequence;

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
        if (serverHitCoroutine != null)
        {
            StopCoroutine(serverHitCoroutine);
        }
        serverHitCoroutine = StartCoroutine(ServerResolveAfterDelay(sequence));
    }

    /// <summary>
    /// True while the attack animation is active (checked by root motion driver).
    /// </summary>
    public bool IsAttackAnimationPlaying
    {
        get
        {
            if (anim != null && anim.GetBool("isAttacting")) return true;
            if (attackPointAnim != null && attackPointAnim.GetBool("isAttacking")) return true;
            return false;
        }
    }

    public void PlayAttackPresentation()
    {
        if (anim != null)
        {
            anim.SetBool("isAttacting", true);
        }
        if (attackPointAnim != null)
        {
            attackPointAnim.SetBool("isAttacking", true);
        }
        SFXManager.Instance?.PlayAttackSFX();
    }

    public void Attackfalse()
    {
        if (anim != null)
        {
            anim.SetBool("isAttacting", false);
        }
        if (attackPointAnim != null)
        {
            attackPointAnim.SetBool("isAttacking", false);
        }
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
        int attackDamage = playerState.RollAttackDamage(out _);
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

                damageable.TakeDamage(attackDamage);
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

            enemyHealth.ChangeEnemyHealth(attackDamage);
            EnemyKnockBack enemyKnockBack = hit.GetComponentInParent<EnemyKnockBack>();
            enemyKnockBack?.EnemyKnockback(
                transform,
                playerState.KnockbackForce,
                playerState.StunTime,
                playerState.KnockbackTime);
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
