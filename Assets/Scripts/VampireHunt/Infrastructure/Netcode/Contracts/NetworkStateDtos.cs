using System;
using System.Collections.Generic;
using VampireHunt.Boss.Contracts;
using VampireHunt.Core;
using VampireHunt.Enemies.Contracts;
using VampireHunt.Player.Contracts;

namespace VampireHunt.Infrastructure.Netcode.Contracts
{
    public enum EnemyReplicationTier
    {
        Near = 0,
        Mid = 1,
        Far = 2,
        Hidden = 3
    }

    /// <summary>Distance/frequency policy is data, not presentation logic.</summary>
    public readonly struct EnemyReplicationPolicy
    {
        public EnemyReplicationPolicy(
            float nearDistance = 20f,
            float midDistance = 50f,
            float hiddenDistance = 100f,
            int midIntervalTicks = 2,
            int farIntervalTicks = 4)
        {
            NearDistance = Math.Max(0f, nearDistance);
            MidDistance = Math.Max(NearDistance, midDistance);
            HiddenDistance = Math.Max(MidDistance, hiddenDistance);
            MidIntervalTicks = Math.Max(1, midIntervalTicks);
            FarIntervalTicks = Math.Max(MidIntervalTicks, farIntervalTicks);
        }

        public float NearDistance { get; }
        public float MidDistance { get; }
        public float HiddenDistance { get; }
        public int MidIntervalTicks { get; }
        public int FarIntervalTicks { get; }

        public EnemyReplicationTier GetTier(float distance)
        {
            if (float.IsNaN(distance) || float.IsInfinity(distance) || distance < 0f)
                return EnemyReplicationTier.Hidden;
            if (distance <= NearDistance) return EnemyReplicationTier.Near;
            if (distance <= MidDistance) return EnemyReplicationTier.Mid;
            if (distance <= HiddenDistance) return EnemyReplicationTier.Far;
            return EnemyReplicationTier.Hidden;
        }

        public bool ShouldSend(EnemyReplicationTier tier, long tick)
        {
            if (tier == EnemyReplicationTier.Hidden) return false;
            if (tick < 0L) tick = 0L;
            int interval = tier == EnemyReplicationTier.Near
                ? 1
                : tier == EnemyReplicationTier.Mid ? MidIntervalTicks : FarIntervalTicks;
            return tick % interval == 0L;
        }
    }

    public readonly struct PlayerStateDto
    {
        public PlayerStateDto(PlayerSnapshot snapshot)
        {
            Id = snapshot.Id;
            Health = snapshot.CurrentHealth;
            MaxHealth = snapshot.MaxHealth;
            IsAlive = snapshot.IsAlive;
            IsInvincibleWindow = snapshot.IsInvincibleWindow;
            Stamina = snapshot.Stamina;
            MaxStamina = snapshot.MaxStamina;
            Scarlet = snapshot.Scarlet;
            Coins = snapshot.Coins;
            Level = snapshot.Level;
            Experience = snapshot.Experience;
            LastCommandSequence = snapshot.LastCommandSequence;
            IsAttacking = snapshot.IsAttacking;
            AttackWindowEndsAt = snapshot.AttackWindowEndsAt;
            BloodPacts = CopyPacts(snapshot.BloodPacts);
        }

        public EntityId Id { get; }
        public int Health { get; }
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
        public IReadOnlyList<BloodPactStack> BloodPacts { get; }

        public PlayerSnapshot ToSnapshot() => new(
            Id, Health, MaxHealth, IsAlive, IsInvincibleWindow, Stamina, MaxStamina,
            Scarlet, Coins, Level, Experience, LastCommandSequence, IsAttacking,
            AttackWindowEndsAt, BloodPacts);

        private static BloodPactStack[] CopyPacts(IReadOnlyList<BloodPactStack> source)
        {
            BloodPactStack[] result = new BloodPactStack[source?.Count ?? 0];
            for (int i = 0; i < result.Length; i++) result[i] = source[i];
            return result;
        }
    }

    public readonly struct EnemyStateDto
    {
        public const int DefaultPositionScale = 100;

        public EnemyStateDto(
            EntityId id,
            int health,
            int maxHealth,
            bool isAlive,
            EnemyState state,
            int positionX,
            int positionY,
            int positionZ,
            EntityId targetId,
            long lastAttackAtMilliseconds,
            uint sequence,
            bool dirty,
            EnemyReplicationTier tier,
            int positionScale = DefaultPositionScale)
        {
            Id = id;
            Health = Math.Max(0, health);
            MaxHealth = Math.Max(0, maxHealth);
            IsAlive = isAlive && Health > 0;
            State = state;
            PositionX = positionX;
            PositionY = positionY;
            PositionZ = positionZ;
            TargetId = targetId;
            LastAttackAtMilliseconds = lastAttackAtMilliseconds;
            Sequence = sequence;
            Dirty = dirty;
            Tier = tier;
            PositionScale = positionScale <= 0 ? DefaultPositionScale : positionScale;
        }

        public EntityId Id { get; }
        public int Health { get; }
        public int MaxHealth { get; }
        public bool IsAlive { get; }
        public EnemyState State { get; }
        public int PositionX { get; }
        public int PositionY { get; }
        public int PositionZ { get; }
        public EntityId TargetId { get; }
        public long LastAttackAtMilliseconds { get; }
        public uint Sequence { get; }
        public bool Dirty { get; }
        public EnemyReplicationTier Tier { get; }
        public int PositionScale { get; }

        public WorldPosition Position => new(
            PositionX / (float)PositionScale,
            PositionY / (float)PositionScale,
            PositionZ / (float)PositionScale);

        public EnemySnapshot ToSnapshot() => new(
            Id, Health, MaxHealth, IsAlive, State, Position, TargetId,
            LastAttackAtMilliseconds / 1000d);

        public static EnemyStateDto From(
            EnemySnapshot snapshot,
            uint sequence,
            bool dirty,
            EnemyReplicationTier tier,
            int positionScale = DefaultPositionScale)
        {
            int safeScale = positionScale <= 0 ? DefaultPositionScale : positionScale;
            return new EnemyStateDto(
                snapshot.Id,
                snapshot.Health,
                snapshot.MaxHealth,
                snapshot.IsAlive,
                snapshot.State,
                Quantize(snapshot.Position.X, safeScale),
                Quantize(snapshot.Position.Y, safeScale),
                Quantize(snapshot.Position.Z, safeScale),
                snapshot.TargetId,
                QuantizeMilliseconds(snapshot.LastAttackAt),
                sequence,
                dirty,
                tier,
                safeScale);
        }

        private static int Quantize(float value, int scale)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0;
            double scaled = value * scale;
            if (scaled >= int.MaxValue) return int.MaxValue;
            if (scaled <= int.MinValue) return int.MinValue;
            return (int)Math.Round(scaled, MidpointRounding.AwayFromZero);
        }

        private static long QuantizeMilliseconds(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0L;
            double scaled = value * 1000d;
            if (scaled >= long.MaxValue) return long.MaxValue;
            if (scaled <= long.MinValue) return long.MinValue;
            return (long)Math.Round(scaled, MidpointRounding.AwayFromZero);
        }
    }

    public readonly struct BossStateDto
    {
        public BossStateDto(BossSnapshot snapshot, uint sequence, bool dirty)
        {
            Id = snapshot.Id;
            Health = snapshot.Health;
            MaxHealth = snapshot.MaxHealth;
            Phase = snapshot.Phase;
            IsInvulnerable = snapshot.IsInvulnerable;
            CurrentAttack = snapshot.CurrentAttack;
            Mode = snapshot.Mode;
            Stagger = snapshot.Stagger;
            ContractSeconds = snapshot.ContractSeconds;
            Sequence = sequence;
            Dirty = dirty;
        }

        public EntityId Id { get; }
        public int Health { get; }
        public int MaxHealth { get; }
        public BossPhase Phase { get; }
        public bool IsInvulnerable { get; }
        public BossAttackId CurrentAttack { get; }
        public EncounterMode Mode { get; }
        public StaggerState Stagger { get; }
        public float ContractSeconds { get; }
        public uint Sequence { get; }
        public bool Dirty { get; }

        public BossSnapshot ToSnapshot() => new(
            Id, Health, MaxHealth, Phase, IsInvulnerable, CurrentAttack, Mode,
            Stagger, ContractSeconds);
    }

    public static class NetworkSequence
    {
        public static bool IsNewer(uint candidate, uint previous)
        {
            uint distance = unchecked(candidate - previous);
            return distance != 0U && distance < 0x80000000U;
        }
    }
}
