#if UNITY_EDITOR
using Blocks.Gameplay.Shooter;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using VampireHunt.Enemies;
using VampireHunt.Infrastructure.Integration;
using VampireHunt.Infrastructure.Netcode;
using VampireHunt.Infrastructure.Unity;
using VampireHunt.Presentation.Enemies;
using VampireHunt.Spawning;

namespace VampireHunt.EditorTools
{
    public static class EnemyContentBuilder
    {
        private const string MeleeArchetypePath = "Assets/VampireHunt/Data/Enemies/VH_MeleeEnemy.asset";
        private const string RangedArchetypePath = "Assets/VampireHunt/Data/Enemies/VH_RangedEnemy.asset";
        private const string SpawnCatalogPath = "Assets/VampireHunt/Data/Enemies/VH_EnemySpawnCatalog.asset";
        private const string MeleePrefabPath = "Assets/VampireHunt/Prefabs/Enemies/VH_Melee.prefab";
        private const string RangedPrefabPath = "Assets/VampireHunt/Prefabs/Enemies/VH_Ranged.prefab";
        private const string ProjectilePrefabPath = "Assets/VampireHunt/Prefabs/Projectiles/VH_EnemyBolt.prefab";
        private const string MeleeLootPath = "Assets/VampireHunt/Data/Loot/LootTables/VH_MeleeEnemyLoot.asset";
        private const string RangedLootPath = "Assets/VampireHunt/Data/Loot/LootTables/VH_RangedEnemyLoot.asset";
        private const string ProjectileMaterialPath = "Assets/VampireHunt/Art/Materials/VH_EnemyBolt.mat";
        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string NetworkPrefabListPath = "Assets/DefaultNetworkPrefabs.asset";

        [MenuItem("Tools/Vampire Hunt/Build Regular Enemy Content")]
        public static void Build()
        {
            try
            {
                EnsureFolder("Assets/VampireHunt/Art/Materials");
                LootTableAsset rangedLoot = CreateOrUpdateRangedLoot();
                GameObject projectilePrefab = CreateOrUpdateProjectilePrefab();
                EnemyArchetypeAsset meleeArchetype =
                    AssetDatabase.LoadAssetAtPath<EnemyArchetypeAsset>(MeleeArchetypePath);
                if (meleeArchetype == null)
                    throw new System.InvalidOperationException($"Missing melee archetype: {MeleeArchetypePath}");

                ConfigureMeleePrefab(meleeArchetype);
                EnemyArchetypeAsset rangedArchetype = CreateOrUpdateRangedArchetype(rangedLoot);
                GameObject rangedPrefab = CreateOrUpdateRangedPrefab(rangedArchetype, projectilePrefab);
                SetObjectReference(rangedArchetype, "networkPrefab", rangedPrefab.GetComponent<NetworkObject>());

                EnemySpawnCatalogAsset spawnCatalog = CreateOrUpdateSpawnCatalog(meleeArchetype, rangedArchetype);
                RegisterNetworkPrefab(rangedPrefab);
                RegisterNetworkPrefab(projectilePrefab);
                ConfigureScene(spawnCatalog, projectilePrefab);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("[EnemyContentBuilder] Regular enemy content built successfully.");
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception);
                throw;
            }
        }

        public static void BuildFromCommandLine()
        {
            Build();
        }

        private static LootTableAsset CreateOrUpdateRangedLoot()
        {
            LootTableAsset loot = AssetDatabase.LoadAssetAtPath<LootTableAsset>(RangedLootPath);
            if (loot == null)
            {
                if (!AssetDatabase.CopyAsset(MeleeLootPath, RangedLootPath))
                    throw new System.InvalidOperationException("Failed to create ranged enemy loot table.");
                loot = AssetDatabase.LoadAssetAtPath<LootTableAsset>(RangedLootPath);
            }
            loot.name = "VH_RangedEnemyLoot";
            SetString(loot, "stableId", "loot.enemy.ranged.basic");
            return loot;
        }

        private static EnemyArchetypeAsset CreateOrUpdateRangedArchetype(LootTableAsset loot)
        {
            EnemyArchetypeAsset asset = LoadOrCreate<EnemyArchetypeAsset>(RangedArchetypePath);
            asset.name = "VH_RangedEnemy";
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("stableId").stringValue = "enemy.ranged.basic";
            serialized.FindProperty("maxHealth").floatValue = 35f;
            serialized.FindProperty("moveSpeed").floatValue = 2.2f;
            serialized.FindProperty("detectionRange").floatValue = 30f;
            serialized.FindProperty("attackRange").floatValue = 11f;
            serialized.FindProperty("attackDamage").floatValue = 8f;
            serialized.FindProperty("attackKnockback").floatValue = 1.5f;
            serialized.FindProperty("spawnDuration").floatValue = 0.35f;
            serialized.FindProperty("telegraphDuration").floatValue = 0.7f;
            serialized.FindProperty("activeDuration").floatValue = 0.1f;
            serialized.FindProperty("recoveryDuration").floatValue = 1.25f;
            serialized.FindProperty("combatStyle").enumValueIndex = (int)EnemyCombatStyle.RangedOrbit;
            serialized.FindProperty("preferredRangeMin").floatValue = 7f;
            serialized.FindProperty("preferredRangeMax").floatValue = 10.5f;
            serialized.FindProperty("retreatRange").floatValue = 5f;
            serialized.FindProperty("attackId").uintValue = 3;
            serialized.FindProperty("scarletReward").floatValue = 24f;
            serialized.FindProperty("spawnCost").intValue = 2;
            serialized.FindProperty("lootTable").objectReferenceValue = loot;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            return asset;
        }

        private static void ConfigureMeleePrefab(EnemyArchetypeAsset archetype)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(MeleePrefabPath);
            try
            {
                EnemyNetworkActor actor = root.GetComponent<EnemyNetworkActor>();
                if (actor == null) throw new System.InvalidOperationException("VH_Melee has no EnemyNetworkActor.");
                EnemyMeleeAttackExecutor executor = GetOrAdd<EnemyMeleeAttackExecutor>(root);
                Transform origin = GetOrCreateAttackOrigin(root, new Vector3(0f, 1f, 0.65f));
                ConfigureActor(actor, archetype, executor, origin);
                PrefabUtility.SaveAsPrefabAsset(root, MeleePrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static GameObject CreateOrUpdateRangedPrefab(
            EnemyArchetypeAsset archetype,
            GameObject projectilePrefab)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(MeleePrefabPath);
            if (source == null) throw new System.InvalidOperationException($"Missing melee prefab: {MeleePrefabPath}");

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                PrefabUtility.UnpackPrefabInstance(
                    instance,
                    PrefabUnpackMode.Completely,
                    InteractionMode.AutomatedAction);
                instance.name = "VH_Ranged";

                EnemyMeleeAttackExecutor melee = instance.GetComponent<EnemyMeleeAttackExecutor>();
                if (melee != null) Object.DestroyImmediate(melee);
                EnemyProjectileAttackExecutor executor = GetOrAdd<EnemyProjectileAttackExecutor>(instance);
                Transform origin = GetOrCreateAttackOrigin(instance, new Vector3(0f, 1.15f, 0.7f));
                var executorSerialized = new SerializedObject(executor);
                executorSerialized.FindProperty("projectilePrefab").objectReferenceValue =
                    projectilePrefab.GetComponent<NetworkObject>();
                executorSerialized.FindProperty("muzzle").objectReferenceValue = origin;
                executorSerialized.FindProperty("projectileSpeed").floatValue = 11f;
                executorSerialized.FindProperty("impactMask").intValue = (1 << 0) | (1 << 3) | (1 << 6);
                executorSerialized.FindProperty("useShooterPool").boolValue = true;
                executorSerialized.ApplyModifiedPropertiesWithoutUndo();

                EnemyNetworkActor actor = instance.GetComponent<EnemyNetworkActor>();
                ConfigureActor(actor, archetype, executor, origin);
                if (instance.TryGetComponent<UnityEngine.AI.NavMeshAgent>(out var agent))
                {
                    agent.speed = 2.2f;
                    agent.stoppingDistance = 9f;
                }
                EnemyPresenter presenter = instance.GetComponentInChildren<EnemyPresenter>(true);
                if (presenter != null)
                {
                    var presenterSerialized = new SerializedObject(presenter);
                    presenterSerialized.FindProperty("normalColor").colorValue = new Color(0.32f, 0.08f, 0.5f, 1f);
                    presenterSerialized.FindProperty("telegraphColor").colorValue = new Color(0.8f, 0.2f, 1f, 1f);
                    presenterSerialized.ApplyModifiedPropertiesWithoutUndo();
                }

                return PrefabUtility.SaveAsPrefabAsset(instance, RangedPrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static GameObject CreateOrUpdateProjectilePrefab()
        {
            GameObject root = new GameObject("VH_EnemyBolt") { layer = 7 };
            try
            {
                root.AddComponent<NetworkObject>();
                Rigidbody rigidbody = root.AddComponent<Rigidbody>();
                rigidbody.useGravity = false;
                rigidbody.isKinematic = true;
                rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
                rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                ModularProjectile projectile = root.AddComponent<ModularProjectile>();
                root.AddComponent<NetworkRigidbody>();
                StraightLineMovement movement = root.AddComponent<StraightLineMovement>();
                ServerCombatProjectileEffect effect = root.AddComponent<ServerCombatProjectileEffect>();

                SphereCollider collider = root.AddComponent<SphereCollider>();
                collider.isTrigger = true;
                collider.radius = 0.25f;

                GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                visual.name = "Visual";
                visual.layer = 7;
                visual.transform.SetParent(root.transform, false);
                visual.transform.localScale = Vector3.one * 0.45f;
                Collider visualCollider = visual.GetComponent<Collider>();
                if (visualCollider != null) Object.DestroyImmediate(visualCollider);
                Renderer renderer = visual.GetComponent<Renderer>();
                renderer.sharedMaterial = CreateOrUpdateProjectileMaterial();
                projectile.visualNode = visual;
                projectile.ignoreStartValues = false;

                var movementSerialized = new SerializedObject(movement);
                movementSerialized.FindProperty("velocityRate").floatValue = 11f;
                movementSerialized.FindProperty("enableBoundary").boolValue = false;
                movementSerialized.FindProperty("useContinuousMovement").boolValue = true;
                movementSerialized.ApplyModifiedPropertiesWithoutUndo();

                var effectSerialized = new SerializedObject(effect);
                effectSerialized.FindProperty("hitRadius").floatValue = 0.25f;
                effectSerialized.FindProperty("lifetime").floatValue = 5f;
                effectSerialized.FindProperty("impactMask").intValue = (1 << 0) | (1 << 3) | (1 << 6);
                effectSerialized.ApplyModifiedPropertiesWithoutUndo();

                return PrefabUtility.SaveAsPrefabAsset(root, ProjectilePrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static Material CreateOrUpdateProjectileMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(ProjectileMaterialPath);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader) { name = "VH_EnemyBolt" };
                AssetDatabase.CreateAsset(material, ProjectileMaterialPath);
            }
            Color color = new Color(0.5f, 0.08f, 0.9f, 1f);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            else if (material.HasProperty("_Color")) material.color = color;
            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 2.5f);
            }
            EditorUtility.SetDirty(material);
            return material;
        }

        private static EnemySpawnCatalogAsset CreateOrUpdateSpawnCatalog(
            EnemyArchetypeAsset melee,
            EnemyArchetypeAsset ranged)
        {
            EnemySpawnCatalogAsset catalog = LoadOrCreate<EnemySpawnCatalogAsset>(SpawnCatalogPath);
            catalog.name = "VH_EnemySpawnCatalog";
            var serialized = new SerializedObject(catalog);
            SerializedProperty entries = serialized.FindProperty("entries");
            entries.arraySize = 2;
            ConfigureSpawnEntry(entries.GetArrayElementAtIndex(0), melee, 1f, 0f, 20);
            ConfigureSpawnEntry(entries.GetArrayElementAtIndex(1), ranged, 0.35f, 60f, 6);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
            return catalog;
        }

        private static void ConfigureSpawnEntry(
            SerializedProperty entry,
            EnemyArchetypeAsset archetype,
            float weight,
            float minimumElapsed,
            int maximumConcurrent)
        {
            entry.FindPropertyRelative("archetype").objectReferenceValue = archetype;
            entry.FindPropertyRelative("weight").floatValue = weight;
            entry.FindPropertyRelative("minimumElapsedSeconds").floatValue = minimumElapsed;
            entry.FindPropertyRelative("maximumConcurrent").intValue = maximumConcurrent;
        }

        private static void ConfigureActor(
            EnemyNetworkActor actor,
            EnemyArchetypeAsset archetype,
            MonoBehaviour executor,
            Transform origin)
        {
            if (actor == null) throw new System.InvalidOperationException("Enemy prefab has no EnemyNetworkActor.");
            var serialized = new SerializedObject(actor);
            serialized.FindProperty("archetype").objectReferenceValue = archetype;
            serialized.FindProperty("attackExecutorBehaviour").objectReferenceValue = executor;
            serialized.FindProperty("attackOrigin").objectReferenceValue = origin;
            serialized.FindProperty("attackBlockingMask").intValue = 1 << 0;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Transform GetOrCreateAttackOrigin(GameObject root, Vector3 localPosition)
        {
            Transform origin = root.transform.Find("AttackOrigin");
            if (origin == null)
            {
                var originObject = new GameObject("AttackOrigin");
                origin = originObject.transform;
                origin.SetParent(root.transform, false);
            }
            origin.localPosition = localPosition;
            origin.localRotation = Quaternion.identity;
            return origin;
        }

        private static void ConfigureScene(EnemySpawnCatalogAsset catalog, GameObject projectilePrefab)
        {
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool openedForBuild = !scene.IsValid() || !scene.isLoaded;
            if (openedForBuild) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                EnemySpawnDirector director = null;
                ObjectPoolSystem pool = null;
                GameObject[] roots = scene.GetRootGameObjects();
                for (int i = 0; i < roots.Length; i++)
                {
                    if (director == null) director = roots[i].GetComponentInChildren<EnemySpawnDirector>(true);
                    if (pool == null) pool = roots[i].GetComponentInChildren<ObjectPoolSystem>(true);
                }
                if (director == null)
                    throw new System.InvalidOperationException($"{ScenePath} has no EnemySpawnDirector.");

                var directorSerialized = new SerializedObject(director);
                if (IsSceneConfigured(directorSerialized, catalog, pool, projectilePrefab))
                    return;

                directorSerialized.FindProperty("spawnCatalog").objectReferenceValue = catalog;
                directorSerialized.FindProperty("softSpawnBudget").intValue = 20;
                directorSerialized.FindProperty("runSeed").intValue = 1337;
                directorSerialized.ApplyModifiedPropertiesWithoutUndo();

                if (pool == null)
                {
                    var poolObject = new GameObject("VH_ProjectilePool");
                    SceneManager.MoveGameObjectToScene(poolObject, scene);
                    pool = poolObject.AddComponent<ObjectPoolSystem>();
                }
                pool.networkPrefabs.Clear();
                pool.networkPrefabs.Add(projectilePrefab);
                pool.objectPoolSize = 32;
                pool.poolInSystemScene = true;
                pool.usePoolForSpawn = true;
                pool.dontDestroyOnSceneUnload = false;
                pool.useUnreliableDeltas = true;
                pool.enableTransformOverrides = true;
                pool.interpolate = true;
                EditorUtility.SetDirty(pool);

                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally
            {
                if (openedForBuild && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static bool IsSceneConfigured(
            SerializedObject director,
            EnemySpawnCatalogAsset catalog,
            ObjectPoolSystem pool,
            GameObject projectilePrefab)
        {
            if (director.FindProperty("spawnCatalog").objectReferenceValue != catalog ||
                director.FindProperty("softSpawnBudget").intValue != 20 ||
                director.FindProperty("runSeed").intValue != 1337 ||
                pool == null)
                return false;

            return pool.networkPrefabs.Count == 1 &&
                   pool.networkPrefabs[0] == projectilePrefab &&
                   pool.objectPoolSize == 32 &&
                   pool.poolInSystemScene &&
                   pool.usePoolForSpawn &&
                   !pool.dontDestroyOnSceneUnload &&
                   pool.useUnreliableDeltas &&
                   pool.enableTransformOverrides &&
                   pool.interpolate;
        }

        private static void RegisterNetworkPrefab(GameObject prefab)
        {
            NetworkPrefabsList list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(NetworkPrefabListPath);
            if (list == null || prefab == null)
                throw new System.InvalidOperationException("DefaultNetworkPrefabs or prefab is missing.");
            var serialized = new SerializedObject(list);
            SerializedProperty entries = serialized.FindProperty("List");
            for (int i = 0; i < entries.arraySize; i++)
            {
                if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("Prefab").objectReferenceValue == prefab)
                    return;
            }
            int index = entries.arraySize;
            entries.InsertArrayElementAtIndex(index);
            SerializedProperty entry = entries.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("Override").enumValueIndex = 0;
            entry.FindPropertyRelative("Prefab").objectReferenceValue = prefab;
            entry.FindPropertyRelative("SourcePrefabToOverride").objectReferenceValue = null;
            entry.FindPropertyRelative("SourceHashToOverride").ulongValue = 0;
            entry.FindPropertyRelative("OverridingTargetPrefab").objectReferenceValue = null;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(list);
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static void SetObjectReference(Object target, string propertyName, Object value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void SetString(Object target, string propertyName, string value)
        {
            var serialized = new SerializedObject(target);
            serialized.FindProperty(propertyName).stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static T GetOrAdd<T>(GameObject target) where T : Component =>
            target.TryGetComponent(out T component) ? component : target.AddComponent<T>();

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
#endif
