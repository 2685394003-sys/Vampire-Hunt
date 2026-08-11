using System;
using System.Collections.Generic;
using UnityEngine;

// Explicit values keep serialized upgrade assets stable when new stats are added.
public enum PlayerStatType
{
    MaxHealth = 0,
    MaxStamina = 1,
    DashStaminaCost = 2,
    StaminaRecovery = 3,
    BaseAttack = 4,
    AttackRange = 5,
    AttackInterval = 6,
    KnockbackForce = 7,
    MoveSpeed = 8,
    CritRate = 9,
    CritDamage = 10,
    InvincibleTime = 11,
    FlashSpeed = 12,
    MaxScarlet = 13
}

/// <summary>Legacy one-shot upgrade operation kept for existing callers.</summary>
public enum PlayerStatOperation
{
    Add = 0,
    Multiply = 1
}

/// <summary>
/// Deterministic aggregation stages. The final formula is:
/// (base + flat) * (1 + additivePercent) * multiplicative.
/// </summary>
public enum PlayerModifierOperation
{
    Flat = 0,
    AdditivePercent = 1,
    Multiplicative = 2
}

/// <summary>
/// Serializable modifier definition used by upgrades, buffs, equipment and curses.
/// durationSeconds <= 0 means the modifier is permanent for the current run.
/// </summary>
[Serializable]
public struct PlayerStatModifier
{
    public string modifierId;
    public string sourceId;
    public PlayerStatType stat;
    public PlayerModifierOperation operation;
    public float value;
    [Min(1)] public int stacks;
    [Min(1)] public int maxStacks;
    [Min(0f)] public float durationSeconds;

    public PlayerStatModifier(
        string modifierId,
        string sourceId,
        PlayerStatType stat,
        PlayerModifierOperation operation,
        float value,
        int stacks = 1,
        int maxStacks = 1,
        float durationSeconds = 0f)
    {
        this.modifierId = modifierId;
        this.sourceId = sourceId;
        this.stat = stat;
        this.operation = operation;
        this.value = value;
        this.stacks = Mathf.Max(1, stacks);
        this.maxStacks = Mathf.Max(1, maxStacks);
        this.durationSeconds = Mathf.Max(0f, durationSeconds);
    }
}

/// <summary>
/// Compatibility payload. New systems should author PlayerStatModifier directly.
/// Multiply uses a direct factor: 1.1 means +10%, 0.9 means -10%.
/// </summary>
[Serializable]
public struct PlayerStatUpgrade
{
    public PlayerStatType stat;
    public PlayerStatOperation operation;
    public float value;

    public PlayerStatUpgrade(
        PlayerStatType stat,
        float value,
        PlayerStatOperation operation = PlayerStatOperation.Add)
    {
        this.stat = stat;
        this.value = value;
        this.operation = operation;
    }
}

/// <summary>Auditable contribution breakdown for UI, debugging and tests.</summary>
public readonly struct PlayerStatBreakdown
{
    public readonly PlayerStatType Stat;
    public readonly float BaseValue;
    public readonly float FlatBonus;
    public readonly float AdditivePercent;
    public readonly float MultiplicativeFactor;
    public readonly float UnclampedValue;
    public readonly float FinalValue;
    public readonly int ModifierCount;

    public PlayerStatBreakdown(
        PlayerStatType stat,
        float baseValue,
        float flatBonus,
        float additivePercent,
        float multiplicativeFactor,
        float unclampedValue,
        float finalValue,
        int modifierCount)
    {
        Stat = stat;
        BaseValue = baseValue;
        FlatBonus = flatBonus;
        AdditivePercent = additivePercent;
        MultiplicativeFactor = multiplicativeFactor;
        UnclampedValue = unclampedValue;
        FinalValue = finalValue;
        ModifierCount = modifierCount;
    }
}

/// <summary>
/// Server-side modifier store. A sorted key keeps floating-point aggregation order
/// stable for a given modifier set. Only final derived stats need network sync.
/// </summary>
public sealed class PlayerStatModifierCollection
{
    private sealed class ActiveModifier
    {
        public PlayerStatModifier Definition;
        public int Stacks;
        public float RemainingSeconds;
    }

    private readonly SortedDictionary<string, ActiveModifier> modifiers =
        new(StringComparer.Ordinal);

    public int Count => modifiers.Count;

    public bool AddOrStack(PlayerStatModifier modifier, out PlayerStatType changedStat)
    {
        changedStat = modifier.stat;
        if (!TryNormalize(ref modifier)) return false;

        if (!modifiers.TryGetValue(modifier.modifierId, out ActiveModifier active))
        {
            modifiers.Add(modifier.modifierId, new ActiveModifier
            {
                Definition = modifier,
                Stacks = Mathf.Min(modifier.stacks, modifier.maxStacks),
                RemainingSeconds = modifier.durationSeconds
            });
            return true;
        }

        if (!DefinitionsMatch(active.Definition, modifier)) return false;

        active.Stacks = Mathf.Min(
            active.Definition.maxStacks,
            active.Stacks + modifier.stacks);
        if (active.Definition.durationSeconds > 0f)
            active.RemainingSeconds = active.Definition.durationSeconds;
        return true;
    }

    public bool Remove(string modifierId, out PlayerStatType changedStat)
    {
        changedStat = default;
        if (string.IsNullOrWhiteSpace(modifierId) ||
            !modifiers.TryGetValue(modifierId, out ActiveModifier active))
        {
            return false;
        }

        changedStat = active.Definition.stat;
        return modifiers.Remove(modifierId);
    }

    public int RemoveBySource(string sourceId, HashSet<PlayerStatType> changedStats)
    {
        if (string.IsNullOrWhiteSpace(sourceId)) return 0;

        List<string> removals = null;
        foreach (KeyValuePair<string, ActiveModifier> pair in modifiers)
        {
            if (!string.Equals(
                    pair.Value.Definition.sourceId,
                    sourceId,
                    StringComparison.Ordinal))
            {
                continue;
            }

            removals ??= new List<string>();
            removals.Add(pair.Key);
            changedStats?.Add(pair.Value.Definition.stat);
        }

        if (removals == null) return 0;
        foreach (string modifierId in removals) modifiers.Remove(modifierId);
        return removals.Count;
    }

    public int Tick(float deltaTime, HashSet<PlayerStatType> changedStats)
    {
        if (deltaTime <= 0f || modifiers.Count == 0) return 0;

        List<string> expired = null;
        foreach (KeyValuePair<string, ActiveModifier> pair in modifiers)
        {
            ActiveModifier active = pair.Value;
            if (active.Definition.durationSeconds <= 0f) continue;

            active.RemainingSeconds -= deltaTime;
            if (active.RemainingSeconds > 0f) continue;

            expired ??= new List<string>();
            expired.Add(pair.Key);
            changedStats?.Add(active.Definition.stat);
        }

        if (expired == null) return 0;
        foreach (string modifierId in expired) modifiers.Remove(modifierId);
        return expired.Count;
    }

    public void Clear() => modifiers.Clear();

    public PlayerStatBreakdown Evaluate(
        PlayerStatType stat,
        float baseValue,
        float minimum,
        float maximum)
    {
        float flat = 0f;
        float additivePercent = 0f;
        float multiplicative = 1f;
        int count = 0;

        foreach (KeyValuePair<string, ActiveModifier> pair in modifiers)
        {
            ActiveModifier active = pair.Value;
            PlayerStatModifier definition = active.Definition;
            if (definition.stat != stat) continue;

            count++;
            switch (definition.operation)
            {
                case PlayerModifierOperation.Flat:
                    flat += definition.value * active.Stacks;
                    break;
                case PlayerModifierOperation.AdditivePercent:
                    additivePercent += definition.value * active.Stacks;
                    break;
                case PlayerModifierOperation.Multiplicative:
                    multiplicative *= Mathf.Pow(definition.value, active.Stacks);
                    break;
            }
        }

        float unclamped = (baseValue + flat) * (1f + additivePercent) * multiplicative;
        float finalValue = Mathf.Clamp(unclamped, minimum, maximum);
        return new PlayerStatBreakdown(
            stat,
            baseValue,
            flat,
            additivePercent,
            multiplicative,
            unclamped,
            finalValue,
            count);
    }

    private static bool TryNormalize(ref PlayerStatModifier modifier)
    {
        if (!Enum.IsDefined(typeof(PlayerStatType), modifier.stat) ||
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
        modifier.stacks = Mathf.Max(1, modifier.stacks);
        modifier.maxStacks = Mathf.Max(1, modifier.maxStacks);
        modifier.stacks = Mathf.Min(modifier.stacks, modifier.maxStacks);
        modifier.durationSeconds = Mathf.Max(0f, modifier.durationSeconds);
        return true;
    }

    private static bool DefinitionsMatch(
        PlayerStatModifier left,
        PlayerStatModifier right)
    {
        return left.stat == right.stat &&
               left.operation == right.operation &&
               Mathf.Approximately(left.value, right.value) &&
               left.maxStacks == right.maxStacks &&
               Mathf.Approximately(left.durationSeconds, right.durationSeconds) &&
               string.Equals(left.sourceId, right.sourceId, StringComparison.Ordinal);
    }
}

/// <summary>Read/write boundary for upgrade cards, pickups, buffs and shops.</summary>
public interface IPlayerRunStats
{
    string PlayerId { get; }
    int MaxHealth { get; }
    int CurrentHealth { get; }
    float MaxStamina { get; }
    float CurrentStamina { get; }
    float DashStaminaCost { get; }
    float StaminaRecoverySpeed { get; }
    float Damage { get; }
    float WeaponRange { get; }
    float AttackCooldown { get; }
    float KnockbackForce { get; }
    float MoveSpeed { get; }
    float CritRate { get; }
    float CritDamage { get; }
    float InvincibleTime { get; }
    float FlashSpeed { get; }
    float MaxScarlet { get; }
    float CurrentScarlet { get; }

    bool AddStatModifier(PlayerStatModifier modifier);
    bool RemoveStatModifier(string modifierId);
    int RemoveStatModifiersFromSource(string sourceId);
    bool TryGetStatBreakdown(PlayerStatType stat, out PlayerStatBreakdown breakdown);
    bool ApplyRunUpgrade(PlayerStatUpgrade upgrade);
    void ResetForNewRun();
}
