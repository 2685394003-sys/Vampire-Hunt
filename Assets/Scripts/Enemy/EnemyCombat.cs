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
        if (target == null || EnemyAttackPoint == null || StatsManager.Instance == null)
            return;

        if (Vector3.Distance(EnemyAttackPoint.position, target.position) > StatsManager.Instance.enemyweaponRange)
            return;

        target.GetComponentInParent<PlayerHealth>()?.ChangeHealth(StatsManager.Instance.enemydamage);
        target.GetComponentInParent<PlayerController>()?.Knockback(
            transform,
            StatsManager.Instance.enemyknockbackForce,
            StatsManager.Instance.enemystunTime);
    }

    private void OnDrawGizmosSelected()
    {
        if (EnemyAttackPoint == null || StatsManager.Instance == null)
            return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(EnemyAttackPoint.position, StatsManager.Instance.enemyweaponRange);
    }
}
