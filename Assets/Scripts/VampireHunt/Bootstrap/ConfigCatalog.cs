using System;
using System.Collections.Generic;
using UnityEngine;
using VampireHunt.Boss.Authoring;
using VampireHunt.Boss.Domain;
using VampireHunt.Enemies.Authoring;
using VampireHunt.Enemies.Contracts;
using VampireHunt.Player.Authoring;
using VampireHunt.Player.Contracts;
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
        [SerializeField] private EnemyDefinition enemyDefinition;
        [SerializeField] private BossDefinition bossDefinition;
        [SerializeField] private EnemySpawnConfig enemySpawnConfig;
        [SerializeField] private BloodPactDefinition[] bloodPactDefinitions = Array.Empty<BloodPactDefinition>();

        public PlayerDefinition PlayerDefinition => playerDefinition;
        public EnemyDefinition EnemyDefinition => enemyDefinition;
        public BossDefinition BossDefinition => bossDefinition;
        public EnemySpawnConfig EnemySpawnConfig => enemySpawnConfig;
        public IReadOnlyList<BloodPactDefinition> BloodPactDefinitions => bloodPactDefinitions;

        public void SetAuthoring(
            PlayerDefinition player,
            BossDefinition boss,
            EnemySpawnConfig enemySpawn,
            params BloodPactDefinition[] bloodPacts)
        {
            SetAuthoring(player, enemyDefinition, boss, enemySpawn, bloodPacts);
        }

        public void SetAuthoring(
            PlayerDefinition player,
            EnemyDefinition enemy,
            BossDefinition boss,
            EnemySpawnConfig enemySpawn,
            params BloodPactDefinition[] bloodPacts)
        {
            playerDefinition = player;
            enemyDefinition = enemy;
            bossDefinition = boss;
            enemySpawnConfig = enemySpawn;
            bloodPactDefinitions = bloodPacts == null
                ? Array.Empty<BloodPactDefinition>()
                : (BloodPactDefinition[])bloodPacts.Clone();
        }

        public GameSpecs BuildSpecs()
        {
            return BuildSpecs(
                playerDefinition,
                enemyDefinition,
                bossDefinition,
                enemySpawnConfig,
                bloodPactDefinitions);
        }

        /// <summary>Purely delegates authoring conversion for composition tests and tooling.</summary>
        public static GameSpecs BuildSpecs(
            PlayerDefinition player,
            EnemyDefinition enemy,
            BossDefinition boss,
            EnemySpawnConfig enemySpawn,
            IReadOnlyList<BloodPactDefinition> bloodPacts = null)
        {
            ValidateAuthoring(player, enemy, boss, enemySpawn);

            PlayerSpec playerSpec = PlayerSpecFactory.Create(player);
            EnemySpec enemySpec = EnemySpecFactory.Create(enemy);
            BossSpec bossSpec = BossSpecFactory.Create(boss);
            VampireHunt.Spawning.Contracts.EnemySpawnSpec enemySpawnSpec = enemySpawn.CreateSpec();
            BloodPactOption[] options = BuildBloodPactOptions(bloodPacts);
            return new GameSpecs(playerSpec, bossSpec, enemySpawnSpec, enemySpec, options);
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
            EnemyDefinition enemy,
            BossDefinition boss,
            EnemySpawnConfig enemySpawn)
        {
            if (player == null) throw new InvalidOperationException("ConfigCatalog requires a PlayerDefinition.");
            if (enemy == null) throw new InvalidOperationException("ConfigCatalog requires an EnemyDefinition.");
            if (boss == null) throw new InvalidOperationException("ConfigCatalog requires a BossDefinition.");
            if (enemySpawn == null) throw new InvalidOperationException("ConfigCatalog requires an EnemySpawnConfig.");

            // BossSpecFactory validates phases, attacks and all boss rule
            // ranges. The catalog owns the call so no runtime service retains
            // the authoring asset.
            enemySpawn.Validate();
        }

        private static BloodPactOption[] BuildBloodPactOptions(
            IReadOnlyList<BloodPactDefinition> definitions)
        {
            if (definitions == null || definitions.Count == 0)
                return Array.Empty<BloodPactOption>();

            BloodPactOption[] options = new BloodPactOption[definitions.Count];
            for (int i = 0; i < definitions.Count; i++)
            {
                BloodPactDefinition definition = definitions[i];
                if (definition == null)
                    throw new InvalidOperationException($"Blood Pact definition {i} is missing.");
                options[i] = definition.CreateOption();
            }
            return options;
        }
    }
}
