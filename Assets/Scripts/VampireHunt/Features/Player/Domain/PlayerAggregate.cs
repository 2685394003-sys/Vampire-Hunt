using System;
using VampireHunt.Core;
using VampireHunt.Player.Contracts;

namespace VampireHunt.Player.Domain
{
    /// <summary>
    /// Server-owned player state. Position and facing are deliberately absent;
    /// they belong to the Owner movement replication channel and its validator.
    /// </summary>
    public sealed class PlayerAggregate
    {
        private bool hasAcceptedSequence;
        private uint lastCommandSequence;
        private bool deathHandled;

        public EntityId Id { get; }
        public PlayerVitals Vitals { get; }
        public PlayerRunStats RunStats { get; }
        public PlayerCombatState CombatState { get; }
        public PlayerMobilityState MobilityState { get; }
        public PlayerProgression Progression { get; }
        public BloodPactLoadout BloodPacts { get; }
        public uint LastCommandSequence => lastCommandSequence;
        public bool HasAcceptedCommand => hasAcceptedSequence;
        public bool IsAlive => Vitals.IsAlive;
        public bool DeathHandled => deathHandled;

        public PlayerAggregate(
            EntityId id,
            PlayerVitals vitals,
            PlayerRunStats runStats,
            PlayerCombatState combatState,
            PlayerMobilityState mobilityState,
            PlayerProgression progression,
            BloodPactLoadout bloodPacts)
        {
            Id = id;
            Vitals = vitals ?? throw new ArgumentNullException(nameof(vitals));
            RunStats = runStats ?? throw new ArgumentNullException(nameof(runStats));
            CombatState = combatState ?? throw new ArgumentNullException(nameof(combatState));
            MobilityState = mobilityState ?? throw new ArgumentNullException(nameof(mobilityState));
            Progression = progression ?? throw new ArgumentNullException(nameof(progression));
            BloodPacts = bloodPacts ?? throw new ArgumentNullException(nameof(bloodPacts));
        }

        public bool TryAcceptCommandSequence(uint sequence)
        {
            if (!hasAcceptedSequence)
            {
                hasAcceptedSequence = true;
                lastCommandSequence = sequence;
                return true;
            }

            // Sequence arithmetic remains valid when the uint wraps.
            uint distance = unchecked(sequence - lastCommandSequence);
            if (distance == 0 || distance >= 0x80000000u) return false;
            lastCommandSequence = sequence;
            return true;
        }

        public bool TryBeginAttack(double now, double attackWindowDuration = 0d) =>
            IsAlive && CombatState.BeginAttack(now, attackWindowDuration);

        public bool TryDash(double now) => IsAlive && MobilityState.ConsumeDash(now);

        public PlayerDamageOutcome ApplyDamage(
            int damage,
            double now,
            double invincibleDuration,
            EntityId sourceId = default,
            WorldPosition hitPosition = default,
            bool wasCritical = false) =>
            Vitals.ApplyDamage(
                damage,
                now,
                invincibleDuration,
                sourceId,
                hitPosition,
                wasCritical);

        public int ApplyHealing(int amount) => Vitals.ApplyHealing(amount);

        public bool TryMarkDeathHandled()
        {
            if (Vitals.IsAlive || deathHandled) return false;
            deathHandled = true;
            return true;
        }

        public void ResetForRespawn(int? health = null)
        {
            Vitals.Reset(health);
            CombatState.Reset();
            MobilityState.Reset();
            deathHandled = false;
            hasAcceptedSequence = false;
            lastCommandSequence = 0;
        }

        public PlayerSnapshot CreateSnapshot(double now = 0d)
        {
            Vitals.IsInvincibleAt(now);
            return new PlayerSnapshot(
                Id,
                Vitals.CurrentHealth,
                Vitals.MaxHealth,
                Vitals.IsAlive,
                Vitals.IsInvincibleWindow,
                MobilityState.Stamina,
                MobilityState.MaxStamina,
                Progression.Scarlet,
                Progression.Coins,
                Progression.Level,
                Progression.Experience,
                lastCommandSequence,
                CombatState.IsAttacking,
                CombatState.AttackWindowEndsAt,
                BloodPacts.Snapshot());
        }
    }
}
