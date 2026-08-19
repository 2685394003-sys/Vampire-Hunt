using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using VampireHunt.Core;

namespace VampireHunt.Stats
{
    public readonly struct StatKey : IEquatable<StatKey>, IComparable<StatKey>
    {
        public ushort Value { get; }
        public bool IsValid => Value != 0;

        public StatKey(ushort value)
        {
            if (value == 0)
                throw new ArgumentOutOfRangeException(nameof(value), "StatKey zero is reserved for an invalid key.");
            Value = value;
        }

        public static StatKey Invalid => default;

        public bool Equals(StatKey other) => Value == other.Value;
        public override bool Equals(object obj) => obj is StatKey other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public int CompareTo(StatKey other) => Value.CompareTo(other.Value);
        public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
        public static bool operator ==(StatKey left, StatKey right) => left.Equals(right);
        public static bool operator !=(StatKey left, StatKey right) => !left.Equals(right);
    }

    public enum ModifierOperation
    {
        AddFlat = 0,
        AddPercent = 1,
        Multiply = 2,
        Override = 3
    }

    /// <summary>An immutable contribution to one stat from one logical source.</summary>
    public readonly struct StatModifier : IEquatable<StatModifier>
    {
        public StatKey Stat { get; }
        public ModifierOperation Operation { get; }
        public float Magnitude { get; }
        public EntityId SourceId { get; }

        public StatModifier(
            StatKey stat,
            ModifierOperation operation,
            float magnitude,
            EntityId sourceId)
        {
            if (!stat.IsValid)
                throw new ArgumentException("A stat modifier requires a valid stat key.", nameof(stat));
            if (!Enum.IsDefined(typeof(ModifierOperation), operation))
                throw new ArgumentOutOfRangeException(nameof(operation));
            if (!IsFinite(magnitude))
                throw new ArgumentOutOfRangeException(nameof(magnitude), "Modifier magnitude must be finite.");
            if (operation == ModifierOperation.Multiply && magnitude < 0f)
                throw new ArgumentOutOfRangeException(nameof(magnitude), "Multipliers cannot be negative.");
            if (!sourceId.IsValid)
                throw new ArgumentException("A stat modifier requires a valid source id.", nameof(sourceId));

            Stat = stat;
            Operation = operation;
            Magnitude = magnitude;
            SourceId = sourceId;
        }

        public bool Equals(StatModifier other) =>
            Stat == other.Stat && Operation == other.Operation &&
            Magnitude.Equals(other.Magnitude) && SourceId == other.SourceId;
        public override bool Equals(object obj) => obj is StatModifier other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Stat.GetHashCode();
                hash = (hash * 397) ^ (int)Operation;
                hash = (hash * 397) ^ Magnitude.GetHashCode();
                return (hash * 397) ^ SourceId.GetHashCode();
            }
        }

        public static bool operator ==(StatModifier left, StatModifier right) => left.Equals(right);
        public static bool operator !=(StatModifier left, StatModifier right) => !left.Equals(right);

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>
    /// Read-only copy of the modifiers at one instant. The collection itself is
    /// intentionally not an IStatSnapshot because it does not own base values.
    /// </summary>
    public readonly struct StatModifierSnapshot : IReadOnlyList<StatModifier>, IStatSnapshot
    {
        private readonly StatModifier[] values;

        internal StatModifierSnapshot(StatModifier[] values)
        {
            this.values = values ?? Array.Empty<StatModifier>();
        }

        public int Count => values == null ? 0 : values.Length;
        public StatModifier this[int index] => values[index];
        public IReadOnlyList<StatModifier> Modifiers => values == null
            ? Array.Empty<StatModifier>()
            : (IReadOnlyList<StatModifier>)((StatModifier[])values.Clone());

        /// <summary>
        /// Returns the contribution of this modifier-only snapshot evaluated
        /// against a zero base. Feature snapshots should supply their own base
        /// values; this implementation exists to keep the immutable snapshot
        /// consumable through the shared read interface without pretending to
        /// own those base values.
        /// </summary>
        public float GetValue(StatKey key)
        {
            if (!key.IsValid) return 0f;
            return new StatCalculator().Evaluate(key, 0f, this);
        }

        public IEnumerator<StatModifier> GetEnumerator()
        {
            if (values == null) yield break;
            for (int index = 0; index < values.Length; index++)
                yield return values[index];
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public sealed class StatModifierCollection
    {
        private readonly List<StatModifier> modifiers = new List<StatModifier>();

        public int Count => modifiers.Count;

        public void Add(StatModifier modifier) => modifiers.Add(modifier);

        public void AddRange(IEnumerable<StatModifier> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            foreach (StatModifier modifier in values) Add(modifier);
        }

        public bool Remove(StatModifier modifier) => modifiers.Remove(modifier);

        public int RemoveBySource(EntityId sourceId)
        {
            if (!sourceId.IsValid) return 0;
            int removed = 0;
            for (int index = modifiers.Count - 1; index >= 0; index--)
            {
                if (modifiers[index].SourceId != sourceId) continue;
                modifiers.RemoveAt(index);
                removed++;
            }
            return removed;
        }

        public int Remove(StatKey stat, EntityId sourceId)
        {
            if (!stat.IsValid || !sourceId.IsValid) return 0;
            int removed = 0;
            for (int index = modifiers.Count - 1; index >= 0; index--)
            {
                StatModifier modifier = modifiers[index];
                if (modifier.Stat != stat || modifier.SourceId != sourceId) continue;
                modifiers.RemoveAt(index);
                removed++;
            }
            return removed;
        }

        public StatModifierSnapshot Snapshot() =>
            new StatModifierSnapshot(modifiers.ToArray());

        public void Clear() => modifiers.Clear();
    }

    public interface IStatSnapshot
    {
        float GetValue(StatKey key);
    }

    /// <summary>Shared deterministic stat formula used by all feature modules.</summary>
    public sealed class StatCalculator
    {
        public float Evaluate(float baseValue, IEnumerable<StatModifier> modifiers)
        {
            ValidateFinite(baseValue, nameof(baseValue));
            if (modifiers == null) throw new ArgumentNullException(nameof(modifiers));

            bool hasOverride = false;
            float overrideValue = 0f;
            float flat = 0f;
            float additivePercent = 0f;
            float multiplicative = 1f;

            foreach (StatModifier modifier in modifiers)
            {
                switch (modifier.Operation)
                {
                    case ModifierOperation.Override:
                        // Modifiers are evaluated in their stable insertion order;
                        // the last override wins and takes precedence over the
                        // additive stages, matching an absolute stat override.
                        overrideValue = modifier.Magnitude;
                        hasOverride = true;
                        break;
                    case ModifierOperation.AddFlat:
                        flat += modifier.Magnitude;
                        break;
                    case ModifierOperation.AddPercent:
                        additivePercent += modifier.Magnitude;
                        break;
                    case ModifierOperation.Multiply:
                        multiplicative *= modifier.Magnitude;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(modifier), "Unknown modifier operation.");
                }
            }

            if (hasOverride)
            {
                ValidateFinite(overrideValue, "result");
                return overrideValue;
            }

            float result = (baseValue + flat) * (1f + additivePercent) * multiplicative;
            ValidateFinite(result, "result");
            return result;
        }

        public float Evaluate(StatKey key, float baseValue, IEnumerable<StatModifier> modifiers)
        {
            if (!key.IsValid) throw new ArgumentException("A valid stat key is required.", nameof(key));
            if (modifiers == null) throw new ArgumentNullException(nameof(modifiers));
            return Evaluate(baseValue, Filter(key, modifiers));
        }

        public float Evaluate(StatKey key, float baseValue, StatModifierSnapshot modifiers) =>
            Evaluate(key, baseValue, (IEnumerable<StatModifier>)modifiers);

        private static IEnumerable<StatModifier> Filter(StatKey key, IEnumerable<StatModifier> modifiers)
        {
            foreach (StatModifier modifier in modifiers)
                if (modifier.Stat == key)
                    yield return modifier;
        }

        private static void ValidateFinite(float value, string parameterName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(parameterName, "Stat values must be finite.");
        }
    }
}
