using System;
using System.Collections.Generic;
using VampireHunt.Stats;
using UnityEngine;
using CoreEntityId = VampireHunt.Core.EntityId;

/// <summary>
/// Serialization/source-compatibility shell for the old enemy modifier API.
///
/// The rule implementation is now owned by <see cref="StatModifierCollection"/>
/// and <see cref="StatCalculator"/>. This wrapper only translates the legacy
/// enum/string surface used by the still-migrating EnemyHealth component. It
/// deliberately keeps modifier IDs and handles separate so removing one effect
/// cannot remove a sibling effect from the same source.
/// </summary>
[Obsolete("Use VampireHunt.Stats.StatModifierCollection from the enemy runtime.")]
public sealed class EnemyInstanceStatModifiers
{
    private readonly StatModifierCollection modifiers = new();
    private readonly Dictionary<string, RegisteredModifier> registrations =
        new(StringComparer.Ordinal);

    public int Count => registrations.Count;

    public bool Add(
        string modifierId,
        string sourceId,
        EnemyStatType stat,
        PlayerModifierOperation operation,
        float value)
    {
        if (string.IsNullOrWhiteSpace(modifierId) ||
            !Enum.IsDefined(typeof(EnemyStatType), stat) ||
            !Enum.IsDefined(typeof(PlayerModifierOperation), operation) ||
            float.IsNaN(value) || float.IsInfinity(value) ||
            (operation == PlayerModifierOperation.Multiplicative && value < 0f))
        {
            return false;
        }

        string normalizedId = modifierId.Trim();
        string normalizedSource = string.IsNullOrWhiteSpace(sourceId)
            ? normalizedId
            : sourceId.Trim();
        RegisteredModifier incoming = new(
            normalizedSource,
            stat,
            operation,
            value,
            CreateDomainModifier(normalizedSource, stat, operation, value));

        if (registrations.TryGetValue(normalizedId, out RegisteredModifier existing))
        {
            // Preserve the legacy idempotent-add contract. A reused id with a
            // different definition is rejected instead of silently replacing
            // an active effect.
            return existing.Matches(incoming);
        }

        StatModifierHandle handle = modifiers.AddWithHandle(incoming.DomainModifier);
        registrations.Add(normalizedId, incoming.WithHandle(handle));
        return true;
    }

    public bool Remove(string modifierId, out EnemyStatType changedStat)
    {
        changedStat = default;
        if (string.IsNullOrWhiteSpace(modifierId) ||
            !registrations.TryGetValue(modifierId.Trim(), out RegisteredModifier registration))
        {
            return false;
        }

        changedStat = registration.Stat;
        bool removed = modifiers.Remove(registration.Handle);
        if (removed) registrations.Remove(modifierId.Trim());
        return removed;
    }

    public float Evaluate(EnemyStatType stat, float baseValue)
    {
        StatKey key = ToStatKey(stat);
        float value = new StatCalculator().Evaluate(key, baseValue, modifiers.Snapshot());
        float minimum = stat switch
        {
            EnemyStatType.MaxHealth => 1f,
            EnemyStatType.AttackCooldown => 0.01f,
            _ => 0f
        };
        return Mathf.Max(minimum, value);
    }

    public void Clear()
    {
        modifiers.Clear();
        registrations.Clear();
    }

    private static StatModifier CreateDomainModifier(
        string sourceId,
        EnemyStatType stat,
        PlayerModifierOperation operation,
        float value)
    {
        return new StatModifier(
            ToStatKey(stat),
            ToModifierOperation(operation),
            value,
            ToEntityId(sourceId));
    }

    private static StatKey ToStatKey(EnemyStatType stat) =>
        new StatKey((ushort)((int)stat + 1));

    private static ModifierOperation ToModifierOperation(PlayerModifierOperation operation) =>
        operation switch
        {
            PlayerModifierOperation.Flat => ModifierOperation.AddFlat,
            PlayerModifierOperation.AdditivePercent => ModifierOperation.AddPercent,
            PlayerModifierOperation.Multiplicative => ModifierOperation.Multiply,
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };

    private static CoreEntityId ToEntityId(string sourceId)
    {
        // Domain StatModifier uses EntityId rather than an arbitrary string.
        // FNV-1a keeps the compatibility projection deterministic across server
        // and offline runs while reserving zero for EntityId.Invalid.
        unchecked
        {
            ulong hash = 14695981039346656037UL;
            for (int index = 0; index < sourceId.Length; index++)
            {
                hash ^= sourceId[index];
                hash *= 1099511628211UL;
            }
            if (hash == 0UL) hash = 1UL;
            return new CoreEntityId(hash);
        }
    }

    private readonly struct RegisteredModifier
    {
        public readonly string SourceId;
        public readonly EnemyStatType Stat;
        public readonly PlayerModifierOperation Operation;
        public readonly float Value;
        public readonly StatModifier DomainModifier;
        public readonly StatModifierHandle Handle;

        public RegisteredModifier(
            string sourceId,
            EnemyStatType stat,
            PlayerModifierOperation operation,
            float value,
            StatModifier domainModifier,
            StatModifierHandle handle = default)
        {
            SourceId = sourceId;
            Stat = stat;
            Operation = operation;
            Value = value;
            DomainModifier = domainModifier;
            Handle = handle;
        }

        public RegisteredModifier WithHandle(StatModifierHandle handle) =>
            new(SourceId, Stat, Operation, Value, DomainModifier, handle);

        public bool Matches(RegisteredModifier other) =>
            Stat == other.Stat &&
            Operation == other.Operation &&
            Mathf.Approximately(Value, other.Value) &&
            string.Equals(SourceId, other.SourceId, StringComparison.Ordinal);
    }
}
