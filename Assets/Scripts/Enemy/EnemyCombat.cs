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

        if (Vector3.Distance(EnemyAttackPoint.position, target.position) > stats.weaponRange)
            return;

        target.GetComponentInParent<PlayerHealth>()?.ChangeHealth(stats.damage);
        target.GetComponentInParent<PlayerController>()?.Knockback(
            transform,
            stats.knockbackForce,
            stats.stunTime);
    }

    private void OnDrawGizmosSelected()
    {
        EnemyStatsConfig stats = EnemyStatsResolver.Resolve(this);
        if (EnemyAttackPoint == null || stats == null)
            return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(EnemyAttackPoint.position, stats.weaponRange);
    }
}
