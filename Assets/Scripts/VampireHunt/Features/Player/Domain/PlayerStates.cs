using System;
using System.Collections.Generic;
using VampireHunt.Player.Contracts;

namespace VampireHunt.Player.Domain
{
    public sealed class PlayerCombatState
    {
        public double NextAttackAllowedAt { get; private set; }
        public double AttackWindowEndsAt { get; private set; }
        public bool IsAttacking { get; private set; }

        public PlayerCombatState(double attackInterval)
        {
            AttackInterval = Math.Max(0d, attackInterval);
            NextAttackAllowedAt = double.NegativeInfinity;
            AttackWindowEndsAt = double.NegativeInfinity;
        }

        public double AttackInterval { get; private set; }

        public PlayerCombatState()
            : this(1d)
        {
        }

        public void SetAttackInterval(double interval) => AttackInterval = Math.Max(0d, interval);

        public bool CanAttack(double now) =>
            !double.IsNaN(now) && !double.IsInfinity(now) && now >= NextAttackAllowedAt && !IsAttacking;

        public bool BeginAttack(double now, double windowDuration = 0d)
        {
            if (!CanAttack(now)) return false;
            IsAttacking = true;
            AttackWindowEndsAt = now + Math.Max(0d, windowDuration);
            NextAttackAllowedAt = now + AttackInterval;
            return true;
        }

        public bool IsAttackWindowOpen(double now) =>
            IsAttacking && now <= AttackWindowEndsAt;

        public void CompleteAttack()
        {
            IsAttacking = false;
            AttackWindowEndsAt = double.NegativeInfinity;
        }

        public void Reset()
        {
            IsAttacking = false;
            NextAttackAllowedAt = double.NegativeInfinity;
            AttackWindowEndsAt = double.NegativeInfinity;
        }
    }

    public sealed class PlayerMobilityState
    {
        public float Stamina { get; private set; }
        public float MaxStamina { get; private set; }
        public float DashStaminaCost { get; private set; }
        public float StaminaRecoveryPerSecond { get; private set; }
        public double DashCooldown { get; private set; }
        public double DashDuration { get; private set; }
        public double DashSpeedMultiplier { get; private set; }
        public double NextDashAllowedAt { get; private set; }
        public double DashEndsAt { get; private set; }

        public PlayerMobilityState()
            : this(100f, 15f, 15f, 1d, 0.15d, 2d)
        {
        }

        public PlayerMobilityState(
            float maxStamina,
            float dashStaminaCost,
            float staminaRecoveryPerSecond,
            double dashCooldown,
            double dashDuration,
            double dashSpeedMultiplier = 1d)
        {
            MaxStamina = Math.Max(0f, maxStamina);
            Stamina = MaxStamina;
            DashStaminaCost = Math.Max(0f, dashStaminaCost);
            StaminaRecoveryPerSecond = Math.Max(0f, staminaRecoveryPerSecond);
            DashCooldown = Math.Max(0d, dashCooldown);
            DashDuration = Math.Max(0d, dashDuration);
            DashSpeedMultiplier = Math.Max(1d, dashSpeedMultiplier);
            NextDashAllowedAt = double.NegativeInfinity;
            DashEndsAt = double.NegativeInfinity;
        }

        public bool IsDashing(double now) => IsFinite(now) && now < DashEndsAt;

        public bool CanDash(double now) =>
            IsFinite(now) && Stamina + 0.0001f >= DashStaminaCost && now >= NextDashAllowedAt;

        public bool ConsumeDash(double now)
        {
            if (!CanDash(now)) return false;
            Stamina = Math.Max(0f, Stamina - DashStaminaCost);
            NextDashAllowedAt = now + DashCooldown;
            DashEndsAt = now + DashDuration;
            return true;
        }

        /// <summary>
        /// Consumes a raw stamina amount for an adapter compatibility path.
        /// Dash legality remains in ConsumeDash; this method never changes
        /// cooldown or dash duration.
        /// </summary>
        public bool TrySpendStamina(float amount)
        {
            if (amount < 0f || float.IsNaN(amount) || float.IsInfinity(amount) ||
                Stamina + 0.0001f < amount)
                return false;
            Stamina = Math.Max(0f, Stamina - amount);
            return true;
        }

        public void Recover(float deltaTime)
        {
            if (deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
            Stamina = Math.Min(MaxStamina, Stamina + StaminaRecoveryPerSecond * deltaTime);
        }

        public bool RestoreStamina(float amount)
        {
            if (amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return false;
            float previous = Stamina;
            Stamina = Math.Min(MaxStamina, Stamina + amount);
            return Stamina > previous;
        }

        public void SetMaxStamina(float maximum, bool preserveRatio = true)
        {
            maximum = Math.Max(0f, maximum);
            float ratio = MaxStamina > 0f ? Stamina / MaxStamina : 1f;
            MaxStamina = maximum;
            Stamina = preserveRatio ? Math.Clamp(maximum * ratio, 0f, maximum) : maximum;
        }

        public void Reset()
        {
            Stamina = MaxStamina;
            NextDashAllowedAt = double.NegativeInfinity;
            DashEndsAt = double.NegativeInfinity;
        }

        private static bool IsFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public sealed class PlayerProgression
    {
        public int Scarlet { get; private set; }
        public int Coins { get; private set; }
        public int Experience { get; private set; }
        public int Level { get; private set; }

        public PlayerProgression(int scarlet = 0, int coins = 0, int experience = 0, int level = 1)
        {
            Scarlet = Math.Max(0, scarlet);
            Coins = Math.Max(0, coins);
            Experience = Math.Max(0, experience);
            Level = Math.Max(1, level);
        }

        public void AddScarlet(int amount)
        {
            if (amount <= 0) return;
            Scarlet = SaturatingAdd(Scarlet, amount);
        }

        public bool SpendScarlet(int amount)
        {
            if (amount < 0 || Scarlet < amount) return false;
            Scarlet -= amount;
            return true;
        }

        public void AddCoins(int amount)
        {
            if (amount > 0) Coins = SaturatingAdd(Coins, amount);
        }

        public bool SpendCoins(int amount)
        {
            if (amount < 0 || Coins < amount) return false;
            Coins -= amount;
            return true;
        }

        public void AddExperience(int amount)
        {
            if (amount <= 0) return;
            Experience = SaturatingAdd(Experience, amount);
            while (Experience >= ExperienceForNextLevel(Level))
            {
                Experience -= ExperienceForNextLevel(Level);
                Level = SaturatingAdd(Level, 1);
            }
        }

        public void Reset(int scarlet = 0, int coins = 0)
        {
            Scarlet = Math.Max(0, scarlet);
            Coins = Math.Max(0, coins);
            Experience = 0;
            Level = 1;
        }

        public static int ExperienceForNextLevel(int level) =>
            Math.Max(1, Math.Min(int.MaxValue, Math.Max(1, level) * 100));

        private static int SaturatingAdd(int left, int right)
        {
            long result = (long)left + right;
            return result > int.MaxValue ? int.MaxValue : (int)result;
        }
    }

    public sealed class BloodPactLoadout
    {
        private readonly Dictionary<BloodPactId, int> stacks = new();

        public int Count => stacks.Count;

        public int GetStacks(BloodPactId id) =>
            stacks.TryGetValue(id, out int value) ? value : 0;

        public bool AddOrStack(BloodPactId id, bool repeatable = true, int maximumStacks = int.MaxValue)
        {
            if (!id.IsValid || maximumStacks <= 0) return false;
            int current = GetStacks(id);
            if (current > 0 && !repeatable) return false;
            if (current >= maximumStacks) return false;
            stacks[id] = Math.Min(maximumStacks, current + 1);
            return true;
        }

        public bool Remove(BloodPactId id) => stacks.Remove(id);

        public IReadOnlyList<BloodPactStack> Snapshot()
        {
            List<BloodPactStack> result = new(stacks.Count);
            foreach (KeyValuePair<BloodPactId, int> pair in stacks)
                result.Add(new BloodPactStack(pair.Key, pair.Value));
            result.Sort((left, right) => string.CompareOrdinal(left.Id.Value, right.Id.Value));
            return result;
        }

        public void Clear() => stacks.Clear();
    }
}
