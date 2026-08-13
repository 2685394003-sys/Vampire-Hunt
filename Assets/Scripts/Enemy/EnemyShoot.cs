using System.Collections;
using UnityEngine;

public class EnemyShoot : MonoBehaviour
{
    public Transform firePoint;
    public GameObject bulletPrefab;
    private EnemyMovement enemyMove;
    private EnemyHealth enemyHealth;
    private bool shotPending;

    void Start()
    {
        // 拿到同物体上的EnemyMovement
        enemyMove = GetComponent<EnemyMovement>();
        enemyHealth = GetComponentInParent<EnemyHealth>();
    }
    
    public void Shoot()
    {
        if (!NetworkAuthority.IsServerOrOffline() || shotPending ||
            (enemyHealth != null && enemyHealth.IsFrozen))
            return;

        EnemyStatsConfig stats = EnemyStatsResolver.Resolve(this);
        float delay = stats != null ? stats.shootWaitTime : 0f;
        if (delay > 0f)
        {
            StartCoroutine(ShootAfterDelay(delay, stats));
            return;
        }

        SpawnProjectile(stats);
    }

    private IEnumerator ShootAfterDelay(float delay, EnemyStatsConfig stats)
    {
        shotPending = true;
        yield return new WaitForSeconds(delay);
        shotPending = false;
        if (NetworkAuthority.IsServerOrOffline() &&
            (enemyHealth == null || !enemyHealth.IsFrozen))
        {
            SpawnProjectile(stats);
        }
    }

    private void SpawnProjectile(EnemyStatsConfig stats)
    {
        if (firePoint == null || bulletPrefab == null) return;

        Transform targetPlayer = null;
        if(enemyMove != null)
        {
            targetPlayer = enemyMove.GetPlayerTarget();
        }

        GameObject bulletObj = NetworkSpawnUtility.Spawn(
            bulletPrefab,
            firePoint.position,
            Quaternion.identity);
        if (bulletObj == null)
            return;

        Shoot bullet = bulletObj.GetComponent<Shoot>();

        if(bullet != null)
        {
            bullet.Configure(targetPlayer, stats);
        }
    }
}
