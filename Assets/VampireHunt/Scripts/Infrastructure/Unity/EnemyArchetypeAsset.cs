using Unity.Netcode;
using UnityEngine;
using VampireHunt.Enemies;

namespace VampireHunt.Infrastructure.Unity
{
    [CreateAssetMenu(fileName = "EnemyArchetype", menuName = "Vampire Hunt/Enemies/Archetype")]
    public sealed class EnemyArchetypeAsset : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string stableId = "enemy.melee.basic";
        [SerializeField] private NetworkObject networkPrefab;

        [Header("Vitals and Movement")]
        [Min(1f)] [SerializeField] private float maxHealth = 50f;
        [Min(0f)] [SerializeField] private float moveSpeed = 2.5f;
        [Min(0.1f)] [SerializeField] private float detectionRange = 30f;

        [Header("Attack")]
        [Min(0.1f)] [SerializeField] private float attackRange = 1.8f;
        [Min(0f)] [SerializeField] private float attackDamage = 10f;
        [Min(0f)] [SerializeField] private float attackKnockback = 3f;
        [Min(0f)] [SerializeField] private float spawnDuration = 0.35f;
        [Min(0f)] [SerializeField] private float telegraphDuration = 0.45f;
        [Min(0.01f)] [SerializeField] private float activeDuration = 0.1f;
        [Min(0f)] [SerializeField] private float recoveryDuration = 0.8f;

        [Header("Spawn and Reward")]
        [Min(0f)] [SerializeField] private float scarletReward = 5f;
        [Min(1)] [SerializeField] private int spawnCost = 1;

        public string StableId => stableId;
        public NetworkObject NetworkPrefab => networkPrefab;
        public int SpawnCost => Mathf.Max(1, spawnCost);

        public EnemyArchetypeDefinition ToDefinition()
        {
            return new EnemyArchetypeDefinition(
                stableId,
                maxHealth,
                moveSpeed,
                detectionRange,
                attackRange,
                attackDamage,
                attackKnockback,
                spawnDuration,
                telegraphDuration,
                activeDuration,
                recoveryDuration,
                scarletReward,
                spawnCost);
        }

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(stableId)) stableId = name;
            maxHealth = Mathf.Max(1f, maxHealth);
            moveSpeed = Mathf.Max(0f, moveSpeed);
            detectionRange = Mathf.Max(attackRange, detectionRange);
            attackRange = Mathf.Max(0.1f, attackRange);
            attackDamage = Mathf.Max(0f, attackDamage);
            attackKnockback = Mathf.Max(0f, attackKnockback);
            activeDuration = Mathf.Max(0.01f, activeDuration);
            spawnCost = Mathf.Max(1, spawnCost);
        }
    }
}
