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
    private WorldPosition targetPosition;
    private bool hasTargetPosition;

    public void Configure(Transform target, EnemyStatsConfig stats)
    {
        Configure(target, stats, default(EntityId));
    }

    public void Configure(Transform target, EnemyStatsConfig stats, EntityId source)
    {
        targetPlayer = target;
        targetPosition = target != null
            ? new WorldPosition(target.position.x, target.position.y, target.position.z)
            : default(WorldPosition);
        hasTargetPosition = target != null;
        sourceStats = stats;
        sourceId = source;
    }

    public void Configure(WorldPosition target, EnemyStatsConfig stats, EntityId source)
    {
        targetPlayer = null;
        targetPosition = target;
        hasTargetPosition = true;
        sourceStats = stats;
        sourceId = source;
    }

    public void OnTakenFromNetworkPool()
    {
        CancelInvoke();
        targetPlayer = null;
        targetPosition = default(WorldPosition);
        hasTargetPosition = false;
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

        if (hasTargetPosition && body != null)
        {
            Vector2 direction = new Vector2(
                targetPosition.X - transform.position.x,
                targetPosition.Z - transform.position.z).normalized;
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
