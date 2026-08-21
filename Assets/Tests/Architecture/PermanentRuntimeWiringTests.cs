using System;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VampireHunt.Tests.Architecture
{
    public sealed class PermanentRuntimeWiringTests
    {
        private const string PlayerPrefabPath = "Assets/Prefabs/Network/Player.prefab";
        private const string EnemyPrefabPath = "Assets/Prefabs/Network/Enemy.prefab";
        private const string RuntimeScenePath = "Assets/Scenes/SampleScene.unity";
        private const string UiScenePath = "Assets/Scenes/UI.unity";
        private const string RetiredMigrationToolPath =
            "Assets/Editor/VampireHuntRuntimeAssetMigration.cs";
        private const string AuthoringUtilityPath =
            "Assets/Editor/VampireHuntArchitectureMigration.cs";
        private const string NetworkSetupPath =
            "Assets/Scripts/Editor/VampireHuntNetworkSetup.cs";

        [Test]
        public void RuntimeAssets_DoNotDependOnAutomaticMigrationTools()
        {
            Assert.That(File.Exists(RetiredMigrationToolPath), Is.False,
                "The retired runtime asset migration tool must not return.");

            string authoringSource = File.ReadAllText(AuthoringUtilityPath);
            Assert.That(authoringSource, Does.Not.Contain("[InitializeOnLoad]"));
            Assert.That(authoringSource, Does.Not.Contain("EditorApplication.delayCall"));
            Assert.That(authoringSource, Does.Not.Contain("SessionState"));
            Assert.That(authoringSource, Does.Not.Contain("class VampireHuntArchitectureMigration"));

            string networkSetupSource = File.ReadAllText(NetworkSetupPath);
            Assert.That(networkSetupSource,
                Does.Not.Contain("GetOrAdd<PlayerProximityMonsterSpawner>"),
                "Network authoring must not recreate the retired parallel spawner.");
        }

        [Test]
        public void PlayerPrefab_ExplicitlyWiresRuntimeAdapterDependencies()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                Component controller = RequireSingle(root, "PlayerController");
                AssertReference(controller, "playerState", RequireSingle(root, "PlayerNetworkState"));
                AssertReference(controller, "body", RequireSingle<Rigidbody>(root));
                AssertReferenceType<Animator>(controller, "anim");
                AssertReference(controller, "_attackPresenter", RequireSingle(root, "PlayerAttackPresenter"));
                AssertNoComponent(root, "AttackDamageForwarder");
                AssertNoComponent(root, "PlayerMovement");
                AssertNoComponent(root, "MonsterSpawnPoint");
                AssertNoComponent(root, "PlayerProximityMonsterSpawner");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [Test]
        public void EnemyPrefab_ExplicitlyWiresRuntimeAdapterDependencies()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(EnemyPrefabPath);
            try
            {
                Component motor = RequireSingle(root, "FlowFieldEnemy");
                AssertReference(motor, "body", RequireSingle<Rigidbody>(root));
                AssertReference(motor, "animationController", RequireSingle(root, "EnemyAnimationController"));
                AssertReference(motor, "combat", RequireSingle(root, "EnemyCombat"));
                AssertReference(motor, "enemyHealth", RequireSingle(root, "EnemyHealth"));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [Test]
        public void RuntimeScene_PermanentlyBindsUnityPhysicsNavigationAdapter()
        {
            Scene scene = SceneManager.GetSceneByPath(RuntimeScenePath);
            bool openedByTest = !scene.IsValid() || !scene.isLoaded;
            if (openedByTest)
                scene = EditorSceneManager.OpenScene(RuntimeScenePath, OpenSceneMode.Additive);

            try
            {
                Component manager = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    manager = FindByTypeName(root, "FlowFieldWorldAdapterBehaviour");
                    if (manager != null) break;
                }

                Assert.That(manager, Is.Not.Null,
                    "SampleScene is missing the UnityPhysics flow-field adapter.");

                Component sceneBindings = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    sceneBindings = FindByTypeName(root, "SceneBindings");
                    if (sceneBindings != null) break;
                }

                Assert.That(sceneBindings, Is.Not.Null, "SampleScene is missing SceneBindings.");
                SerializedProperty bindings = new SerializedObject(sceneBindings)
                    .FindProperty("adapterBindings");
                Assert.That(bindings, Is.Not.Null);
                Assert.That(ContainsObjectReference(bindings, manager), Is.True,
                    "FlowFieldWorldAdapterBehaviour must be explicitly wired through SceneBindings.");
            }
            finally
            {
                if (openedByTest && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static bool ContainsObjectReference(SerializedProperty array, UnityEngine.Object expected)
        {
            for (int i = 0; i < array.arraySize; i++)
            {
                if (array.GetArrayElementAtIndex(i).objectReferenceValue == expected) return true;
            }
            return false;
        }

        [Test]
        public void UiScene_PermanentlyAuthorsCombatTextUnderExplicitSceneBindings()
        {
            Scene scene = SceneManager.GetSceneByPath(UiScenePath);
            bool openedByTest = !scene.IsValid() || !scene.isLoaded;
            if (openedByTest)
                scene = EditorSceneManager.OpenScene(UiScenePath, OpenSceneMode.Additive);

            try
            {
                Component bindings = null;
                Component combatText = null;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    bindings ??= FindByTypeName(root, "SceneBindings");
                    combatText ??= FindByTypeName(root, "CombatTextService");
                }

                Assert.That(bindings, Is.Not.Null, "UI scene is missing SceneBindings.");
                Assert.That(combatText, Is.Not.Null, "UI scene is missing authored CombatTextService.");
                SerializedProperty gameplayRoot = new SerializedObject(bindings)
                    .FindProperty("gameplayRoot");
                Assert.That(gameplayRoot, Is.Not.Null);
                Transform rootTransform = gameplayRoot.objectReferenceValue as Transform;
                Assert.That(rootTransform, Is.Not.Null);
                Assert.That(combatText.transform.IsChildOf(rootTransform), Is.True,
                    "CombatTextService must be below the UI SceneBindings gameplay root.");
            }
            finally
            {
                if (openedByTest && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static Component RequireSingle(GameObject root, string typeName)
        {
            Component match = null;
            int count = 0;
            Component[] components = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null || !string.Equals(component.GetType().Name, typeName, StringComparison.Ordinal))
                    continue;
                match = component;
                count++;
            }

            Assert.That(count, Is.EqualTo(1), $"Expected exactly one {typeName} on '{root.name}'.");
            return match;
        }

        private static T RequireSingle<T>(GameObject root) where T : Component
        {
            T[] values = root.GetComponentsInChildren<T>(true);
            Assert.That(values.Length, Is.EqualTo(1),
                $"Expected exactly one {typeof(T).Name} on '{root.name}'.");
            return values[0];
        }

        private static Component FindByTypeName(GameObject root, string typeName)
        {
            Component[] components = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component != null && string.Equals(component.GetType().Name, typeName, StringComparison.Ordinal))
                    return component;
            }
            return null;
        }

        private static void AssertNoComponent(GameObject root, string typeName)
        {
            Assert.That(FindByTypeName(root, typeName), Is.Null,
                $"'{root.name}' must not serialize retired runtime component {typeName}.");
        }

        private static void AssertReference(Component owner, string propertyName, UnityEngine.Object expected)
        {
            SerializedProperty property = new SerializedObject(owner).FindProperty(propertyName);
            Assert.That(property, Is.Not.Null,
                $"{owner.GetType().Name} is missing serialized property '{propertyName}'.");
            Assert.That(property.objectReferenceValue, Is.SameAs(expected),
                $"{owner.GetType().Name}.{propertyName} is not wired to the canonical component.");
        }

        private static void AssertReferenceType<T>(Component owner, string propertyName)
            where T : UnityEngine.Object
        {
            SerializedProperty property = new SerializedObject(owner).FindProperty(propertyName);
            Assert.That(property, Is.Not.Null,
                $"{owner.GetType().Name} is missing serialized property '{propertyName}'.");
            Assert.That(property.objectReferenceValue, Is.TypeOf<T>(),
                $"{owner.GetType().Name}.{propertyName} is not wired to {typeof(T).Name}.");
        }
    }
}
