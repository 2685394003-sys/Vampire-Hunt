using UnityEngine;

/// <summary>Shared baseline for the current default enemy archetype.</summary>
[CreateAssetMenu(
    fileName = "EnemyStats",
    menuName = "Vampire Hunt/Balance/Enemy Stats",
    order = 2)]
public sealed class EnemyStatsConfig : ScriptableObject
{
    private const string DefaultResourcePath = "GameBalance/EnemyDefaultStats";

    [Header("近战 / Melee")]
    [Min(1)] public int maxHealth = 2;
    [Min(0)] public int damage = 2;
    [Min(0f)] public float attackRange = 2f;
    [Min(0f)] public float weaponRange = 2f;
    [Min(0.01f)] public float attackCooldown = 2f;
    [Min(0f)] public float knockbackForce = 2f;
    [Min(0f)] public float knockbackTime = 2f;
    [Min(0f)] public float stunTime = 2f;

    [Header("移动与检测 / Movement & Detection")]
    [Min(0f)] public float moveSpeed = 2f;
    [Min(0f)] public float playerDetectRange = 5f;
    [Min(0f)] public float slowDeceleration = 3f;
    public LayerMask playerLayer = 1 << 6;

    [Header("远程 / Ranged")]
    [Min(0)] public int projectileDamage = 2;
    [Min(0.01f)] public float shootCooldown = 2f;
    [Min(0.05f)] public float projectileLifetime = 2f;
    [Min(0f)] public float projectileSpeed = 2f;
    [Min(0f)] public float shootWaitTime = 2f;
    [Min(0f)] public float shootRange = 2f;

    [Header("受伤反馈 / Hurt Feedback")]
    [Min(0f)] public float hurtFlashDuration = 0.3f;
    [Min(0.01f)] public float hurtFlashSpeed = 0.08f;

    public static EnemyStatsConfig LoadDefault() =>
        Resources.Load<EnemyStatsConfig>(DefaultResourcePath);

    private void OnValidate()
    {
        maxHealth = Mathf.Max(1, maxHealth);
        damage = Mathf.Max(0, damage);
        attackRange = Mathf.Max(0f, attackRange);
        weaponRange = Mathf.Max(0f, weaponRange);
        attackCooldown = Mathf.Max(0.01f, attackCooldown);
        knockbackForce = Mathf.Max(0f, knockbackForce);
        knockbackTime = Mathf.Max(0f, knockbackTime);
        stunTime = Mathf.Max(0f, stunTime);
        moveSpeed = Mathf.Max(0f, moveSpeed);
        playerDetectRange = Mathf.Max(0f, playerDetectRange);
        slowDeceleration = Mathf.Max(0f, slowDeceleration);
        projectileDamage = Mathf.Max(0, projectileDamage);
        shootCooldown = Mathf.Max(0.01f, shootCooldown);
        projectileLifetime = Mathf.Max(0.05f, projectileLifetime);
        projectileSpeed = Mathf.Max(0f, projectileSpeed);
        shootWaitTime = Mathf.Max(0f, shootWaitTime);
        shootRange = Mathf.Max(0f, shootRange);
        hurtFlashDuration = Mathf.Max(0f, hurtFlashDuration);
        hurtFlashSpeed = Mathf.Max(0.01f, hurtFlashSpeed);
    }
}

public static class EnemyStatsResolver
{
    private static EnemyStatsConfig cachedDefault;

    public static EnemyStatsConfig Resolve(Component context)
    {
        if (context != null)
        {
            EnemyHealth health = context.GetComponentInParent<EnemyHealth>();
            if (health != null && health.Config != null) return health.Config;
        }

        cachedDefault ??= EnemyStatsConfig.LoadDefault();
        return cachedDefault;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache() => cachedDefault = null;
}
