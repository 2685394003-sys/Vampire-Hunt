using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace VampireHunt.Tests.Architecture
{
    /// <summary>
    /// Source-level guards for editor authoring commands. These are deliberately
    /// independent of the current serialized assets: a future builder change
    /// must not silently restore a legacy runtime authority or rewrite the
    /// canonical network actor assets from a scene clone.
    /// </summary>
    public sealed class BuilderAuthoringContractTests
    {
        [Test]
        public void NetworkSetup_DoesNotAssignLegacySpawnAuthorities()
        {
            string source = ReadProjectFile("Assets/Scripts/Editor/VampireHuntNetworkSetup.cs");

            Assert.That(source, Does.Not.Contain("AssignEnemyPrefabToSpawners"));
            Assert.That(source, Does.Not.Contain("MonsterSpawnPoint"));
            Assert.That(source, Does.Not.Contain("PlayerProximityMonsterSpawner"));
            Assert.That(source, Does.Not.Contain("RemoveLegacySpawnComponents"));
        }

        [Test]
        public void NetworkSetup_PreservesExistingCanonicalActorPrefab()
        {
            string source = ReadProjectFile("Assets/Scripts/Editor/VampireHuntNetworkSetup.cs");

            Assert.That(source, Does.Contain("LoadAssetAtPath<GameObject>(path)"));
            Assert.That(source, Does.Contain("refusing to overwrite"));
            Assert.That(source, Does.Contain("existing.GetComponent<NetworkObject>()"));
        }

        [Test]
        public void DefaultEnemyBuilder_IsExplicitAndValidatesNetworkObject()
        {
            string source = ReadProjectFile("Assets/Scripts/Editor/DefaultEnemy3DBuilder.cs");

            Assert.That(source, Does.Not.Contain("InitializeOnLoadMethod"));
            Assert.That(source, Does.Not.Contain("delayCall"));
            Assert.That(source, Does.Contain("prefab.GetComponent<NetworkObject>()"));
            Assert.That(source, Does.Contain("PrefabUtility.LoadPrefabContents"));
        }

        [Test]
        public void BossInstaller_DoesNotAuthorLegacySpawningOrFlowFieldSolvers()
        {
            string source = ReadProjectFile("Assets/Scripts/Boss/Editor/Boss3DSceneInstaller.cs");

            Assert.That(source, Does.Not.Contain("MonsterSpawnPoint"));
            Assert.That(source, Does.Not.Contain("PlayerProximityMonsterSpawner"));
            Assert.That(source, Does.Not.Contain("FlowFieldManager"));
            Assert.That(source, Does.Contain("NetworkObject"));
        }

        private static string ReadProjectFile(string relativePath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string path = Path.Combine(
                projectRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.That(File.Exists(path), Is.True, $"Missing source contract file: {relativePath}");
            return File.ReadAllText(path);
        }
    }
}
