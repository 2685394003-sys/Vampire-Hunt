using System;
using System.Collections.Generic;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;
using VampireHunt.Stats;

namespace VampireHunt.Abilities.Contracts
{
    public enum GameplayCuePhase
    {
        Applied = 0,
        Executed = 1,
        Removed = 2
    }

    public readonly struct EffectId : IEquatable<EffectId>, IComparable<EffectId>
    {
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public static EffectId Invalid => default;

        public EffectId(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            value = value.Trim();
            if (value.Length == 0) throw new ArgumentException("EffectId cannot be empty.", nameof(value));
            Value = value;
        }

        public bool Equals(EffectId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is EffectId other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);
        public int CompareTo(EffectId other) => string.Compare(Value, other.Value, StringComparison.Ordinal);
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(EffectId left, EffectId right) => left.Equals(right);
        public static bool operator !=(EffectId left, EffectId right) => !left.Equals(right);
    }

    public readonly struct CueId : IEquatable<CueId>, IComparable<CueId>
    {
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public static CueId None => default;

        public CueId(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            value = value.Trim();
            if (value.Length == 0)
            {
                Value = string.Empty;
                return;
            }
            Value = value;
        }

        public bool Equals(CueId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is CueId other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);
        public int CompareTo(CueId other) => string.Compare(Value, other.Value, StringComparison.Ordinal);
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(CueId left, CueId right) => left.Equals(right);
        public static bool operator !=(CueId left, CueId right) => !left.Equals(right);
    }

    public enum DurationPolicy
    {
        Instant = 0,
        Duration = 1,
        Infinite = 2
    }

    public enum StackingPolicy
    {
        RefreshDuration = 0,
        AddStacks = 1,
        Replace = 2
    }

    public enum GameplayExecutionType
    {
        Damage = 0,
        Healing = 1,
        Attribute = 2,
        Modifier = 2
    }

    /// <summary>Descriptive alias used by callers that model executions explicitly.</summary>
    public enum GameplayEffectExecutionType
    {
        Damage = GameplayExecutionType.Damage,
        Healing = GameplayExecutionType.Healing,
        Attribute = GameplayExecutionType.Attribute
    }

    /// <summary>Authoring-independent modifier description; source is bound at runtime.</summary>
    public readonly struct GameplayModifierSpec : IEquatable<GameplayModifierSpec>
    {
        public StatKey Stat { get; }
        public ModifierOperation Operation { get; }
        public float Magnitude { get; }

        public GameplayModifierSpec(StatKey stat, ModifierOperation operation, float magnitude)
        {
            if (!stat.IsValid) throw new ArgumentException("A valid stat key is required.", nameof(stat));
            if (!Enum.IsDefined(typeof(ModifierOperation), operation))
                throw new ArgumentOutOfRangeException(nameof(operation));
            if (float.IsNaN(magnitude) || float.IsInfinity(magnitude))
                throw new ArgumentOutOfRangeException(nameof(magnitude));
            if (operation == ModifierOperation.Multiply && magnitude < 0f)
                throw new ArgumentOutOfRangeException(nameof(magnitude));
            Stat = stat;
            Operation = operation;
            Magnitude = magnitude;
        }

        public StatModifier Bind(EntityId sourceId) =>
            new StatModifier(Stat, Operation, Magnitude, sourceId);

        public bool Equals(GameplayModifierSpec other) =>
            Stat == other.Stat && Operation == other.Operation && Magnitude.Equals(other.Magnitude);
        public override bool Equals(object obj) => obj is GameplayModifierSpec other && Equals(other);
        public override int GetHashCode()
        {
            unchecked { return ((Stat.GetHashCode() * 397) ^ (int)Operation) * 397 ^ Magnitude.GetHashCode(); }
        }
        public static bool operator ==(GameplayModifierSpec left, GameplayModifierSpec right) => left.Equals(right);
        public static bool operator !=(GameplayModifierSpec left, GameplayModifierSpec right) => !left.Equals(right);
    }

    public readonly struct GameplayEffectExecution : IEquatable<GameplayEffectExecution>
    {
        public GameplayExecutionType Type { get; }
        public float Magnitude { get; }
        public DamageFlags DamageFlags { get; }
        public GameplayModifierSpec Modifier { get; }

        public GameplayEffectExecution(
            GameplayExecutionType type,
            float magnitude,
            DamageFlags damageFlags = DamageFlags.None)
        {
            if (!Enum.IsDefined(typeof(GameplayExecutionType), type))
                throw new ArgumentOutOfRangeException(nameof(type));
            if (magnitude < 0f || float.IsNaN(magnitude) || float.IsInfinity(magnitude))
                throw new ArgumentOutOfRangeException(nameof(magnitude));
            DamageRequest.ValidateFlags(damageFlags);
            Type = type;
            Magnitude = magnitude;
            DamageFlags = damageFlags;
            Modifier = default;
        }

        public GameplayEffectExecution(GameplayModifierSpec modifier)
        {
            Type = GameplayExecutionType.Attribute;
            Magnitude = 1f;
            DamageFlags = DamageFlags.None;
            Modifier = modifier;
        }

        public GameplayEffectExecution(
            GameplayEffectExecutionType type,
            float magnitude,
            DamageFlags damageFlags = DamageFlags.None)
            : this((GameplayExecutionType)type, magnitude, damageFlags) { }

        public static GameplayEffectExecution Attribute(GameplayModifierSpec modifier) =>
            new GameplayEffectExecution(modifier);

        public static GameplayEffectExecution Damage(float amount, DamageFlags flags = DamageFlags.None) =>
            new GameplayEffectExecution(GameplayExecutionType.Damage, amount, flags);
        public static GameplayEffectExecution Healing(float amount) =>
            new GameplayEffectExecution(GameplayExecutionType.Healing, amount);

        public bool Equals(GameplayEffectExecution other) =>
            Type == other.Type && Magnitude.Equals(other.Magnitude) && DamageFlags == other.DamageFlags &&
            Modifier == other.Modifier;
        public override bool Equals(object obj) => obj is GameplayEffectExecution other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = ((int)Type * 397 ^ Magnitude.GetHashCode()) * 397 ^ (int)DamageFlags;
                return (hash * 397) ^ Modifier.GetHashCode();
            }
        }
        public static bool operator ==(GameplayEffectExecution left, GameplayEffectExecution right) => left.Equals(right);
        public static bool operator !=(GameplayEffectExecution left, GameplayEffectExecution right) => !left.Equals(right);
    }

    public readonly struct GameplayEffectContext
    {
        public EntityId SourceId { get; }
        public EntityId TargetId { get; }
        public HitContext Hit { get; }
        public IStatSnapshot SourceStats { get; }

        public GameplayEffectContext(
            EntityId sourceId,
            EntityId targetId,
            HitContext hit = default,
            IStatSnapshot sourceStats = null)
        {
            if (!sourceId.IsValid) throw new ArgumentException("A valid source id is required.", nameof(sourceId));
            if (!targetId.IsValid) throw new ArgumentException("A valid target id is required.", nameof(targetId));
            SourceId = sourceId;
            TargetId = targetId;
            Hit = hit;
            SourceStats = sourceStats;
        }
    }

    /// <summary>
    /// Immutable runtime specification created from authoring data. Lists are
    /// copied on construction and on exposure so no caller can mutate a spec.
    /// </summary>
    public sealed class GameplayEffectSpec
    {
        private readonly GameplayModifierSpec[] modifiers;
        private readonly GameplayEffectExecution[] executions;

        public EffectId Id { get; }
        public EffectId EffectId => Id;
        public DurationPolicy Duration { get; }
        public StackingPolicy Stacking { get; }
        public DurationPolicy DurationPolicy => Duration;
        public StackingPolicy StackingPolicy => Stacking;
        public float DurationSeconds { get; }
        public float PeriodSeconds { get; }
        public float Period => PeriodSeconds;
        public int MaxStacks { get; }
        public bool ExecuteOnApplication { get; }
        public CueId Cue { get; }
        public bool IsInstant => Duration == DurationPolicy.Instant;
        public IReadOnlyList<GameplayModifierSpec> Modifiers =>
            (GameplayModifierSpec[])modifiers.Clone();
        public IReadOnlyList<GameplayEffectExecution> Executions =>
            (GameplayEffectExecution[])executions.Clone();

        public GameplayEffectSpec(
            EffectId id,
            DurationPolicy duration,
            StackingPolicy stacking,
            float durationSeconds = 0f,
            float periodSeconds = 0f,
            int maxStacks = 1,
            bool executeOnApplication = true,
            IEnumerable<GameplayModifierSpec> modifiers = null,
            IEnumerable<GameplayEffectExecution> executions = null,
            CueId cue = default)
        {
            if (!id.IsValid) throw new ArgumentException("A valid effect id is required.", nameof(id));
            if (!Enum.IsDefined(typeof(DurationPolicy), duration))
                throw new ArgumentOutOfRangeException(nameof(duration));
            if (!Enum.IsDefined(typeof(StackingPolicy), stacking))
                throw new ArgumentOutOfRangeException(nameof(stacking));
            if (durationSeconds < 0f || float.IsNaN(durationSeconds) || float.IsInfinity(durationSeconds))
                throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            if (periodSeconds < 0f || float.IsNaN(periodSeconds) || float.IsInfinity(periodSeconds))
                throw new ArgumentOutOfRangeException(nameof(periodSeconds));
            if (maxStacks < 1) throw new ArgumentOutOfRangeException(nameof(maxStacks));
            if (duration == DurationPolicy.Duration && durationSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(durationSeconds), "Duration effects must last for a positive duration.");
            if (duration != DurationPolicy.Duration && durationSeconds > 0f)
                throw new ArgumentException("Only Duration effects may specify DurationSeconds.", nameof(durationSeconds));
            if (duration == DurationPolicy.Instant && periodSeconds > 0f)
                throw new ArgumentException("Instant effects cannot be periodic.", nameof(periodSeconds));

            this.modifiers = modifiers == null ? Array.Empty<GameplayModifierSpec>() : new List<GameplayModifierSpec>(modifiers).ToArray();
            this.executions = executions == null ? Array.Empty<GameplayEffectExecution>() : new List<GameplayEffectExecution>(executions).ToArray();
            if (duration == DurationPolicy.Instant && this.modifiers.Length > 0)
                throw new ArgumentException("Instant effects cannot own persistent modifiers.", nameof(modifiers));

            Id = id;
            Duration = duration;
            Stacking = stacking;
            DurationSeconds = durationSeconds;
            PeriodSeconds = periodSeconds;
            MaxStacks = maxStacks;
            ExecuteOnApplication = executeOnApplication;
            Cue = cue;
        }
    }

    public interface IAttributeModifierTarget
    {
        void AddModifier(StatModifier modifier);
        int RemoveModifiers(EntityId sourceId);
    }

    public sealed class GameplayCueEvent : GameplayEventBase
    {
        public CueId Cue { get; }
        public EntityId TargetId { get; }
        public WorldPosition Position { get; }
        public GameplayCuePhase Phase { get; }

        public GameplayCueEvent(
            ulong eventId,
            double occurredAt,
            CueId cue,
            EntityId targetId,
            WorldPosition position,
            GameplayCuePhase phase = GameplayCuePhase.Applied)
            : base(eventId, occurredAt)
        {
            if (!cue.IsValid) throw new ArgumentException("A valid cue id is required.", nameof(cue));
            if (!targetId.IsValid) throw new ArgumentException("A valid target id is required.", nameof(targetId));
            Cue = cue;
            TargetId = targetId;
            Position = position;
            Phase = phase;
        }

        public GameplayCueEvent(CueId cue, EntityId targetId, WorldPosition position)
            : this(0UL, 0d, cue, targetId, position) { }
    }

    public readonly struct GameplayEffectExecutionResult
    {
        public int DamageApplications { get; }
        public int HealingApplications { get; }
        public int ModifierApplications { get; }

        public GameplayEffectExecutionResult(int damageApplications, int healingApplications, int modifierApplications)
        {
            DamageApplications = damageApplications;
            HealingApplications = healingApplications;
            ModifierApplications = modifierApplications;
        }
    }
}
