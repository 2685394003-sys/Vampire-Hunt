using System;
using System.Collections.Generic;
using UnityEngine;

public enum EnemyStatType
{
    MaxHealth = 0,
    Damage = 1,
    MoveSpeed = 2,
    WeaponRange = 3,
    AttackCooldown = 4,
    KnockbackForce = 5,
    ProjectileDamage = 6,
    EnhancedProjectileDamage = 7
}

[Serializable]
public struct EnemyStatModifier
{
    public string modifierId;
    public string sourceId;
    public EnemyStatType stat;
    public PlayerModifierOperation operation;
    public float value;

    public EnemyStatModifier(
        string modifierId,
        string sourceId,
        EnemyStatType stat,
        PlayerModifierOperation operation,
        float value)
    {
        this.modifierId = modifierId;
        this.sourceId = sourceId;
        this.stat = stat;
        this.operation = operation;
        this.value = value;
    }
}

/// <summary>
/// Global enemy-side run modifiers. Enemy simulation is server-authoritative, so
/// callers read the derived value at the point of use rather than replicating it.
/// </summary>
public static class EnemyRunStats
{
    private static readonly SortedDictionary<string, EnemyStatModifier> Modifiers =
        new(StringComparer.Ordinal);

    public static event Action<EnemyStatType> StatChanged;

    public static bool AddModifier(EnemyStatModifier modifier)
    {
        if (!NetworkAuthority.IsServerOrOffline() ||
            !Enum.IsDefined(typeof(EnemyStatType), modifier.stat) ||
            !Enum.IsDefined(typeof(PlayerModifierOperation), modifier.operation) ||
            string.IsNullOrWhiteSpace(modifier.modifierId) ||
            float.IsNaN(modifier.value) ||
            float.IsInfinity(modifier.value) ||
            (modifier.operation == PlayerModifierOperation.Multiplicative &&
             modifier.value < 0f))
        {
            return false;
        }

        modifier.modifierId = modifier.modifierId.Trim();
        modifier.sourceId = string.IsNullOrWhiteSpace(modifier.sourceId)
            ? modifier.modifierId
            : modifier.sourceId.Trim();

        if (Modifiers.TryGetValue(modifier.modifierId, out EnemyStatModifier existing))
        {
            return DefinitionsMatch(existing, modifier);
        }

        Modifiers.Add(modifier.modifierId, modifier);
        StatChanged?.Invoke(modifier.stat);
        return true;
    }

    public static int RemoveModifiersFromSource(string sourceId)
    {
        if (!NetworkAuthority.IsServerOrOffline() || string.IsNullOrWhiteSpace(sourceId))
            return 0;

        List<string> removals = null;
        HashSet<EnemyStatType> changedStats = new();
        foreach (KeyValuePair<string, EnemyStatModifier> pair in Modifiers)
        {
            if (!string.Equals(pair.Value.sourceId, sourceId, StringComparison.Ordinal))
                continue;

            removals ??= new List<string>();
            removals.Add(pair.Key);
            changedStats.Add(pair.Value.stat);
        }

        if (removals == null) return 0;
        foreach (string modifierId in removals) Modifiers.Remove(modifierId);
        foreach (EnemyStatType stat in changedStats) StatChanged?.Invoke(stat);
        return removals.Count;
    }

    public static float GetValue(EnemyStatsConfig config, EnemyStatType stat)
    {
        if (config == null) return GetMinimum(stat);

        float baseValue = config.GetBaseValue(stat);
        float flat = 0f;
        float additivePercent = 0f;
        float multiplier = 1f;
        foreach (KeyValuePair<string, EnemyStatModifier> pair in Modifiers)
        {
            EnemyStatModifier modifier = pair.Value;
            if (modifier.stat != stat) continue;

            switch (modifier.operation)
            {
                case PlayerModifierOperation.Flat:
                    flat += modifier.value;
                    break;
                case PlayerModifierOperation.AdditivePercent:
                    additivePercent += modifier.value;
                    break;
                case PlayerModifierOperation.Multiplicative:
                    multiplier *= modifier.value;
                    break;
            }
        }

        return Mathf.Max(
            GetMinimum(stat),
            (baseValue + flat) * (1f + additivePercent) * multiplier);
    }

    public static int GetRoundedValue(EnemyStatsConfig config, EnemyStatType stat) =>
        Mathf.RoundToInt(GetValue(config, stat));

    public static void ResetForNewRun()
    {
        if (!NetworkAuthority.IsServerOrOffline() || Modifiers.Count == 0) return;

        HashSet<EnemyStatType> changedStats = new();
        foreach (EnemyStatModifier modifier in Modifiers.Values)
            changedStats.Add(modifier.stat);
        Modifiers.Clear();
        foreach (EnemyStatType stat in changedStats) StatChanged?.Invoke(stat);
    }

    private static float GetMinimum(EnemyStatType stat) => stat switch
    {
        EnemyStatType.MaxHealth => 1f,
        EnemyStatType.AttackCooldown => 0.01f,
        _ => 0f
    };

    private static bool DefinitionsMatch(EnemyStatModifier left, EnemyStatModifier right) =>
        left.stat == right.stat &&
        left.operation == right.operation &&
        Mathf.Approximately(left.value, right.value) &&
        string.Equals(left.sourceId, right.sourceId, StringComparison.Ordinal);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        Modifiers.Clear();
        StatChanged = null;
    }
}

/// <summary>Immutable baseline for one enemy archetype authored by design.</summary>
[CreateAssetMenu(
    fileName = "EnemyStats",
    menuName = "Vampire Hunt/Balance/Enemy Stats",
    order = 2)]
public sealed class EnemyStatsConfig : ScriptableObject
{
    private const string DefaultResourcePath = "GameBalance/EnemyDefaultStats";

    [Header("策划标识 / Design Identity")]
    public string enemyId = "enemy_0001";
    public string enemyName = "近战小怪";

    [Header("近战 / Melee")]
    [Min(1)] public int maxHealth = 30;
    [Min(0)] public int damage = 3;
    [Min(0f)] public float moveSpeed = 4f;
    [Min(0f)] public float weaponRange = 1.5f;
    [Min(0f)] public float knockbackForce = 0.2f;
    [Min(0f)] public float knockbackTime = 0.2f;
    [Min(0.01f)] public float attackCooldown = 1.2f;
    [Min(0f)] public float stunTime = 0.3f;

    [Header("弹反 / Parry")]
    public bool canBeParried;
    [Min(0f)] public float parryWindupTime;

    [Header("掉落 / Drops")]
    [Min(0)] public int redResourceDropAmount = 1;
    [Min(0)] public int coinDropAmount = 5;
    [Range(0f, 1f)] public float healthPackDropChance = 0.3f;

    [Header("远程 / Ranged")]
    public bool isRanged;
    [Min(0)] public int projectileDamage;
    [Min(0)] public int enhancedProjectileDamage;
    [Min(0f)] public float shootCooldown;
    [Min(0f)] public float projectileLifetime;
    [Min(0f)] public float projectileSpeed = 12f;
    [Min(0f)] public float shootWaitTime = 0.5f;
    [Min(0f)] public float shootRange = 15f;
    [Tooltip("策划表 Z 列：索敌范围内的攻击冷却。")]
    [Min(0f)] public float inRangeAttackCooldown;
    [Tooltip("策划表 AA 列：远程小怪徘徊范围，当前表内未填值。")]
    [Min(0f)] public float rangedPatrolRange;

    [Header("受伤反馈 / Hurt Feedback")]
    [Min(0f)] public float hurtFlashDuration = 0.2f;
    [Tooltip("每秒闪烁次数 / flashes per second")]
    [Min(0.01f)] public float hurtFlashSpeed = 15f;

    [Header("系统补充参数 / Supplemental Settings")]
    [Min(0f)] public float playerDetectRange = 5f;
    [Min(0f)] public float slowDeceleration = 3f;
    public LayerMask playerLayer = 1 << 6;

    public float GetBaseValue(EnemyStatType stat) => stat switch
    {
        EnemyStatType.MaxHealth => maxHealth,
        EnemyStatType.Damage => damage,
        EnemyStatType.MoveSpeed => moveSpeed,
        EnemyStatType.WeaponRange => weaponRange,
        EnemyStatType.AttackCooldown => attackCooldown,
        EnemyStatType.KnockbackForce => knockbackForce,
        EnemyStatType.ProjectileDamage => projectileDamage,
        EnemyStatType.EnhancedProjectileDamage => enhancedProjectileDamage,
        _ => 0f
    };

    public static EnemyStatsConfig LoadDefault() =>
        Resources.Load<EnemyStatsConfig>(DefaultResourcePath);

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(enemyId)) enemyId = name;
        if (string.IsNullOrWhiteSpace(enemyName)) enemyName = name;
        maxHealth = Mathf.Max(1, maxHealth);
        damage = Mathf.Max(0, damage);
        moveSpeed = Mathf.Max(0f, moveSpeed);
        weaponRange = Mathf.Max(0f, weaponRange);
        knockbackForce = Mathf.Max(0f, knockbackForce);
        knockbackTime = Mathf.Max(0f, knockbackTime);
        attackCooldown = Mathf.Max(0.01f, attackCooldown);
        stunTime = Mathf.Max(0f, stunTime);
        parryWindupTime = Mathf.Max(0f, parryWindupTime);
        redResourceDropAmount = Mathf.Max(0, redResourceDropAmount);
        coinDropAmount = Mathf.Max(0, coinDropAmount);
        healthPackDropChance = Mathf.Clamp01(healthPackDropChance);
        projectileDamage = Mathf.Max(0, projectileDamage);
        enhancedProjectileDamage = Mathf.Max(0, enhancedProjectileDamage);
        shootCooldown = Mathf.Max(0f, shootCooldown);
        projectileLifetime = Mathf.Max(0f, projectileLifetime);
        projectileSpeed = Mathf.Max(0f, projectileSpeed);
        shootWaitTime = Mathf.Max(0f, shootWaitTime);
        shootRange = Mathf.Max(0f, shootRange);
        inRangeAttackCooldown = Mathf.Max(0f, inRangeAttackCooldown);
        rangedPatrolRange = Mathf.Max(0f, rangedPatrolRange);
        hurtFlashDuration = Mathf.Max(0f, hurtFlashDuration);
        hurtFlashSpeed = Mathf.Max(0.01f, hurtFlashSpeed);
        playerDetectRange = Mathf.Max(0f, playerDetectRange);
        slowDeceleration = Mathf.Max(0f, slowDeceleration);
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
