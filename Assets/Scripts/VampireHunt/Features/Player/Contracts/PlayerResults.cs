using System;
using System.Collections.Generic;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;

namespace VampireHunt.Player.Contracts
{
    public readonly struct MovementPose
    {
        public WorldPosition Position { get; }
        public MoveVector Facing { get; }
        public double ReportedAt { get; }
        public uint Sequence { get; }
        public bool IsDashing { get; }

        public MovementPose(
            WorldPosition position,
            MoveVector facing,
            double reportedAt = 0d,
            uint sequence = 0,
            bool isDashing = false)
        {
            Position = position;
            Facing = facing;
            ReportedAt = reportedAt;
            Sequence = sequence;
            IsDashing = isDashing;
        }

        public bool IsFinite =>
            IsFinitePosition(Position) && Facing.IsFinite &&
            !double.IsNaN(ReportedAt) && !double.IsInfinity(ReportedAt);

        public static bool IsFinitePosition(WorldPosition position) =>
            !float.IsNaN(position.X) && !float.IsInfinity(position.X) &&
            !float.IsNaN(position.Y) && !float.IsInfinity(position.Y) &&
            !float.IsNaN(position.Z) && !float.IsInfinity(position.Z);
    }

    public readonly struct BloodPactStack : IEquatable<BloodPactStack>
    {
        public BloodPactId Id { get; }
        public int Stacks { get; }

        public BloodPactStack(BloodPactId id, int stacks)
        {
            Id = id;
            Stacks = Math.Max(0, stacks);
        }

        public bool Equals(BloodPactStack other) => Id == other.Id && Stacks == other.Stacks;
        public override bool Equals(object obj) => obj is BloodPactStack other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Id, Stacks);
    }

    /// <summary>Immutable snapshot consumed by state replication and presenters.</summary>
    public readonly struct PlayerSnapshot
    {
        private readonly BloodPactStack[] bloodPacts;

        public EntityId Id { get; }
        public int CurrentHealth { get; }
        public int Health => CurrentHealth;
        public int MaxHealth { get; }
        public bool IsAlive { get; }
        public bool IsInvincibleWindow { get; }
        public float Stamina { get; }
        public float MaxStamina { get; }
        public int Scarlet { get; }
        public int Coins { get; }
        public int Level { get; }
        public int Experience { get; }
        public uint LastCommandSequence { get; }
        public bool IsAttacking { get; }
        public double AttackWindowEndsAt { get; }
        public IReadOnlyList<BloodPactStack> BloodPacts => bloodPacts ?? Array.Empty<BloodPactStack>();

        public PlayerSnapshot(
            EntityId id,
            int currentHealth,
            int maxHealth,
            bool isAlive,
            bool isInvincibleWindow,
            float stamina,
            float maxStamina,
            int scarlet,
            int coins,
            int level,
            int experience,
            uint lastCommandSequence,
            bool isAttacking,
            double attackWindowEndsAt,
            IReadOnlyList<BloodPactStack> bloodPacts = null)
        {
            Id = id;
            CurrentHealth = Math.Max(0, currentHealth);
            MaxHealth = Math.Max(0, maxHealth);
            IsAlive = isAlive && CurrentHealth > 0;
            IsInvincibleWindow = IsAlive && isInvincibleWindow;
            MaxStamina = Math.Max(0f, maxStamina);
            Stamina = Clamp(stamina, 0f, MaxStamina);
            Scarlet = Math.Max(0, scarlet);
            Coins = Math.Max(0, coins);
            Level = Math.Max(1, level);
            Experience = Math.Max(0, experience);
            LastCommandSequence = lastCommandSequence;
            IsAttacking = isAttacking;
            AttackWindowEndsAt = attackWindowEndsAt;
            this.bloodPacts = bloodPacts == null
                ? Array.Empty<BloodPactStack>()
                : CopyPacts(bloodPacts);
        }

        private static BloodPactStack[] CopyPacts(IReadOnlyList<BloodPactStack> source)
        {
            BloodPactStack[] copy = new BloodPactStack[source.Count];
            for (int i = 0; i < source.Count; i++) copy[i] = source[i];
            return copy;
        }

        private static float Clamp(float value, float minimum, float maximum)
        {
            if (float.IsNaN(value) || value < minimum) return minimum;
            if (value > maximum) return maximum;
            return value;
        }
    }

    public readonly struct RewardGrant
    {
        public EntityId RecipientId { get; }
        public int Scarlet { get; }
        public int Coins { get; }
        public int Gold => Coins;
        public int Experience { get; }
        public int SharedScarlet { get; }
        public int SharedAmount => SharedScarlet;

        public bool HasReward => Scarlet != 0 || Coins != 0 || Experience != 0 || SharedScarlet != 0;

        public RewardGrant(
            int scarlet,
            int coins = 0,
            int experience = 0,
            int sharedScarlet = 0)
            : this(default, scarlet, coins, experience, sharedScarlet)
        {
        }

        public RewardGrant(
            EntityId recipientId,
            int scarlet,
            int coins = 0,
            int experience = 0,
            int sharedScarlet = 0)
        {
            RecipientId = recipientId;
            Scarlet = scarlet;
            Coins = coins;
            Experience = experience;
            SharedScarlet = sharedScarlet;
        }
    }

    public readonly struct AttackResult
    {
        private readonly DamageResult[] damageResults;

        public bool Accepted { get; }
        public bool Succeeded => Accepted;
        public bool WasAccepted => Accepted;
        public string Reason { get; }
        public int TargetsConsidered { get; }
        public int TargetsHit { get; }
        public IReadOnlyList<DamageResult> DamageResults =>
            damageResults ?? Array.Empty<DamageResult>();
        public int TotalAppliedDamage { get; }
        public bool AnyKilled { get; }

        public AttackResult(
            bool accepted,
            string reason,
            int targetsConsidered,
            IReadOnlyList<DamageResult> damageResults)
        {
            Accepted = accepted;
            Reason = reason ?? string.Empty;
            TargetsConsidered = Math.Max(0, targetsConsidered);
            this.damageResults = damageResults == null
                ? Array.Empty<DamageResult>()
                : CopyDamageResults(damageResults);

            int applied = 0;
            int hitCount = 0;
            bool killed = false;
            for (int i = 0; i < this.damageResults.Length; i++)
            {
                DamageResult result = this.damageResults[i];
                applied += Math.Max(0, result.AppliedDamage);
                if (result.AppliedDamage > 0) hitCount++;
                killed |= result.WasKilled;
            }

            TargetsHit = hitCount;
            TotalAppliedDamage = applied;
            AnyKilled = killed;
        }

        public AttackResult(
            bool accepted,
            IReadOnlyList<DamageResult> damageResults)
            : this(accepted, string.Empty, damageResults?.Count ?? 0, damageResults)
        {
        }

        private static DamageResult[] CopyDamageResults(IReadOnlyList<DamageResult> source)
        {
            DamageResult[] copy = new DamageResult[source.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = source[i];
            return copy;
        }
    }

    public readonly struct BloodPactOffer
    {
        private readonly BloodPactId[] choices;

        public EntityId PlayerId { get; }
        public uint OfferVersion { get; }
        public int Cost { get; }
        public IReadOnlyList<BloodPactId> Choices => choices ?? Array.Empty<BloodPactId>();
        public bool IsValid => OfferVersion != 0 && choices != null && choices.Length > 0;

        public BloodPactOffer(
            EntityId playerId,
            uint offerVersion,
            int cost,
            IReadOnlyList<BloodPactId> choices)
        {
            PlayerId = playerId;
            OfferVersion = offerVersion;
            Cost = Math.Max(0, cost);
            this.choices = choices == null
                ? Array.Empty<BloodPactId>()
                : CopyChoices(choices);
        }

        private static BloodPactId[] CopyChoices(IReadOnlyList<BloodPactId> source)
        {
            BloodPactId[] copy = new BloodPactId[source.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = source[i];
            return copy;
        }

        public bool Contains(BloodPactId id)
        {
            for (int i = 0; i < Choices.Count; i++)
                if (Choices[i] == id) return true;
            return false;
        }
    }

    public enum BloodPactSelectionCode
    {
        Accepted = 0,
        InvalidPlayer = 1,
        Dead = 2,
        StaleOffer = 3,
        NotOffered = 4,
        InsufficientScarlet = 5,
        AlreadyOwned = 6,
        InvalidSelection = 7
    }

    public readonly struct SelectionResult
    {
        public BloodPactSelectionCode Code { get; }
        public BloodPactSelectionCode Reason => Code;
        public bool Accepted => Code == BloodPactSelectionCode.Accepted;
        public bool Succeeded => Accepted;
        public BloodPactId Selection { get; }
        public int RemainingScarlet { get; }
        public int Scarlet => RemainingScarlet;
        public int Stacks { get; }
        public uint OfferVersion { get; }

        public SelectionResult(
            BloodPactSelectionCode code,
            BloodPactId selection,
            int remainingScarlet,
            int stacks,
            uint offerVersion)
        {
            Code = code;
            Selection = selection;
            RemainingScarlet = Math.Max(0, remainingScarlet);
            Stacks = Math.Max(0, stacks);
            OfferVersion = offerVersion;
        }
    }
}
