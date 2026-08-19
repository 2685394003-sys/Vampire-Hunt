using System;
using UnityEngine;
using VampireHunt.Enemies.Contracts;

namespace VampireHunt.Enemies.Authoring
{
    /// <summary>
    /// Editable definition for one enemy archetype. Bootstrap reads this asset
    /// once and passes the immutable EnemySpec to runtime code.
    /// </summary>
    [CreateAssetMenu(
        fileName = "EnemyDefinition",
        menuName = "Vampire Hunt/Enemies/Enemy Definition",
        order = 2)]
    public sealed class EnemyDefinition : ScriptableObject
    {
        [Header("Design identity")]
        [SerializeField] private string archetypeId = "enemy_0001";
        [SerializeField] private GameObject prefab;

        [Header("Combat and movement")]
        [SerializeField, Min(1)] private int maxHealth = 30;
        [SerializeField, Min(0f)] private float moveSpeed = 4f;
        [SerializeField, Min(0)] private int attackDamage = 3;
        [SerializeField, Min(0f)] private float attackRange = 1.5f;
        [SerializeField, Min(0f)] private float attackCooldown = 1.2f;
        [SerializeField] private EnemyAttackType attackType = EnemyAttackType.Melee;

        [Header("Rewards")]
        [SerializeField, Min(0)] private int scarletReward = 1;
        [SerializeField, Min(0)] private int coinReward = 5;
        [SerializeField, Min(0)] private int experienceReward;

        public string ArchetypeId => string.IsNullOrWhiteSpace(archetypeId)
            ? name
            : archetypeId.Trim();
        public GameObject Prefab => prefab;
        public int MaxHealth => Mathf.Max(1, maxHealth);
        public float MoveSpeed => Mathf.Max(0f, moveSpeed);
        public int AttackDamage => Mathf.Max(0, attackDamage);
        public float AttackRange => Mathf.Max(0f, attackRange);
        public float AttackCooldown => Mathf.Max(0f, attackCooldown);
        public EnemyAttackType AttackType => attackType;
        public int ScarletReward => Mathf.Max(0, scarletReward);
        public int CoinReward => Mathf.Max(0, coinReward);
        public int ExperienceReward => Mathf.Max(0, experienceReward);

        public EnemySpec CreateSpec() => EnemySpecFactory.Create(this);

        private void OnValidate()
        {
            maxHealth = Mathf.Max(1, maxHealth);
            moveSpeed = Mathf.Max(0f, moveSpeed);
            attackDamage = Mathf.Max(0, attackDamage);
            attackRange = Mathf.Max(0f, attackRange);
            attackCooldown = Mathf.Max(0f, attackCooldown);
            scarletReward = Mathf.Max(0, scarletReward);
            coinReward = Mathf.Max(0, coinReward);
            experienceReward = Mathf.Max(0, experienceReward);
            if (string.IsNullOrWhiteSpace(archetypeId)) archetypeId = name;
        }
    }

    /// <summary>
    /// Authoring conversion boundary. The primitive overload is also used by
    /// the legacy EnemyStatsConfig shell after run-stat modifiers are applied.
    /// </summary>
    public static class EnemySpecFactory
    {
        public static EnemySpec Create(EnemyDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));

            return Create(
                definition.ArchetypeId,
                definition.MaxHealth,
                definition.MoveSpeed,
                definition.AttackDamage,
                definition.AttackRange,
                definition.AttackCooldown,
                definition.AttackType,
                new RewardGrant(
                    definition.ScarletReward,
                    definition.CoinReward,
                    definition.ExperienceReward));
        }

        public static EnemySpec Create(
            string archetypeId,
            int maxHealth,
            float moveSpeed,
            int attackDamage,
            float attackRange,
            float attackCooldown,
            EnemyAttackType attackType,
            RewardGrant reward)
        {
            return new EnemySpec(
                archetypeId,
                maxHealth,
                moveSpeed,
                attackDamage,
                attackRange,
                attackCooldown,
                attackType,
                reward);
        }
    }
}
