using UnityEngine;

public sealed class Shoot : MonoBehaviour
{
    public Transform targetPlayer;
    private Rigidbody2D body;
    private EnemyStatsConfig sourceStats;

    public void Configure(Transform target, EnemyStatsConfig stats)
    {
        targetPlayer = target;
        sourceStats = stats;
    }

    private void Start()
    {
        body = GetComponent<Rigidbody2D>();
        if (!NetworkAuthority.IsServerOrOffline())
        {
            if (body != null) body.simulated = false;
            return;
        }

        if (targetPlayer == null)
        {
            PlayerNetworkState target = NetworkPlayerRegistry.GetClosestAlive(transform.position);
            targetPlayer = target != null ? target.transform : null;
        }

        EnemyStatsConfig stats = ResolveStats();
        if (targetPlayer != null && body != null && stats != null)
        {
            Vector2 direction = (targetPlayer.position - transform.position).normalized;
            body.linearVelocity = direction * stats.projectileSpeed;
        }

        float life = stats != null ? stats.projectileLifetime : 5f;
        Invoke(nameof(ServerExpire), Mathf.Max(0.05f, life));
    }

    private void OnTriggerEnter2D(Collider2D hit)
    {
        if (!NetworkAuthority.IsServerOrOffline() || hit == null)
            return;

        PlayerHealth playerHealth = hit.GetComponentInParent<PlayerHealth>();
        if (playerHealth != null)
        {
            EnemyStatsConfig stats = ResolveStats();
            int damage = stats != null
                ? EnemyRunStats.GetRoundedValue(stats, EnemyStatType.ProjectileDamage)
                : 1;
            playerHealth.ChangeHealth(damage);
            NetworkSpawnUtility.Despawn(gameObject);
            return;
        }

        if (hit.CompareTag("Wall"))
            NetworkSpawnUtility.Despawn(gameObject);
    }

    private void ServerExpire()
    {
        NetworkSpawnUtility.Despawn(gameObject);
    }

    private EnemyStatsConfig ResolveStats() =>
        sourceStats != null ? sourceStats : EnemyStatsResolver.Resolve(this);
}
