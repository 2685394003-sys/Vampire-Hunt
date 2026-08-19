using UnityEngine;
using VampireHunt.Core;
using VampireHunt.Enemies.Contracts;
using EntityId = VampireHunt.Core.EntityId;

/// <summary>
/// Projectile view/physics adapter.  It never resolves player health; a hit
/// is converted to the public projectile-hit port and Combat owns the result.
/// </summary>
public sealed class Shoot : MonoBehaviour, INetworkPoolLifecycle
{
    public Transform targetPlayer;
    private Rigidbody2D body;
    private EnemyStatsConfig sourceStats;
    private EntityId sourceId;

    public void Configure(Transform target, EnemyStatsConfig stats)
    {
        Configure(target, stats, default(EntityId));
    }

    public void Configure(Transform target, EnemyStatsConfig stats, EntityId source)
    {
        targetPlayer = target;
        sourceStats = stats;
        sourceId = source;
    }

    public void OnTakenFromNetworkPool()
    {
        CancelInvoke();
        targetPlayer = null;
        sourceStats = null;
        sourceId = default(EntityId);
        if (body == null) body = GetComponent<Rigidbody2D>();
        if (body != null) body.linearVelocity = Vector2.zero;
    }

    public void OnReturnedToNetworkPool() => OnTakenFromNetworkPool();

    private void Start()
    {
        body = GetComponent<Rigidbody2D>();
        if (!NetworkAuthority.IsServerOrOffline())
        {
            if (body != null) body.simulated = false;
            return;
        }

        if (targetPlayer != null && body != null)
        {
            Vector2 direction = (targetPlayer.position - transform.position).normalized;
            body.linearVelocity = direction * ResolveProjectileSpeed();
        }

        Invoke(nameof(ServerExpire), Mathf.Max(0.05f, ResolveProjectileLifetime()));
    }

    private void OnTriggerEnter2D(Collider2D hit)
    {
        if (!NetworkAuthority.IsServerOrOffline() || hit == null) return;

        EntityId targetId = EnemyLegacyEntityIds.Resolve(hit.gameObject);
        if (targetId.IsValid && sourceId.IsValid)
        {
            foreach (MonoBehaviour component in hit.GetComponentsInParent<MonoBehaviour>(true))
            {
                if (component is not IEnemyProjectileHitPort port) continue;
                if (port.ApplyHit(sourceId, targetId, ToWorldPosition(transform.position)))
                {
                    NetworkSpawnUtility.Despawn(gameObject);
                    return;
                }
            }
        }

        if (hit.CompareTag("Wall"))
            NetworkSpawnUtility.Despawn(gameObject);
    }

    private void ServerExpire() => NetworkSpawnUtility.Despawn(gameObject);

    private EnemyStatsConfig ResolveStats() =>
        sourceStats != null ? sourceStats : EnemyStatsResolver.Resolve(this);

    private float ResolveProjectileSpeed()
    {
        EnemyStatsConfig stats = ResolveStats();
        return stats != null ? stats.projectileSpeed : 0f;
    }

    private float ResolveProjectileLifetime()
    {
        EnemyStatsConfig stats = ResolveStats();
        return stats != null ? stats.projectileLifetime : 5f;
    }

    private static WorldPosition ToWorldPosition(Vector3 position) =>
        new WorldPosition(position.x, position.y, position.z);
}
