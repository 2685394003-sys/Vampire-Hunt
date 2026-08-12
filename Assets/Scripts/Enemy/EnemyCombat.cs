using UnityEngine;

public sealed class EnemyCombat : MonoBehaviour
{
    public Transform EnemyAttackPoint;

    public void Attack()
    {
        if (!NetworkAuthority.IsServerOrOffline())
            return;

        FlowFieldEnemy enemy = GetComponentInParent<FlowFieldEnemy>();
        Transform target = enemy != null ? enemy.CurrentTarget : null;
        EnemyStatsConfig stats = EnemyStatsResolver.Resolve(this);
        if (target == null || EnemyAttackPoint == null || stats == null)
            return;

        float weaponRange = EnemyRunStats.GetValue(stats, EnemyStatType.WeaponRange);
        if (Vector3.Distance(EnemyAttackPoint.position, target.position) > weaponRange)
            return;

        int damage = EnemyRunStats.GetRoundedValue(stats, EnemyStatType.Damage);
        target.GetComponentInParent<PlayerHealth>()?.ChangeHealth(damage);
        target.GetComponentInParent<PlayerController>()?.Knockback(
            transform,
            EnemyRunStats.GetValue(stats, EnemyStatType.KnockbackForce),
            stats.stunTime);
    }

    private void OnDrawGizmosSelected()
    {
        EnemyStatsConfig stats = EnemyStatsResolver.Resolve(this);
        if (EnemyAttackPoint == null || stats == null)
            return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(
            EnemyAttackPoint.position,
            EnemyRunStats.GetValue(stats, EnemyStatType.WeaponRange));
    }
}
