using System;

namespace VampireHunt.Enemies.Contracts
{
    /// <summary>
    /// Runtime enemy definition. Authoring/Prefab references are converted to
    /// this value before entering Domain/Application code.
    /// </summary>
    public sealed class EnemySpec
    {
        public EnemySpec(
            int maxHealth,
            float moveSpeed,
            int attackDamage,
            float attackRange,
            float attackCooldown,
            EnemyAttackType attackType = EnemyAttackType.Melee,
            RewardGrant reward = null)
            : this(string.Empty, maxHealth, moveSpeed, attackDamage, attackRange, attackCooldown, attackType, reward)
        {
        }

        public EnemySpec(
            string archetypeId,
            int maxHealth,
            float moveSpeed,
            int attackDamage,
            float attackRange,
            float attackCooldown,
            EnemyAttackType attackType,
            RewardGrant reward)
        {
            if (maxHealth <= 0) throw new ArgumentOutOfRangeException(nameof(maxHealth));
            if (moveSpeed < 0f) throw new ArgumentOutOfRangeException(nameof(moveSpeed));
            if (attackDamage < 0) throw new ArgumentOutOfRangeException(nameof(attackDamage));
            if (attackRange < 0f) throw new ArgumentOutOfRangeException(nameof(attackRange));
            if (attackCooldown < 0f) throw new ArgumentOutOfRangeException(nameof(attackCooldown));
            ArchetypeId = archetypeId ?? string.Empty;
            MaxHealth = maxHealth;
            MoveSpeed = moveSpeed;
            AttackDamage = attackDamage;
            AttackRange = attackRange;
            AttackCooldown = attackCooldown;
            AttackType = attackType;
            Reward = reward ?? new RewardGrant(0, 0);
        }

        public string ArchetypeId { get; }
        public int MaxHealth { get; }
        public float MoveSpeed { get; }
        public int AttackDamage { get; }
        public float AttackRange { get; }
        public float AttackCooldown { get; }
        public EnemyAttackType AttackType { get; }
        public RewardGrant Reward { get; }
    }
}
