using System;
using System.Collections.Generic;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;

namespace VampireHunt.Player.Contracts
{
    /// <summary>
    /// Immutable values needed by a Unity adapter to drive movement and
    /// presentation.  It is a read model; changing one of these values never
    /// changes the authoritative player state.
    /// </summary>
    public readonly struct PlayerRuntimeValues
    {
        public float DashStaminaCost { get; }
        public float StaminaRecoveryPerSecond { get; }
        public float MoveSpeed { get; }
        public float DashSpeedMultiplier { get; }
        public float DashDuration { get; }
        public float AttackRange { get; }
        public float AttackConeAngle { get; }
        public float KnockbackForce { get; }
        public float KnockbackDuration { get; }
        public float StunDuration { get; }

        public PlayerRuntimeValues(
            float dashStaminaCost,
            float staminaRecoveryPerSecond,
            float moveSpeed,
            float dashSpeedMultiplier,
            float dashDuration,
            float attackRange,
            float attackConeAngle,
            float knockbackForce = 0f,
            float knockbackDuration = 0f,
            float stunDuration = 0f)
        {
            DashStaminaCost = NonNegative(dashStaminaCost);
            StaminaRecoveryPerSecond = NonNegative(staminaRecoveryPerSecond);
            MoveSpeed = NonNegative(moveSpeed);
            DashSpeedMultiplier = Math.Max(1f, FiniteOr(dashSpeedMultiplier, 1f));
            DashDuration = NonNegative(dashDuration);
            AttackRange = NonNegative(attackRange);
            AttackConeAngle = Clamp(FiniteOr(attackConeAngle, 0f), 0f, 180f);
            KnockbackForce = NonNegative(knockbackForce);
            KnockbackDuration = NonNegative(knockbackDuration);
            StunDuration = NonNegative(stunDuration);
        }

        private static float NonNegative(float value) => Math.Max(0f, FiniteOr(value, 0f));

        private static float FiniteOr(float value, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : value;

        private static float Clamp(float value, float minimum, float maximum) =>
            value < minimum ? minimum : value > maximum ? maximum : value;
    }

    /// <summary>Intent passed to the authoritative damage application port.</summary>
    public readonly struct PlayerDamageCommand
    {
        public EntityId SourceId { get; }
        public EntityId TargetId { get; }
        public int Amount { get; }
        public WorldPosition HitPosition { get; }
        public DamageFlags Flags { get; }

        public PlayerDamageCommand(
            EntityId sourceId,
            EntityId targetId,
            int amount,
            WorldPosition hitPosition = default,
            DamageFlags flags = DamageFlags.None)
        {
            SourceId = sourceId;
            TargetId = targetId;
            Amount = Math.Max(0, amount);
            HitPosition = hitPosition;
            Flags = flags;
        }
    }

    /// <summary>
    /// Result returned by the authoritative player damage port.  A blocked
    /// invincibility-window hit is explicit and does not become a fake damage
    /// event in a presenter.
    /// </summary>
    public readonly struct PlayerDamageResult
    {
        public DamageResult CombatResult { get; }
        public bool Accepted { get; }
        public bool WasInvincible { get; }
        public bool WasKilled => CombatResult.WasKilled;
        public int AppliedDamage => CombatResult.AppliedDamage;

        public PlayerDamageResult(
            DamageResult combatResult,
            bool accepted = true,
            bool wasInvincible = false)
        {
            CombatResult = combatResult;
            Accepted = accepted;
            WasInvincible = wasInvincible;
        }

        public static PlayerDamageResult Rejected(PlayerDamageCommand command) =>
            new PlayerDamageResult(
                new DamageResult(command.Amount, 0, false, false, command.HitPosition),
                false,
                false);

        public static PlayerDamageResult Invincible(PlayerDamageCommand command) =>
            new PlayerDamageResult(
                new DamageResult(command.Amount, 0, false, false, command.HitPosition),
                true,
                true);
    }

    /// <summary>
    /// Read-only modifier description used by compatibility upgrade entry
    /// points.  The application owns validation, stacking and stat recompute.
    /// </summary>
    public readonly struct PlayerModifierCommand
    {
        public string ModifierId { get; }
        public string SourceId { get; }
        public ushort Stat { get; }
        public byte Operation { get; }
        public float Magnitude { get; }
        public int Stacks { get; }
        public int MaxStacks { get; }
        public float DurationSeconds { get; }

        public PlayerModifierCommand(
            string modifierId,
            string sourceId,
            ushort stat,
            byte operation,
            float magnitude,
            int stacks = 1,
            int maxStacks = 1,
            float durationSeconds = 0f)
        {
            ModifierId = modifierId ?? string.Empty;
            SourceId = sourceId ?? string.Empty;
            Stat = stat;
            Operation = operation;
            Magnitude = magnitude;
            Stacks = Math.Max(1, stacks);
            MaxStacks = Math.Max(1, maxStacks);
            DurationSeconds = Math.Max(0f, durationSeconds);
        }
    }

    /// <summary>
    /// Per-prefab adapter port.  Implementations live in Player Application or
    /// in the composition root; Assembly-CSharp only sees this contract and
    /// never receives a PlayerAggregate.
    /// </summary>
    public interface IPlayerRuntimePort :
        IPlayerCommandGateway,
        IPlayerReadModel,
        IPlayerBloodPactReadModel
    {
        EntityId PlayerId { get; }
        PlayerSnapshot Snapshot { get; }
        PlayerRuntimeValues Values { get; }

        bool TryCreateBloodPactOffer(out BloodPactOffer offer);
        bool TryGetBloodPactStacks(BloodPactId id, out int stacks);

        PlayerDamageResult ApplyDamage(PlayerDamageCommand command);
        int ApplyHealing(int amount);
        bool ForceDeath(EntityId killerId = default);
        bool RestoreToFull();

        bool TryConsumeStamina(float amount);
        bool RestoreStamina(float amount);
        bool AddScarlet(int amount);
        bool TrySpendScarlet(int amount);
        bool AddCoins(int amount);
        bool TrySpendCoins(int amount);
        bool GrantReward(RewardGrant reward);

        bool TryApplyModifier(PlayerModifierCommand modifier);
        bool RemoveModifier(string modifierId);
        int RemoveModifiersFromSource(string sourceId);
        bool ResetForNewRun();
        bool IsInvincibleAt(double now);
        /// <summary>Records the latest owner pose without anti-cheat validation.</summary>
        void SubmitPose(MovementPose pose);
        void Tick(float deltaTime);

        event Action<PlayerSnapshot> SnapshotChanged;
        event Action<IGameplayEvent> GameplayEventProduced;
    }

    /// <summary>Optional composition seam for a prefab/network adapter.</summary>
    public interface IPlayerRuntimeBinding
    {
        bool TryBind(IPlayerRuntimePort runtime);
    }

    /// <summary>
    /// Small immutable snapshot of a stat contribution.  The legacy Unity
    /// component can display it without seeing the internal modifier store.
    /// </summary>
    public readonly struct PlayerStatBreakdownSnapshot
    {
        public ushort Stat { get; }
        public float BaseValue { get; }
        public float FlatBonus { get; }
        public float AdditivePercent { get; }
        public float MultiplicativeFactor { get; }
        public float UnclampedValue { get; }
        public float FinalValue { get; }
        public int ModifierCount { get; }

        public PlayerStatBreakdownSnapshot(
            ushort stat,
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
            ModifierCount = Math.Max(0, modifierCount);
        }
    }

    /// <summary>
    /// Explicit result port for callers that need actual attack outcomes.  The
    /// normal input path remains IPlayerCommandGateway and only submits intent.
    /// </summary>
    public interface IPlayerAttackResultPort
    {
        AttackResult ExecuteAttack(AttackCommand command);
    }

    /// <summary>Shared helper for adapters that need a defensive empty list.</summary>
    internal static class PlayerRuntimeContractHelpers
    {
        public static IReadOnlyList<BloodPactId> EmptyChoices => Array.Empty<BloodPactId>();
    }
}
