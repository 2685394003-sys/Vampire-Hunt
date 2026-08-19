using System.Collections;
using UnityEngine;
using VampireHunt.Core;
using VampireHunt.Enemies.Contracts;
using EntityId = VampireHunt.Core.EntityId;

/// <summary>
/// Legacy ranged-attack adapter.  It builds a ranged AttackIntent from the
/// runtime facade and forwards it to the composed projectile port.  The
/// serialized fire point/prefab are retained as a fallback view adapter for
/// old prefabs until their NetworkSpawnAdapter binding is installed.
/// </summary>
public class EnemyShoot : MonoBehaviour, INetworkPoolLifecycle
{
    public Transform firePoint;
    public GameObject bulletPrefab;
    private EnemyMovement enemyMove;
    private EnemyHealth enemyHealth;
    private bool shotPending;

    private void Start()
    {
        enemyMove = GetComponent<EnemyMovement>();
        enemyHealth = GetComponentInParent<EnemyHealth>();
    }

    public void OnTakenFromNetworkPool()
    {
        StopAllCoroutines();
        shotPending = false;
    }

    public void OnReturnedToNetworkPool() => OnTakenFromNetworkPool();

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

        ForwardOrSpawn(stats);
    }

    private IEnumerator ShootAfterDelay(float delay, EnemyStatsConfig stats)
    {
        shotPending = true;
        yield return new WaitForSeconds(delay);
        shotPending = false;
        if (NetworkAuthority.IsServerOrOffline() &&
            (enemyHealth == null || !enemyHealth.IsFrozen))
            ForwardOrSpawn(stats);
    }

    private void ForwardOrSpawn(EnemyStatsConfig stats)
    {
        EnemyHealth health = enemyHealth != null ? enemyHealth : GetComponentInParent<EnemyHealth>();
        Transform target = enemyMove != null ? enemyMove.GetPlayerTarget() : null;
        if (health != null && health.Runtime != null && target != null)
        {
            EntityId targetId = EnemyLegacyEntityIds.Resolve(target.gameObject);
            if (targetId.IsValid)
            {
                EnemyCombatContextData context = new EnemyCombatContextData(
                    health.Runtime.Id,
                    targetId,
                    ToWorldPosition(transform.position),
                    Vector3.Distance(transform.position, target.position),
                    true,
                    Time.time,
                    health.Runtime.Snapshot.LastAttackAt);
                AttackIntent intent = health.Runtime.CreateAttackIntent(in context);
                if (intent.SourceId.IsValid && TryForwardProjectile(in intent)) return;
            }
        }

        // Transitional presentation fallback: this still only creates the
        // projectile view. Shoot converts any eventual hit through its port.
        SpawnLegacyProjectile(target, stats, health != null ? health.Runtime?.Id ?? default(EntityId) : default(EntityId));
    }

    private bool TryForwardProjectile(in AttackIntent intent)
    {
        foreach (MonoBehaviour component in GetComponentsInParent<MonoBehaviour>(true))
        {
            if (component is IEnemyProjectilePort port && port.Spawn(in intent))
                return true;
        }
        return false;
    }

    private void SpawnLegacyProjectile(Transform target, EnemyStatsConfig stats, EntityId sourceId)
    {
        if (firePoint == null || bulletPrefab == null) return;

        GameObject bulletObject = NetworkSpawnUtility.Spawn(
            bulletPrefab,
            firePoint.position,
            Quaternion.identity);
        if (bulletObject == null) return;

        Shoot bullet = bulletObject.GetComponent<Shoot>();
        bullet?.Configure(target, stats, sourceId);
    }

    private static WorldPosition ToWorldPosition(Vector3 position) =>
        new WorldPosition(position.x, position.y, position.z);
}
