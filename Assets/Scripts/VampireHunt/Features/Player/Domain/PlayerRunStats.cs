using System;
using System.Collections.Generic;
using VampireHunt.Abilities.Contracts;
using VampireHunt.Core;
using VampireHunt.Stats;

namespace VampireHunt.Player.Domain
{
    public enum PlayerStat
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
        MaxScarlet = 12,
        DashSpeedMultiplier = 13,
        DashDuration = 14
    }

    public enum PlayerStatModifierOperation
    {
        AddFlat = 0,
        AddPercent = 1,
        Multiply = 2,
        Override = 3
    }

    public static class PlayerStatKeys
    {
        public static StatKey For(PlayerStat stat) =>
            new StatKey((ushort)((int)stat + 1));

        public static readonly StatKey MaxHealth = For(PlayerStat.MaxHealth);
        public static readonly StatKey MaxStamina = For(PlayerStat.MaxStamina);
        public static readonly StatKey DashStaminaCost = For(PlayerStat.DashStaminaCost);
        public static readonly StatKey StaminaRecovery = For(PlayerStat.StaminaRecovery);
        public static readonly StatKey BaseAttack = For(PlayerStat.BaseAttack);
        public static readonly StatKey AttackRange = For(PlayerStat.AttackRange);
        public static readonly StatKey AttackInterval = For(PlayerStat.AttackInterval);
        public static readonly StatKey MoveSpeed = For(PlayerStat.MoveSpeed);
        public static readonly StatKey CritRate = For(PlayerStat.CritRate);
        public static readonly StatKey CritDamage = For(PlayerStat.CritDamage);
        public static readonly StatKey InvincibleTime = For(PlayerStat.InvincibleTime);
        public static readonly StatKey MaxScarlet = For(PlayerStat.MaxScarlet);
        public static readonly StatKey DashSpeedMultiplier = For(PlayerStat.DashSpeedMultiplier);
        public static readonly StatKey DashDuration = For(PlayerStat.DashDuration);
    }

    public readonly struct PlayerStatModifier
    {
        public string ModifierId { get; }
        public string SourceId { get; }
        public PlayerStat Stat { get; }
        public PlayerStatModifierOperation Operation { get; }
        public float Magnitude { get; }
        public int Stacks { get; }
        public int MaxStacks { get; }

        public PlayerStatModifier(
            string modifierId,
            string sourceId,
            PlayerStat stat,
            PlayerStatModifierOperation operation,
            float magnitude,
            int stacks = 1,
            int maxStacks = 1)
        {
            ModifierId = modifierId ?? string.Empty;
            SourceId = sourceId ?? string.Empty;
            Stat = stat;
            Operation = operation;
            Magnitude = magnitude;
            Stacks = Math.Max(1, stacks);
            MaxStacks = Math.Max(1, maxStacks);
        }
    }

    /// <summary>Base values plus deterministic in-run modifiers; no asset state is mutated.</summary>
    public sealed class PlayerRunStats : IStatSnapshot, IPreciseAttributeModifierTarget
    {
        private readonly Dictionary<PlayerStat, float> baseValues;
        private readonly Dictionary<string, ActiveModifier> modifiers =
            new(StringComparer.Ordinal);
        private readonly Dictionary<StatModifierHandle, string> modifierIdsByHandle =
            new();
        private ulong nextModifierHandle = 1UL;

        private sealed class ActiveModifier
        {
            public PlayerStatModifier Definition;
            public int Stacks;
        }

        public PlayerRunStats()
            : this(null)
        {
        }

        public PlayerRunStats(IReadOnlyDictionary<PlayerStat, float> baseValues)
        {
            this.baseValues = new Dictionary<PlayerStat, float>();
            if (baseValues == null) return;
            foreach (KeyValuePair<PlayerStat, float> value in baseValues)
            {
                if (IsFinite(value.Value)) this.baseValues[value.Key] = value.Value;
            }
        }

        public float GetBaseValue(PlayerStat stat) =>
            baseValues.TryGetValue(stat, out float value) ? value : 0f;

        public float GetValue(PlayerStat stat)
        {
            float flat = 0f;
            float percent = 0f;
            float multiplier = 1f;
            float? overrideValue = null;
            foreach (KeyValuePair<string, ActiveModifier> pair in modifiers)
            {
                ActiveModifier active = pair.Value;
                if (active.Definition.Stat != stat) continue;
                float magnitude = active.Definition.Magnitude * active.Stacks;
                switch (active.Definition.Operation)
                {
                    case PlayerStatModifierOperation.AddFlat:
                        flat += magnitude;
                        break;
                    case PlayerStatModifierOperation.AddPercent:
                        percent += magnitude;
                        break;
                    case PlayerStatModifierOperation.Multiply:
                        multiplier *= active.Definition.Magnitude <= 0f
                            ? 0f
                            : (float)Math.Pow(active.Definition.Magnitude, active.Stacks);
                        break;
                    case PlayerStatModifierOperation.Override:
                        overrideValue = active.Definition.Magnitude;
                        break;
                }
            }

            float result = (GetBaseValue(stat) + flat) * (1f + percent) * multiplier;
            return overrideValue ?? result;
        }

        public float GetValue(StatKey key)
        {
            if (!key.IsValid) return 0f;
            ushort raw = key.Value;
            if (raw == 0 || raw > (ushort)PlayerStat.DashDuration + 1) return 0f;
            return GetValue((PlayerStat)(raw - 1));
        }

        public void AddModifier(StatModifier modifier)
        {
            PlayerStat stat = (PlayerStat)Math.Max(0, modifier.Stat.Value - 1);
            PlayerStatModifierOperation operation = (PlayerStatModifierOperation)modifier.Operation;
            AddModifier(new PlayerStatModifier(
                $"{modifier.SourceId.Value}:{modifier.Stat.Value}:{modifier.Operation}",
                modifier.SourceId.ToString(),
                stat,
                operation,
                modifier.Magnitude));
        }

        public int RemoveModifiers(EntityId sourceId) =>
            RemoveBySource(sourceId.ToString());

        /// <summary>
        /// Registers a modifier under a unique handle. Unlike the legacy
        /// source-based entry point, this never merges with a sibling effect
        /// that happens to use the same source/stat/operation.
        /// </summary>
        public StatModifierHandle AddModifierWithHandle(StatModifier modifier)
        {
            StatModifierHandle handle = AllocateModifierHandle();
            string modifierId = BuildHandleModifierId(handle);
            PlayerStat stat = ToPlayerStat(modifier.Stat);
            PlayerStatModifierOperation operation = (PlayerStatModifierOperation)modifier.Operation;
            PlayerStatModifier definition = new PlayerStatModifier(
                modifierId,
                modifier.SourceId.ToString(),
                stat,
                operation,
                modifier.Magnitude);

            if (!AddModifier(definition)) return StatModifierHandle.Invalid;
            modifierIdsByHandle.Add(handle, modifierId);
            return handle;
        }

        public bool RemoveModifier(StatModifierHandle handle)
        {
            if (!handle.IsValid || !modifierIdsByHandle.TryGetValue(handle, out string modifierId))
                return false;

            modifierIdsByHandle.Remove(handle);
            return modifiers.Remove(modifierId);
        }

        public bool AddModifier(PlayerStatModifier modifier)
        {
            if (string.IsNullOrWhiteSpace(modifier.ModifierId) ||
                !IsFinite(modifier.Magnitude)) return false;

            if (!modifiers.TryGetValue(modifier.ModifierId, out ActiveModifier active))
            {
                modifiers.Add(modifier.ModifierId, new ActiveModifier
                {
                    Definition = modifier,
                    Stacks = Math.Min(modifier.Stacks, modifier.MaxStacks)
                });
                return true;
            }

            if (active.Definition.Stat != modifier.Stat ||
                active.Definition.Operation != modifier.Operation ||
                !string.Equals(active.Definition.SourceId, modifier.SourceId, StringComparison.Ordinal))
                return false;

            active.Stacks = Math.Min(active.Definition.MaxStacks, active.Stacks + modifier.Stacks);
            return true;
        }

        public bool RemoveModifier(string modifierId) =>
            !string.IsNullOrWhiteSpace(modifierId) && RemoveModifierById(modifierId);

        public int RemoveBySource(string sourceId)
        {
            if (string.IsNullOrWhiteSpace(sourceId)) return 0;
            List<string> removals = new();
            foreach (KeyValuePair<string, ActiveModifier> pair in modifiers)
                if (string.Equals(pair.Value.Definition.SourceId, sourceId, StringComparison.Ordinal))
                    removals.Add(pair.Key);
            foreach (string id in removals) RemoveModifierById(id);
            return removals.Count;
        }

        public IReadOnlyDictionary<PlayerStat, float> Snapshot()
        {
            Dictionary<PlayerStat, float> snapshot = new();
            foreach (PlayerStat stat in Enum.GetValues(typeof(PlayerStat)))
                snapshot[stat] = GetValue(stat);
            return snapshot;
        }

        public void ResetModifiers()
        {
            modifiers.Clear();
            modifierIdsByHandle.Clear();
        }

        private static PlayerStat ToPlayerStat(StatKey stat) =>
            (PlayerStat)Math.Max(0, stat.Value - 1);

        private static string BuildHandleModifierId(StatModifierHandle handle) =>
            $"__gameplay-effect:{handle.Value}";

        private bool RemoveModifierById(string modifierId)
        {
            if (string.IsNullOrWhiteSpace(modifierId)) return false;
            bool removed = modifiers.Remove(modifierId);
            if (!removed) return false;

            List<StatModifierHandle> handles = new();
            foreach (KeyValuePair<StatModifierHandle, string> pair in modifierIdsByHandle)
                if (string.Equals(pair.Value, modifierId, StringComparison.Ordinal)) handles.Add(pair.Key);
            foreach (StatModifierHandle handle in handles) modifierIdsByHandle.Remove(handle);
            return true;
        }

        private StatModifierHandle AllocateModifierHandle()
        {
            if (nextModifierHandle == 0UL)
                throw new InvalidOperationException("Modifier handle allocation exhausted the ulong range.");

            StatModifierHandle handle = new(nextModifierHandle);
            nextModifierHandle = nextModifierHandle == ulong.MaxValue ? 0UL : nextModifierHandle + 1UL;
            return handle;
        }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
