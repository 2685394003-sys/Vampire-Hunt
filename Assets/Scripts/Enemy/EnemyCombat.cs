using UnityEngine;

public sealed class EnemyCombat : MonoBehaviour
{
    public Transform EnemyAttackPoint;

    /// <summary>
    /// Server-authoritative damage resolution. Kept public for compatibility
    /// with legacy Animation Events, though the 3D enemy calls it from its
    /// gameplay attack timeline instead.
    /// </summary>
    public void Attack()
    {
        if (!NetworkAuthority.IsServerOrOffline())
            return;

        FlowFieldEnemy enemy = GetComponentInParent<FlowFieldEnemy>();
        EnemyHealth enemyHealth = GetComponentInParent<EnemyHealth>();
        if (enemyHealth != null && enemyHealth.IsFrozen) return;
        Transform target = enemy != null ? enemy.CurrentTarget : null;
        EnemyStatsConfig stats = EnemyStatsResolver.Resolve(this);
        if (target == null || stats == null)
            return;

        Transform attackOrigin = EnemyAttackPoint != null ? EnemyAttackPoint : transform;
        float weaponRange = enemyHealth != null
            ? enemyHealth.GetStatValue(EnemyStatType.WeaponRange)
            : EnemyRunStats.GetValue(stats, EnemyStatType.WeaponRange);
        Vector3 targetOffset = target.position - attackOrigin.position;
        targetOffset.y = 0f;
        if (targetOffset.magnitude > weaponRange)
            return;

        int damage = enemyHealth != null
            ? enemyHealth.GetRoundedStatValue(EnemyStatType.Damage)
            : EnemyRunStats.GetRoundedValue(stats, EnemyStatType.Damage);
        target.GetComponentInParent<PlayerHealth>()?.ChangeHealth(damage);
        target.GetComponentInParent<PlayerController>()?.Knockback(
            transform,
            enemyHealth != null
                ? enemyHealth.GetStatValue(EnemyStatType.KnockbackForce)
                : EnemyRunStats.GetValue(stats, EnemyStatType.KnockbackForce),
            stats.stunTime);
    }

    private void OnDrawGizmosSelected()
    {
        EnemyStatsConfig stats = EnemyStatsResolver.Resolve(this);
        if (EnemyAttackPoint == null || stats == null)
            return;
        EnemyHealth enemyHealth = GetComponentInParent<EnemyHealth>();
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(
            EnemyAttackPoint.position,
            enemyHealth != null
                ? enemyHealth.GetStatValue(EnemyStatType.WeaponRange)
                : EnemyRunStats.GetValue(stats, EnemyStatType.WeaponRange));
    }
}
