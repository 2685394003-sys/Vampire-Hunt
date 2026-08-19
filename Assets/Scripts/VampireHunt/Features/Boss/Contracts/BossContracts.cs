using System;
using VampireHunt.Core;

namespace VampireHunt.Boss.Contracts
{
    public enum BossPhase
    {
        Dormant = 0,
        PhaseOne = 1,
        PhaseTwo = 2,
        PhaseThree = 3
    }

    // Explicit values preserve compatibility with the prototype network/animation enum.
    public enum BossAttackId
    {
        None = 0,
        GuardSweep = 1,
        RotatingBarrage = 2,
        CrossSlash = 3,
        ChargedSlash = 4,
        RectangleDash = 6
    }

    public enum EncounterMode
    {
        Inactive = 0,
        Hunt = 1,
        Battle = 2,
        Defeated = 3
    }

    public enum StaggerState
    {
        Guarded = 0,
        Telegraph = 1,
        Vulnerable = 2,
        Executed = 3
    }

    public readonly struct PresentationCueId : IEquatable<PresentationCueId>
    {
        public string Value { get; }
        public bool IsValid => !string.IsNullOrWhiteSpace(Value);

        public PresentationCueId(string value)
        {
            Value = value?.Trim() ?? string.Empty;
        }

        public bool Equals(PresentationCueId other) =>
            string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is PresentationCueId other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(PresentationCueId left, PresentationCueId right) => left.Equals(right);
        public static bool operator !=(PresentationCueId left, PresentationCueId right) => !left.Equals(right);
    }

    public readonly struct BossSnapshot
    {
        public EntityId Id { get; }
        public int Health { get; }
        public int MaxHealth { get; }
        public BossPhase Phase { get; }
        public bool IsInvulnerable { get; }
        public BossAttackId CurrentAttack { get; }
        public EncounterMode Mode { get; }
        public StaggerState Stagger { get; }
        public float ContractSeconds { get; }
        public bool IsAlive => Health > 0 && Mode != EncounterMode.Defeated;
        public float HealthRatio => MaxHealth > 0 ? (float)Health / MaxHealth : 0f;

        public BossSnapshot(
            EntityId id,
            int health,
            int maxHealth,
            BossPhase phase,
            bool isInvulnerable,
            BossAttackId currentAttack,
            EncounterMode mode,
            StaggerState stagger,
            float contractSeconds)
        {
            Id = id;
            MaxHealth = Math.Max(1, maxHealth);
            Health = Math.Max(0, Math.Min(health, MaxHealth));
            Phase = phase;
            IsInvulnerable = isInvulnerable;
            CurrentAttack = currentAttack;
            Mode = mode;
            Stagger = stagger;
            ContractSeconds = Math.Max(0f, contractSeconds);
        }
    }

    public interface IBossReadModel
    {
        int Health { get; }
        int MaxHealth { get; }
        BossPhase Phase { get; }
        bool IsInvulnerable { get; }
        BossAttackId CurrentAttack { get; }
        EncounterMode Mode { get; }
        StaggerState Stagger { get; }
        float ContractSeconds { get; }
    }

    public interface IBossEncounterQuery
    {
        EncounterMode Mode { get; }
        bool IsEncounterActive { get; }
    }
}
