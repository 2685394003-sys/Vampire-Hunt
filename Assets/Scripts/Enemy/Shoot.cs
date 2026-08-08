using UnityEngine;

public sealed class Shoot : MonoBehaviour
{
    public Transform targetPlayer;
    private Rigidbody2D body;

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

        if (targetPlayer != null && body != null && StatsManager.Instance != null)
        {
            Vector2 direction = (targetPlayer.position - transform.position).normalized;
            body.linearVelocity = direction * StatsManager.Instance.bulletspeed;
        }

        float life = StatsManager.Instance != null ? StatsManager.Instance.bulletMaxLife : 5f;
        Invoke(nameof(ServerExpire), Mathf.Max(0.05f, life));
    }

    private void OnTriggerEnter2D(Collider2D hit)
    {
        if (!NetworkAuthority.IsServerOrOffline() || hit == null)
            return;

        PlayerHealth playerHealth = hit.GetComponentInParent<PlayerHealth>();
        if (playerHealth != null)
        {
            int damage = StatsManager.Instance != null ? StatsManager.Instance.shootDamage : 1;
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
}
