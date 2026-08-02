using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class EnemyCombat : MonoBehaviour
{
    public Transform EnemyAttackPoint;

    public void Attack()
    {
        if (EnemyAttackPoint == null || StatsManager.Instance == null)
            return;

        // 3D版：用球形检测代替2D圆形检测
        Collider[] hits = Physics.OverlapSphere(EnemyAttackPoint.position, StatsManager.Instance.enemyweaponRange, StatsManager.Instance.playerLayer);

        if (hits.Length > 0)
        {
            PlayerHealth playerHp = hits[0].GetComponentInParent<PlayerHealth>();
            if (playerHp != null)
            {
                playerHp.ChangeHealth(StatsManager.Instance.enemydamage);
                // 击退等其他效果暂未实现（3D版待做）
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (EnemyAttackPoint == null || StatsManager.Instance == null)
            return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(EnemyAttackPoint.position, StatsManager.Instance.enemyweaponRange);
    }
}