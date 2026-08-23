using System;

namespace VampireHunt.Enemies
{
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
            int spawnCost)
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
        }
    }
}
