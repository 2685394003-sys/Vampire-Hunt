using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class BossProjectile : NetworkBehaviour
{
    private Vector3 direction;
    private float speed;
    private int damage;
    private float knockback;
    private LayerMask playerLayer;
    private LayerMask obstacleLayer;
    private Transform owner;
    private float minimumWorldY;
    private bool initialized;

    public void Initialize(
        Vector3 moveDirection,
        float moveSpeed,
        int hitDamage,
        float hitKnockback,
        float lifeTime,
        LayerMask targetPlayerLayer,
        LayerMask worldObstacleLayer,
        Transform projectileOwner,
        float minimumEffectHeight)
    {
        if (!NetworkAuthority.IsServerOrOffline(this)) return;
        direction = Vector3.ProjectOnPlane(moveDirection, Vector3.up).normalized;
        speed = Mathf.Max(0f, moveSpeed);
        damage = Mathf.Max(0, hitDamage);
        knockback = Mathf.Max(0f, hitKnockback);
        playerLayer = targetPlayerLayer;
        obstacleLayer = worldObstacleLayer;
        owner = projectileOwner;
        minimumWorldY = Mathf.Max(minimumEffectHeight, -0.99f);
        Vector3 startPosition = transform.position;
        startPosition.y = Mathf.Max(startPosition.y, minimumWorldY);
        transform.position = startPosition;
        initialized = true;

        StartCoroutine(DespawnAfter(Mathf.Max(0.05f, lifeTime)));
    }

    private void Update()
    {
        if (!NetworkAuthority.IsServerOrOffline(this)) return;
        if (initialized)
        {
            transform.position += direction * (speed * Time.deltaTime);
            Vector3 clampedPosition = transform.position;
            clampedPosition.y = Mathf.Max(clampedPosition.y, minimumWorldY);
            transform.position = clampedPosition;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!NetworkAuthority.IsServerOrOffline(this)) return;
        if (!initialized || other == null)
        {
            return;
        }

        if (owner != null && (other.transform == owner || other.transform.IsChildOf(owner)))
        {
            return;
        }

        if (IsInLayerMask(other.gameObject.layer, playerLayer))
        {
            if (!BossCombatTarget.TryGetInParent(other, out IDamageable damageable))
            {
                BossCombatTarget.EnsurePlayerAdapter(other.transform.root, true);
                BossCombatTarget.TryGetInParent(other, out damageable);
            }

            if (damageable == null)
            {
                return;
            }

            damageable.TakeDamage(damage);
            Component damageComponent = damageable as Component;
            if (damageComponent != null &&
                knockback > 0f &&
                BossCombatTarget.TryGetInParent(damageComponent, out IKnockbackReceiver receiver))
            {
                receiver.ApplyKnockback(
                    owner != null ? owner : transform,
                    knockback,
                    0.18f);
            }

            NetworkSpawnUtility.Despawn(gameObject);
            return;
        }

        if (IsInLayerMask(other.gameObject.layer, obstacleLayer))
        {
            NetworkSpawnUtility.Despawn(gameObject);
        }
    }

    private System.Collections.IEnumerator DespawnAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        NetworkSpawnUtility.Despawn(gameObject);
    }

    private static bool IsInLayerMask(int layer, LayerMask mask)
    {
        return (mask.value & (1 << layer)) != 0;
    }
}
