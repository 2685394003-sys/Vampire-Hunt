using System;

namespace VampireHunt.Enemies
{
    public enum EnemyCombatStyle : byte
    {
        MeleeChase = 0,
        RangedOrbit = 1
    }

    /// <summary>Unity-free immutable gameplay configuration for one enemy type.</summary>
    public sealed class EnemyArchetypeDefinition
    {
        public string StableId { get; }
        public float MaxHealth { get; }
        public float MoveSpeed { get; }
        public float DetectionRange { get; }
        public float AttackRange { get; }
        public float AttackBreakRange { get; }
        public float AttackDamage { get; }
        public float AttackKnockback { get; }
        public double SpawnDuration { get; }
        public double TelegraphDuration { get; }
        public double ActiveDuration { get; }
        public double RecoveryDuration { get; }
        public float ScarletReward { get; }
        public int SpawnCost { get; }
        public EnemyCombatStyle CombatStyle { get; }
        public float PreferredRangeMin { get; }
        public float PreferredRangeMax { get; }
        public float RetreatRange { get; }
        public uint AttackId { get; }

        public EnemyArchetypeDefinition(
            string stableId,
            float maxHealth,
            float moveSpeed,
            float detectionRange,
            float attackRange,
            float attackDamage,
            float attackKnockback,
            double spawnDuration,
            double telegraphDuration,
            double activeDuration,
            double recoveryDuration,
            float scarletReward,
            int spawnCost,
            EnemyCombatStyle combatStyle = EnemyCombatStyle.MeleeChase,
            float preferredRangeMin = 0f,
            float preferredRangeMax = 0f,
            float retreatRange = 0f,
            uint attackId = 2)
        {
            if (string.IsNullOrWhiteSpace(stableId))
                throw new ArgumentException("Enemy stable ID is required.", nameof(stableId));

            StableId = stableId;
            MaxHealth = Math.Max(1f, maxHealth);
            MoveSpeed = Math.Max(0f, moveSpeed);
            DetectionRange = Math.Max(attackRange, detectionRange);
            AttackRange = Math.Max(0.1f, attackRange);
            AttackBreakRange = AttackRange * 1.5f;
            AttackDamage = Math.Max(0f, attackDamage);
            AttackKnockback = Math.Max(0f, attackKnockback);
            SpawnDuration = Math.Max(0d, spawnDuration);
            TelegraphDuration = Math.Max(0d, telegraphDuration);
            ActiveDuration = Math.Max(0.01d, activeDuration);
            RecoveryDuration = Math.Max(0d, recoveryDuration);
            ScarletReward = Math.Max(0f, scarletReward);
            SpawnCost = Math.Max(1, spawnCost);
            CombatStyle = combatStyle;

            if (combatStyle == EnemyCombatStyle.RangedOrbit)
            {
                PreferredRangeMin = Math.Max(0.1f, preferredRangeMin);
                PreferredRangeMax = Math.Max(
                    PreferredRangeMin,
                    Math.Min(AttackRange, preferredRangeMax > 0f ? preferredRangeMax : AttackRange));
                RetreatRange = Math.Max(0f, Math.Min(PreferredRangeMin, retreatRange));
            }
            else
            {
                PreferredRangeMin = 0f;
                PreferredRangeMax = AttackRange;
                RetreatRange = 0f;
            }

            AttackId = Math.Max(1u, attackId);
        }
    }
}
