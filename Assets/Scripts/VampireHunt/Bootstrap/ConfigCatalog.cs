using System;
using UnityEngine;
using VampireHunt.Boss.Authoring;
using VampireHunt.Boss.Domain;
using VampireHunt.Player.Authoring;
using VampireHunt.Player.Domain;
using VampireHunt.Spawning.Authoring;

namespace VampireHunt.Bootstrap
{
    /// <summary>
    /// Composition-facing configuration catalog. Authoring assets are read once
    /// and immediately converted to immutable runtime Specs.
    /// </summary>
    [CreateAssetMenu(
        fileName = "ConfigCatalog",
        menuName = "Vampire Hunt/Bootstrap/Config Catalog",
        order = 0)]
    public sealed class ConfigCatalog : ScriptableObject
    {
        [SerializeField] private PlayerDefinition playerDefinition;
        [SerializeField] private BossDefinition bossDefinition;
        [SerializeField] private EnemySpawnConfig enemySpawnConfig;

        public PlayerDefinition PlayerDefinition => playerDefinition;
        public BossDefinition BossDefinition => bossDefinition;
        public EnemySpawnConfig EnemySpawnConfig => enemySpawnConfig;

        public void SetAuthoring(
            PlayerDefinition player,
            BossDefinition boss,
            EnemySpawnConfig enemySpawn)
        {
            playerDefinition = player;
            bossDefinition = boss;
            enemySpawnConfig = enemySpawn;
        }

        public GameSpecs BuildSpecs()
        {
            return BuildSpecs(playerDefinition, bossDefinition, enemySpawnConfig);
        }

        /// <summary>Purely delegates authoring conversion for composition tests and tooling.</summary>
        public static GameSpecs BuildSpecs(
            PlayerDefinition player,
            BossDefinition boss,
            EnemySpawnConfig enemySpawn)
        {
            ValidateAuthoring(player, boss, enemySpawn);

            PlayerSpec playerSpec = PlayerSpecFactory.Create(player);
            BossSpec bossSpec = BossSpecFactory.Create(boss);
            VampireHunt.Spawning.Contracts.EnemySpawnSpec enemySpawnSpec = enemySpawn.CreateSpec();
            return new GameSpecs(playerSpec, bossSpec, enemySpawnSpec);
        }

        public void Validate()
        {
            // Run the same complete conversion path as startup so validation
            // also catches malformed Boss phase/attack collections, rather
            // than only checking whether asset references are present.
            _ = BuildSpecs();
        }

        private static void ValidateAuthoring(
            PlayerDefinition player,
            BossDefinition boss,
            EnemySpawnConfig enemySpawn)
        {
            if (player == null) throw new InvalidOperationException("ConfigCatalog requires a PlayerDefinition.");
            if (boss == null) throw new InvalidOperationException("ConfigCatalog requires a BossDefinition.");
            if (enemySpawn == null) throw new InvalidOperationException("ConfigCatalog requires an EnemySpawnConfig.");

            // BossSpecFactory validates phases, attacks and all boss rule
            // ranges. The catalog owns the call so no runtime service retains
            // the authoring asset.
            enemySpawn.Validate();
        }
    }
}
