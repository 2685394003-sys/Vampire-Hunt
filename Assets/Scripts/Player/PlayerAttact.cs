using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;


public class PlayerAttact : MonoBehaviour
{
    public Animator anim;
    public Animator attackPointAnim;
    public Transform AttackPoint;

    [Header("特效大小跟随攻击范围 / VFX Scale Follows Weapon Range")]
    [Tooltip("特效在 localScale=1 时的可视半径(世界单位),用于对齐判定框红线 / VFX visual radius at localScale=1 (world units), align to red gizmo circle")]
    [SerializeField] private float vfxVisualRadiusAtScaleOne = 2f;

    private float timer;

    private void Update()
    {
        SyncVfxScale();

        if(timer > 0)
        {
            timer -= Time.deltaTime;
        }
    }
    
    public void Attack()
    {
        if(timer <= 0)
        {
        if (anim != null)
            anim.SetBool("isAttacting",true);
        if (attackPointAnim != null)
            attackPointAnim.SetBool("isAttacking",true);
        if (SFXManager.Instance != null)
            SFXManager.Instance.PlayAttackSFX();

        timer = StatsManager.Instance.cooldown;
        }
    }

    public void Attackfalse()
    {
        if (anim != null)
            anim.SetBool("isAttacting",false);
        if (attackPointAnim != null)
            attackPointAnim.SetBool("isAttacking",false);
    }

    // 用攻击范围换算特效缩放：weaponRange 越大，特效弧光越大
    private void SyncVfxScale()
    {
        if (AttackPoint == null || StatsManager.Instance == null)
            return;

        float s = StatsManager.Instance.weaponRange / Mathf.Max(0.001f, vfxVisualRadiusAtScaleOne);
        AttackPoint.localScale = new Vector3(s, s, s);
    }

    // 由 Effects Animation 的 AnimationEvent 在挥砍最亮帧调用
    public void DealDamage()
    {
        if (AttackPoint == null || StatsManager.Instance == null)
            return;

        float range = StatsManager.Instance.weaponRange;
        int damage = StatsManager.Instance.damage;
        Collider[] hits = Physics.OverlapSphere(
            AttackPoint.position,
            range,
            StatsManager.Instance.enemyLayer,
            QueryTriggerInteraction.Collide);

        HashSet<Component> damaged = new HashSet<Component>();
        foreach (Collider hit in hits)
        {
            if (hit == null)
                continue;

            // Boss 与一切实现 IDamageable 的目标（对齐 BossCombatTarget 范式）
            if (BossCombatTarget.TryGetInParent<IDamageable>(hit, out IDamageable damageable))
            {
                Component asComponent = damageable as Component;
                if (asComponent != null && !damaged.Add(asComponent))
                    continue;

                damageable.TakeDamage(damage);

                if (StatsManager.Instance.knockbackForce > 0f &&
                    BossCombatTarget.TryGetInParent<IKnockbackReceiver>(hit, out IKnockbackReceiver receiver))
                {
                    receiver.ApplyKnockback(transform, StatsManager.Instance.knockbackForce, StatsManager.Instance.knockbackTime);
                }
                continue;
            }

            // 普通敌人
            EnemyHealth enemyHealth = hit.GetComponentInParent<EnemyHealth>();
            if (enemyHealth == null || !damaged.Add(enemyHealth))
                continue;

            enemyHealth.ChangeEnemyHealth(damage);

            EnemyKnockBack enemyKnockBack = hit.GetComponentInParent<EnemyKnockBack>();
            if (enemyKnockBack != null)
            {
                enemyKnockBack.EnemyKnockback(
                    transform,
                    StatsManager.Instance.knockbackForce,
                    StatsManager.Instance.stunTime,
                    StatsManager.Instance.knockbackTime);
            }
        }
    }

    [ContextMenu("测试/触发一次挥砍伤害")]
    private void DebugDealDamage()
    {
        DealDamage();
    }

    // 编辑时始终可见，半径跟随 StatsManager.weaponRange，与特效范围对齐
    private void OnDrawGizmos()
    {
        if (AttackPoint == null || StatsManager.Instance == null)
            return;

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(AttackPoint.position, StatsManager.Instance.weaponRange);
    }

}
