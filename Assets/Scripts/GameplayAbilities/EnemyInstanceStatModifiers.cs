using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-enemy modifiers layered on top of EnemyRunStats. This keeps debuffs local
/// to one target while preserving global run-wide enemy upgrades and curses.
/// </summary>
public sealed class EnemyInstanceStatModifiers
{
    private readonly struct Modifier
    {
        public readonly string SourceId;
        public readonly EnemyStatType Stat;
        public readonly PlayerModifierOperation Operation;
        public readonly float Value;

        public Modifier(
            string sourceId,
            EnemyStatType stat,
            PlayerModifierOperation operation,
            float value)
        {
            SourceId = sourceId;
            Stat = stat;
            Operation = operation;
            Value = value;
        }
    }

    private readonly SortedDictionary<string, Modifier> modifiers =
        new(StringComparer.Ordinal);

    public int Count => modifiers.Count;

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

        modifierId = modifierId.Trim();
        sourceId = string.IsNullOrWhiteSpace(sourceId) ? modifierId : sourceId.Trim();
        Modifier incoming = new(sourceId, stat, operation, value);
        if (modifiers.TryGetValue(modifierId, out Modifier existing))
        {
            return existing.Stat == incoming.Stat &&
                   existing.Operation == incoming.Operation &&
                   Mathf.Approximately(existing.Value, incoming.Value) &&
                   string.Equals(existing.SourceId, incoming.SourceId, StringComparison.Ordinal);
        }

        modifiers.Add(modifierId, incoming);
        return true;
    }

    public bool Remove(string modifierId, out EnemyStatType changedStat)
    {
        changedStat = default;
        if (string.IsNullOrWhiteSpace(modifierId) ||
            !modifiers.TryGetValue(modifierId, out Modifier modifier))
        {
            return false;
        }

        changedStat = modifier.Stat;
        return modifiers.Remove(modifierId);
    }

    public float Evaluate(EnemyStatType stat, float baseValue)
    {
        float flat = 0f;
        float additivePercent = 0f;
        float multiplier = 1f;
        foreach (KeyValuePair<string, Modifier> pair in modifiers)
        {
            Modifier modifier = pair.Value;
            if (modifier.Stat != stat) continue;
            switch (modifier.Operation)
            {
                case PlayerModifierOperation.Flat:
                    flat += modifier.Value;
                    break;
                case PlayerModifierOperation.AdditivePercent:
                    additivePercent += modifier.Value;
                    break;
                case PlayerModifierOperation.Multiplicative:
                    multiplier *= modifier.Value;
                    break;
            }
        }

        float minimum = stat switch
        {
            EnemyStatType.MaxHealth => 1f,
            EnemyStatType.AttackCooldown => 0.01f,
            _ => 0f
        };
        return Mathf.Max(minimum, (baseValue + flat) * (1f + additivePercent) * multiplier);
    }

    public void Clear() => modifiers.Clear();
}
