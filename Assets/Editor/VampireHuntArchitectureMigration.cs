using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using VampireHunt.Boss.Authoring;
using VampireHunt.Boss.Application;
using VampireHunt.Boss.Contracts;
using VampireHunt.Bootstrap;
using VampireHunt.Enemies.Authoring;
using VampireHunt.Enemies.Contracts;
using VampireHunt.Infrastructure.Netcode;
using VampireHunt.Navigation.Contracts;
using VampireHunt.Player.Authoring;
using VampireHunt.Player.Contracts;
using VampireHunt.Spawning.Contracts;
using VampireHunt.Spawning.Authoring;
using VampireHunt.UI.Contracts;
using PlayerBloodPactDefinition = VampireHunt.Player.Authoring.BloodPactDefinition;

[InitializeOnLoad]
public static class VampireHuntArchitectureMigration
{
    public const string ConfigFolder = "Assets/Config/VampireHunt";
    public const string CatalogPath = ConfigFolder + "/RuntimeConfigCatalog.asset";
    private const string EnemyPrefabPath = "Assets/Prefabs/Network/Enemy.prefab";
    private const string LegacyBloodPactPath = "Assets/Resources/GameBalance/BloodPacts.asset";
    private const string SessionKey = "VampireHunt.ArchitectureMigration.v7";

    static VampireHuntArchitectureMigration()
    {
        EditorApplication.delayCall += RunOnceAfterCompilation;
    }

    [MenuItem("Tools/Vampire Hunt/Architecture/Create Or Repair Runtime Config")]
    public static void CreateOrRepairRuntimeConfigMenu()
    {
        ConfigCatalog catalog = EnsureRuntimeConfig();
        Selection.activeObject = catalog;
        EditorGUIUtility.PingObject(catalog);
        Debug.Log($"[VampireHunt] Runtime configuration is valid: {CatalogPath}", catalog);
    }

    [MenuItem("Tools/Vampire Hunt/Architecture/Migrate Runtime Scenes")]
    public static void MigrateRuntimeScenesMenu()
    {
        ConfigCatalog catalog = EnsureRuntimeConfig();
        MigrateEnabledBuildScenes(catalog);
        Debug.Log("[VampireHunt] Runtime scenes were migrated through Unity serialization.");
    }

    public static ConfigCatalog EnsureRuntimeConfig()
    {
        EnsureFolder(ConfigFolder);

        PlayerDefinition player = LoadOrCreate<PlayerDefinition>(
            ConfigFolder + "/PlayerDefinition.asset",
            out bool playerCreated);
        ConfigurePlayer(player, playerCreated);
        EnemyDefinition enemy = LoadOrCreate<EnemyDefinition>(
            ConfigFolder + "/EnemyDefinition.asset",
            out bool enemyCreated);
        ConfigureEnemy(enemy, enemyCreated);
        EnemySpawnConfig spawning = LoadOrCreate<EnemySpawnConfig>(
            ConfigFolder + "/EnemySpawnConfig.asset",
            out bool spawningCreated);
        ConfigureSpawning(spawning, enemy.ArchetypeId, spawningCreated);

        PlayerBloodPactDefinition pact1 = EnsureBloodPact("update_001", 100);
        PlayerBloodPactDefinition pact2 = EnsureBloodPact("update_002", 100);
        PlayerBloodPactDefinition pact3 = EnsureBloodPact("update_003", 100);
        BossAttackDefinition guardSweep = LoadOrCreate<BossAttackDefinition>(
            ConfigFolder + "/BossAttack_GuardSweep.asset",
            out bool guardCreated);
        BossAttackDefinition barrage = LoadOrCreate<BossAttackDefinition>(
            ConfigFolder + "/BossAttack_RotatingBarrage.asset",
            out bool barrageCreated);
        BossAttackDefinition crossSlash = LoadOrCreate<BossAttackDefinition>(
            ConfigFolder + "/BossAttack_CrossSlash.asset",
            out bool crossCreated);

        if (guardCreated) ConfigureAttack(guardSweep, BossAttackId.GuardSweep, 2f, 1f, 10, BossPhase.PhaseOne);
        if (barrageCreated) ConfigureAttack(barrage, BossAttackId.RotatingBarrage, 4f, 0.8f, 7, BossPhase.PhaseTwo);
        if (crossCreated) ConfigureAttack(crossSlash, BossAttackId.CrossSlash, 5f, 0.6f, 15, BossPhase.PhaseThree);

        BossDefinition boss = LoadOrCreate<BossDefinition>(
            ConfigFolder + "/BossDefinition.asset",
            out bool bossCreated);
        if (bossCreated)
            ConfigureBoss(boss, guardSweep, barrage, crossSlash);

        ConfigCatalog catalog = LoadOrCreate<ConfigCatalog>(CatalogPath, out _);
        catalog.SetAuthoring(player, enemy, boss, spawning, pact1, pact2, pact3);
        EditorUtility.SetDirty(catalog);
        catalog.Validate();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return catalog;
    }

    private static void RunOnceAfterCompilation()
    {
        if (SessionState.GetBool(SessionKey, false) ||
            EditorApplication.isPlayingOrWillChangePlaymode ||
            EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                EditorApplication.delayCall += RunOnceAfterCompilation;
            return;
        }

        SessionState.SetBool(SessionKey, true);
        try
        {
            ConfigCatalog catalog = EnsureRuntimeConfig();
            MigrateEnabledBuildScenes(catalog);
            Debug.Log("[VampireHunt] Architecture assets and build scenes migrated successfully.");
        }
        catch (Exception exception)
        {
            SessionState.SetBool(SessionKey, false);
            Debug.LogException(exception);
        }
    }

    private static void MigrateEnabledBuildScenes(ConfigCatalog catalog)
    {
        foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes)
        {
            if (!buildScene.enabled || string.IsNullOrWhiteSpace(buildScene.path) ||
                !buildScene.path.StartsWith("Assets/Scenes/", StringComparison.Ordinal))
                continue;

            Scene scene = SceneManager.GetSceneByPath(buildScene.path);
            bool openedByTool = !scene.IsValid() || !scene.isLoaded;
            if (openedByTool)
                scene = EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Additive);

            try
            {
                RemoveMissingScripts(scene);
                if (buildScene.path.EndsWith("SampleScene.unity", StringComparison.OrdinalIgnoreCase))
                    MigratePrimaryRuntimeScene(scene, catalog);
                else
                    MigrateAuxiliaryScene(scene);
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene))
                    throw new InvalidOperationException($"Could not save migrated scene '{buildScene.path}'.");
            }
            finally
            {
                if (openedByTool && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }
    }

    private static void RemoveMissingScripts(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
        {
            GameObject gameObject = transform.gameObject;
            if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject) == 0)
                continue;

            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(gameObject);
            EditorUtility.SetDirty(gameObject);
        }
    }

    private static void MigratePrimaryRuntimeScene(Scene scene, ConfigCatalog catalog)
    {
        List<MonoBehaviour> behaviours = CollectBehaviours(scene);
        BossConfig legacyBoss = FirstInScene<BossConfig>(behaviours);
        if (legacyBoss != null)
        {
            ConfigureBossFromLegacy(catalog.BossDefinition, legacyBoss);
            SerializedObject serializedBoss = new(legacyBoss);
            serializedBoss.FindProperty("bossDefinition").objectReferenceValue = catalog.BossDefinition;
            serializedBoss.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(legacyBoss);
            catalog.Validate();
            AssetDatabase.SaveAssets();
        }
        NetworkRuntimeLauncher launcher = FirstInScene<NetworkRuntimeLauncher>(behaviours);
        if (launcher == null)
        {
            GameObject host = new("[RuntimeComposition]");
            SceneManager.MoveGameObjectToScene(host, scene);
            launcher = host.AddComponent<NetworkRuntimeLauncher>();
            behaviours = CollectBehaviours(scene);
        }

        GameObject compositionHost = launcher.gameObject;
        SceneBindings bindings = GetOrAdd<SceneBindings>(compositionHost);
        DefaultCompositionFactoryProvider provider =
            GetOrAdd<DefaultCompositionFactoryProvider>(compositionHost);
        Camera camera = FirstInScene<Camera>(scene);

        Transform adaptersRoot = compositionHost.transform.Find("[RuntimeNetcodeAdapters]");
        if (adaptersRoot == null)
        {
            GameObject adapterObject = new("[RuntimeNetcodeAdapters]");
            adapterObject.transform.SetParent(compositionHost.transform, false);
            adaptersRoot = adapterObject.transform;
        }
        GetOrAdd<NetworkObject>(adaptersRoot.gameObject);
        GetOrAdd<NetworkCommandRpcAdapter>(adaptersRoot.gameObject);
        GetOrAdd<NetworkStateRpcAdapter>(adaptersRoot.gameObject);
        GetOrAdd<GameplayEventRpcAdapter>(adaptersRoot.gameObject);

        behaviours = CollectBehaviours(scene);
        ConfigureBloodPactViews(behaviours);
        bindings.Bind(compositionHost.transform, camera);
        bindings.BindAdapters(CollectRuntimeAdapters(behaviours).ToArray());

        SerializedObject serializedLauncher = new(launcher);
        serializedLauncher.FindProperty("sceneBindings").objectReferenceValue = bindings;
        serializedLauncher.FindProperty("configCatalog").objectReferenceValue = catalog;
        serializedLauncher.FindProperty("compositionFactoryProvider").objectReferenceValue = provider;
        serializedLauncher.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(launcher);
        EditorUtility.SetDirty(bindings);
        EditorUtility.SetDirty(provider);
    }

    private static void MigrateAuxiliaryScene(Scene scene)
    {
        List<MonoBehaviour> behaviours = CollectBehaviours(scene);
        List<MonoBehaviour> adapters = CollectRuntimeAdapters(behaviours);
        if (adapters.Count == 0) return;

        SceneBindings bindings = FirstInScene<SceneBindings>(behaviours);
        if (bindings == null)
        {
            GameObject host = new("[SceneBindings]");
            SceneManager.MoveGameObjectToScene(host, scene);
            bindings = host.AddComponent<SceneBindings>();
        }
        ConfigureBloodPactViews(behaviours);
        bindings.Bind(bindings.transform, FirstInScene<Camera>(scene));
        bindings.BindAdapters(adapters.ToArray());
        EditorUtility.SetDirty(bindings);
    }

    private static List<MonoBehaviour> CollectRuntimeAdapters(List<MonoBehaviour> behaviours)
    {
        List<MonoBehaviour> adapters = new();
        HashSet<MonoBehaviour> unique = new();
        for (int i = 0; i < behaviours.Count; i++)
        {
            MonoBehaviour value = behaviours[i];
            if (value == null || !IsRuntimeAdapter(value) || !unique.Add(value)) continue;
            adapters.Add(value);
        }
        return adapters;
    }

    private static bool IsRuntimeAdapter(MonoBehaviour value) =>
        value is IPlayerRuntimeBinding ||
        value is IEnemyRuntimeBinding ||
        value is IBossRuntimeBinding ||
        value is IBossRuntimeDependencyProvider ||
        value is IEnemySpawnDirectorBinding ||
        value is INavigationField ||
        value is IPlayerHudView ||
        value is IBloodPactSelectionView ||
        value is NetworkCommandRpcAdapter ||
        value is NetworkStateRpcAdapter ||
        value is GameplayEventRpcAdapter;

    private static void ConfigureBloodPactViews(List<MonoBehaviour> behaviours)
    {
        BloodPactConfig legacyCatalog = AssetDatabase.LoadAssetAtPath<BloodPactConfig>(LegacyBloodPactPath);
        for (int i = 0; i < behaviours.Count; i++)
            if (behaviours[i] is BloodPactSelectionController view)
                view.SetCatalog(legacyCatalog);
    }

    private static List<MonoBehaviour> CollectBehaviours(Scene scene)
    {
        List<MonoBehaviour> result = new();
        foreach (GameObject root in scene.GetRootGameObjects())
            result.AddRange(root.GetComponentsInChildren<MonoBehaviour>(true));
        return result;
    }

    private static T FirstInScene<T>(List<MonoBehaviour> behaviours) where T : MonoBehaviour
    {
        for (int i = 0; i < behaviours.Count; i++)
            if (behaviours[i] is T value) return value;
        return null;
    }

    private static T FirstInScene<T>(Scene scene) where T : Component
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            T value = root.GetComponentInChildren<T>(true);
            if (value != null) return value;
        }
        return null;
    }

    private static T GetOrAdd<T>(GameObject target) where T : Component =>
        target.TryGetComponent(out T value) ? value : target.AddComponent<T>();

    private static void ConfigurePlayer(PlayerDefinition player, bool created)
    {
        PlayerStatsConfig legacy = PlayerStatsConfig.LoadDefault();
        if (legacy == null) return;

        SerializedObject serialized = new(player);
        if (!created && !LooksLikeGeneratedPlayer(serialized)) return;
        serialized.FindProperty("maxHealth").intValue = legacy.MaxHealth;
        serialized.FindProperty("maxStamina").floatValue = legacy.MaxStamina;
        serialized.FindProperty("dashStaminaCost").floatValue = legacy.DashStaminaCost;
        serialized.FindProperty("staminaRecoveryPerSecond").floatValue = legacy.StaminaRecoverSpeed;
        serialized.FindProperty("dashDuration").floatValue = legacy.DashDuration;
        serialized.FindProperty("dashSpeedMultiplier").floatValue = legacy.DashSpeedMultiplier;
        serialized.FindProperty("attackInterval").floatValue = legacy.AttackInterval;
        serialized.FindProperty("moveSpeed").floatValue = legacy.MoveSpeed;
        serialized.FindProperty("baseAttack").floatValue = legacy.BaseAttack;
        serialized.FindProperty("attackRange").floatValue = legacy.AttackRange;
        serialized.FindProperty("attackConeAngle").floatValue = legacy.AttackConeAngle;
        serialized.FindProperty("critRate").floatValue = legacy.CritRate;
        serialized.FindProperty("critDamage").floatValue = legacy.CritDamage;
        serialized.FindProperty("invincibleTime").floatValue = legacy.InvincibleTime;
        serialized.FindProperty("maxScarlet").floatValue = legacy.MaxScarlet;
        serialized.FindProperty("knockbackForce").floatValue = legacy.KnockbackForce;
        serialized.FindProperty("knockbackDuration").floatValue = legacy.KnockbackDuration;
        serialized.FindProperty("stunDuration").floatValue = legacy.StunDuration;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(player);
    }

    private static bool LooksLikeGeneratedPlayer(SerializedObject serialized) =>
        serialized.FindProperty("maxHealth").intValue == 100 &&
        Mathf.Approximately(serialized.FindProperty("baseAttack").floatValue, 10f) &&
        Mathf.Approximately(serialized.FindProperty("attackRange").floatValue, 2f);

    private static void ConfigureEnemy(EnemyDefinition enemy, bool created)
    {
        SerializedObject serialized = new(enemy);
        if (serialized.FindProperty("prefab").objectReferenceValue == null)
            serialized.FindProperty("prefab").objectReferenceValue =
                AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);

        EnemyStatsConfig legacy = EnemyStatsConfig.LoadDefault();
        bool shouldMigrate = created || LooksLikeGeneratedEnemy(serialized);
        if (legacy != null && shouldMigrate)
        {
            serialized.FindProperty("archetypeId").stringValue = legacy.enemyId;
            serialized.FindProperty("maxHealth").intValue = legacy.maxHealth;
            serialized.FindProperty("moveSpeed").floatValue = legacy.moveSpeed;
            serialized.FindProperty("attackDamage").intValue = legacy.damage;
            serialized.FindProperty("attackRange").floatValue = legacy.weaponRange;
            serialized.FindProperty("attackCooldown").floatValue = legacy.attackCooldown;
            serialized.FindProperty("attackType").intValue = legacy.isRanged
                ? (int)EnemyAttackType.Ranged
                : (int)EnemyAttackType.Melee;
            serialized.FindProperty("scarletReward").intValue = legacy.redResourceDropAmount;
            serialized.FindProperty("coinReward").intValue = legacy.coinDropAmount;
            serialized.FindProperty("experienceReward").intValue = 0;
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(enemy);
    }

    private static bool LooksLikeGeneratedEnemy(SerializedObject serialized) =>
        serialized.FindProperty("maxHealth").intValue == 30 &&
        Mathf.Approximately(serialized.FindProperty("attackCooldown").floatValue, 1.2f) &&
        serialized.FindProperty("scarletReward").intValue <= 1 &&
        serialized.FindProperty("experienceReward").intValue == 0;

    private static void ConfigureSpawning(
        EnemySpawnConfig spawning,
        string enemyTypeId,
        bool created)
    {
        SerializedObject serialized = new(spawning);
        serialized.FindProperty("enemyTypeId").stringValue = enemyTypeId;
        MonsterSpawnConfig legacy = MonsterSpawnConfig.LoadDefault();
        if (legacy != null &&
            (created || LooksLikeGeneratedSpawning(serialized) ||
             LooksLikeGeneratedSpawningExtensions(serialized)))
        {
            serialized.FindProperty("maxAlive").intValue = legacy.MaxAlive;
            serialized.FindProperty("poolPrewarm").intValue = legacy.PoolPrewarmCount;
            serialized.FindProperty("spawnInterval").floatValue = legacy.SpawnInterval;
            serialized.FindProperty("firstSpawnDelay").floatValue = legacy.FirstSpawnDelay;
            serialized.FindProperty("minSpawnRadius").floatValue = legacy.MinSpawnRadius;
            serialized.FindProperty("spawnRadius").floatValue = legacy.MaxSpawnRadius;
            serialized.FindProperty("baseSpawnsPerInterval").intValue = legacy.MonstersPerWave;
            serialized.FindProperty("growthPerMinute").floatValue =
                legacy.MonstersAddedPerGrowth * 60f / Mathf.Max(1f, legacy.WaveGrowthInterval);
            serialized.FindProperty("maxSpawnsPerTick").intValue = legacy.MaxMonstersPerWave;
            serialized.FindProperty("maxSampleAttempts").intValue = legacy.MaxSampleAttemptsPerMonster;
            serialized.FindProperty("spawnHeightOffset").floatValue = legacy.SpawnHeightOffset;
            serialized.FindProperty("minWaveSpawnSeparation").floatValue = legacy.MinWaveSpawnSeparation;
            serialized.FindProperty("bossDirectionProbability").floatValue = legacy.BossDirectionProbability;
            serialized.FindProperty("bossDirectionHalfAngle").floatValue = legacy.BossDirectionHalfAngle;
            serialized.FindProperty("pauseDuringBossTransition").boolValue = legacy.PauseDuringBossTransition;
            serialized.FindProperty("stopWhenBossDies").boolValue = legacy.StopWhenBossDies;
            serialized.FindProperty("resumeDelayAfterPhase").floatValue = legacy.ResumeDelayAfterPhase;
            serialized.FindProperty("requireWalkable").boolValue = legacy.RequireWalkableCell;
            serialized.FindProperty("spawnBlockingLayers").intValue = legacy.SpawnBlockingLayers.value;
            serialized.FindProperty("spawnClearanceRadius").floatValue = legacy.SpawnClearanceRadius;
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(spawning);
    }

    private static bool LooksLikeGeneratedSpawning(SerializedObject serialized) =>
        serialized.FindProperty("maxAlive").intValue == 100 &&
        serialized.FindProperty("poolPrewarm").intValue == 0 &&
        Mathf.Approximately(serialized.FindProperty("spawnInterval").floatValue, 1f) &&
        Mathf.Approximately(serialized.FindProperty("spawnRadius").floatValue, 8f);

    private static bool LooksLikeGeneratedSpawningExtensions(SerializedObject serialized) =>
        Mathf.Approximately(serialized.FindProperty("firstSpawnDelay").floatValue, 1f) &&
        Mathf.Approximately(serialized.FindProperty("minSpawnRadius").floatValue, 4f) &&
        serialized.FindProperty("maxSampleAttempts").intValue == 8 &&
        Mathf.Approximately(serialized.FindProperty("bossDirectionProbability").floatValue, 0f) &&
        Mathf.Approximately(serialized.FindProperty("spawnClearanceRadius").floatValue, 0f);

    private static PlayerBloodPactDefinition EnsureBloodPact(string pactId, int cost)
    {
        PlayerBloodPactDefinition definition = LoadOrCreate<PlayerBloodPactDefinition>(
            ConfigFolder + $"/BloodPact_{pactId}.asset",
            out _);
        SerializedObject serialized = new(definition);
        serialized.FindProperty("pactId").stringValue = pactId;
        serialized.FindProperty("cost").intValue = cost;
        serialized.FindProperty("repeatable").boolValue = true;
        serialized.FindProperty("maximumStacks").intValue = 99;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(definition);
        return definition;
    }

    private static T LoadOrCreate<T>(string assetPath, out bool created) where T : ScriptableObject
    {
        T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
        created = asset == null;
        if (!created) return asset;

        asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, assetPath);
        return asset;
    }

    private static void ConfigureAttack(
        BossAttackDefinition attack,
        BossAttackId id,
        float cooldown,
        float weight,
        int damage,
        BossPhase minimumPhase)
    {
        SerializedObject serialized = new(attack);
        serialized.FindProperty("id").intValue = (int)id;
        serialized.FindProperty("cooldown").floatValue = cooldown;
        serialized.FindProperty("weight").floatValue = weight;
        serialized.FindProperty("damage").intValue = damage;
        serialized.FindProperty("minimumPhase").intValue = (int)minimumPhase;
        serialized.FindProperty("presentationCue").stringValue = id.ToString();
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(attack);
    }

    private static void ConfigureBoss(
        BossDefinition boss,
        params BossAttackDefinition[] attacks)
    {
        SerializedObject serialized = new(boss);
        SerializedProperty phases = serialized.FindProperty("phases");
        phases.arraySize = 3;
        ConfigurePhase(phases.GetArrayElementAtIndex(0), BossPhase.PhaseOne, 1f);
        ConfigurePhase(phases.GetArrayElementAtIndex(1), BossPhase.PhaseTwo, 0.65f);
        ConfigurePhase(phases.GetArrayElementAtIndex(2), BossPhase.PhaseThree, 0.3f);

        SerializedProperty attackArray = serialized.FindProperty("attacks");
        attackArray.arraySize = attacks.Length;
        for (int i = 0; i < attacks.Length; i++)
            attackArray.GetArrayElementAtIndex(i).objectReferenceValue = attacks[i];

        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(boss);
    }

    private static void ConfigureBossFromLegacy(BossDefinition boss, BossConfig legacy)
    {
        if (boss == null || legacy == null || legacy.Definition == boss) return;

        SerializedObject serialized = new(boss);
        serialized.FindProperty("maxHealth").intValue = legacy.maxHealth;
        serialized.FindProperty("guardIntegrity").intValue = legacy.guardMaxHealth * 2;
        serialized.FindProperty("staggerSeconds").floatValue = legacy.staggerWindowDuration;
        serialized.FindProperty("contractTriggerHealthRatio").floatValue = legacy.format5TriggerHealthRate;
        serialized.FindProperty("contractSeconds").floatValue = legacy.format5CountdownSeconds;
        serialized.FindProperty("contractCountdownRate").floatValue = legacy.format5CountdownRate;

        SerializedProperty phases = serialized.FindProperty("phases");
        phases.arraySize = 3;
        ConfigurePhase(phases.GetArrayElementAtIndex(0), BossPhase.PhaseOne, legacy.phase1HealthRate);
        ConfigurePhase(phases.GetArrayElementAtIndex(1), BossPhase.PhaseTwo, legacy.phase2HealthRate);
        ConfigurePhase(phases.GetArrayElementAtIndex(2), BossPhase.PhaseThree, legacy.phase3HealthRate);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(boss);

        ConfigureAttackFromLegacy(
            FindAttack(boss, BossAttackId.GuardSweep),
            legacy.format1Cooldown,
            legacy.format1Weight,
            legacy.format1Damage,
            BossPhase.PhaseOne,
            legacy.format1WarningTime,
            legacy.format1SweepSteps * legacy.format1SweepStepInterval,
            legacy.format1SweepLength,
            legacy.format1SweepWidth,
            legacy.format1Knockback,
            0,
            0f);
        ConfigureAttackFromLegacy(
            FindAttack(boss, BossAttackId.RotatingBarrage),
            legacy.format2Cooldown,
            legacy.format2Weight,
            legacy.format2Damage,
            BossPhase.PhaseOne,
            legacy.format2PreDelay,
            legacy.format2ProjectileCount * legacy.format2ProjectileInterval,
            legacy.format2ProjectileLife,
            legacy.format2ProjectileRadius * 2f,
            legacy.format2Knockback,
            legacy.format2ProjectileCount,
            legacy.format2ProjectileSpeed);
        ConfigureAttackFromLegacy(
            FindAttack(boss, BossAttackId.CrossSlash),
            legacy.format3Cooldown,
            legacy.format3Weight,
            legacy.format3Damage,
            BossPhase.PhaseTwo,
            legacy.format3WarningTime,
            0.2f,
            legacy.format3HalfLength,
            legacy.format3Width,
            legacy.format3Knockback,
            0,
            0f);
    }

    private static BossAttackDefinition FindAttack(BossDefinition boss, BossAttackId id)
    {
        BossAttackDefinition[] attacks = boss.Attacks;
        for (int i = 0; i < attacks.Length; i++)
            if (attacks[i] != null && attacks[i].Id == id) return attacks[i];
        throw new InvalidOperationException($"BossDefinition is missing attack {id}.");
    }

    private static void ConfigureAttackFromLegacy(
        BossAttackDefinition attack,
        float cooldown,
        float weight,
        int damage,
        BossPhase minimumPhase,
        float telegraphSeconds,
        float activeSeconds,
        float range,
        float width,
        float knockback,
        int projectileCount,
        float projectileSpeed)
    {
        SerializedObject serialized = new(attack);
        serialized.FindProperty("cooldown").floatValue = cooldown;
        serialized.FindProperty("weight").floatValue = weight;
        serialized.FindProperty("damage").intValue = damage;
        serialized.FindProperty("minimumPhase").intValue = (int)minimumPhase;
        serialized.FindProperty("telegraphSeconds").floatValue = telegraphSeconds;
        serialized.FindProperty("activeSeconds").floatValue = activeSeconds;
        serialized.FindProperty("range").floatValue = range;
        serialized.FindProperty("width").floatValue = width;
        serialized.FindProperty("knockback").floatValue = knockback;
        serialized.FindProperty("projectileCount").intValue = projectileCount;
        serialized.FindProperty("projectileSpeed").floatValue = projectileSpeed;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(attack);
    }

    private static void ConfigurePhase(
        SerializedProperty phase,
        BossPhase id,
        float threshold)
    {
        phase.FindPropertyRelative("phase").intValue = (int)id;
        phase.FindPropertyRelative("enterAtHealthRatio").floatValue = threshold;
    }

    private static void EnsureFolder(string folder)
    {
        string[] segments = folder.Split('/');
        string current = segments[0];
        for (int i = 1; i < segments.Length; i++)
        {
            string next = current + "/" + segments[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, segments[i]);
            current = next;
        }
    }
}
