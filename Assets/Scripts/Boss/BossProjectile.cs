using System;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;

/// <summary>
/// Server projectile view/physics adapter. Collision only reports an intent to
/// the Boss projectile port; CombatApplicationService remains the sole health
/// mutation path.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class BossProjectile : NetworkBehaviour
{
    private Vector3 direction;
    private float speed;
    private int damage;
    private LayerMask playerLayer;
    private LayerMask obstacleLayer;
    private Transform owner;
    private float minimumWorldY;
    private float knockback;
    private bool initialized;
    private Action<ICombatTarget, int, WorldPosition, float> hit;

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
        Initialize(moveDirection, moveSpeed, hitDamage, hitKnockback, lifeTime,
            targetPlayerLayer, worldObstacleLayer, projectileOwner, minimumEffectHeight,
            (Action<ICombatTarget, int, WorldPosition, float>)null);
    }

    public void Initialize(
        Vector3 moveDirection,
        float moveSpeed,
        int hitDamage,
        float hitKnockback,
        float lifeTime,
        LayerMask targetPlayerLayer,
        LayerMask worldObstacleLayer,
        Transform projectileOwner,
        float minimumEffectHeight,
        Action<ICombatTarget, int, WorldPosition> hitCallback)
    {
        Action<ICombatTarget, int, WorldPosition, float> wrapped = null;
        if (hitCallback != null)
            wrapped = (target, damageValue, position, _) => hitCallback(target, damageValue, position);
        Initialize(moveDirection, moveSpeed, hitDamage, hitKnockback, lifeTime,
            targetPlayerLayer, worldObstacleLayer, projectileOwner, minimumEffectHeight,
            wrapped);
    }

    public void Initialize(
        Vector3 moveDirection,
        float moveSpeed,
        int hitDamage,
        float hitKnockback,
        float lifeTime,
        LayerMask targetPlayerLayer,
        LayerMask worldObstacleLayer,
        Transform projectileOwner,
        float minimumEffectHeight,
        Action<ICombatTarget, int, WorldPosition, float> hitCallback)
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
        hit = hitCallback;
        Vector3 position = transform.position;
        position.y = Mathf.Max(position.y, minimumWorldY);
        transform.position = position;
        initialized = true;
        StartCoroutine(DespawnAfter(Mathf.Max(0.05f, lifeTime)));
    }

    private void Update()
    {
        if (!NetworkAuthority.IsServerOrOffline(this) || !initialized) return;
        transform.position += direction * (speed * Time.deltaTime);
        Vector3 position = transform.position;
        position.y = Mathf.Max(position.y, minimumWorldY);
        transform.position = position;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!NetworkAuthority.IsServerOrOffline(this) || !initialized || other == null) return;
        if (owner != null && (other.transform == owner || other.transform.IsChildOf(owner))) return;

        if (IsInLayerMask(other.gameObject.layer, playerLayer))
        {
            if (BossCombatTarget.TryGetCombatTarget(other, out ICombatTarget target) && target != null)
            {
                Vector3 point = other.ClosestPoint(transform.position);
                WorldPosition position = new(point.x, point.y, point.z);
                hit?.Invoke(target, damage, position, knockback);
            }
            NetworkSpawnUtility.Despawn(gameObject);
            return;
        }

        if (IsInLayerMask(other.gameObject.layer, obstacleLayer))
            NetworkSpawnUtility.Despawn(gameObject);
    }

    private System.Collections.IEnumerator DespawnAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        NetworkSpawnUtility.Despawn(gameObject);
    }

    private static bool IsInLayerMask(int layer, LayerMask mask) => (mask.value & (1 << layer)) != 0;
}
