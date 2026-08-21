#if UNITY_EDITOR
using System.IO;
using Unity.Netcode;
using Unity.Netcode.Components;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class VampireHuntNetworkSetup
{
    private const string PrefabFolder = "Assets/Prefabs/Network";
    private const string PlayerPrefabPath = PrefabFolder + "/Player.prefab";
    private const string EnemyPrefabPath = PrefabFolder + "/Enemy.prefab";
    private const string PrefabListPath = "Assets/DefaultNetworkPrefabs.asset";

    [MenuItem("Tools/Vampire Hunt/Configure Netcode Scene")]
    public static void ConfigureOpenScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[Network Setup] 请先退出 Play Mode。");
            return;
        }

        Directory.CreateDirectory(PrefabFolder);

        PlayerController scenePlayer = Object.FindFirstObjectByType<PlayerController>(
            FindObjectsInactive.Include);
        if (scenePlayer == null)
        {
            Debug.LogError("[Network Setup] 当前场景没有 PlayerController，无法创建 Player prefab。");
            return;
        }

        PrepareActor(scenePlayer.gameObject, true);
        GameObject playerPrefab = SaveActorPrefab(scenePlayer.gameObject, PlayerPrefabPath, true);

        FlowFieldEnemy sceneEnemy = Object.FindFirstObjectByType<FlowFieldEnemy>(
            FindObjectsInactive.Include);
        GameObject enemyPrefab = null;
        if (sceneEnemy != null)
        {
            PrepareActor(sceneEnemy.gameObject, false);
            enemyPrefab = SaveActorPrefab(sceneEnemy.gameObject, EnemyPrefabPath, false);
        }

        PrepareSceneNetworkObjects();
        NetworkPrefabsList prefabList = LoadOrCreatePrefabList();
        Register(prefabList, playerPrefab);
        Register(prefabList, enemyPrefab);
        RegisterConfiguredSpawnPrefabs(prefabList);

        NetworkManager manager = CreateOrConfigureNetworkManager(playerPrefab, prefabList);

        // The player is now spawned by NetworkManager. Keeping the original
        // disabled makes the migration reversible and avoids a duplicate actor.
        scenePlayer.gameObject.SetActive(false);
        EditorUtility.SetDirty(scenePlayer.gameObject);
        EditorUtility.SetDirty(manager);
        EditorUtility.SetDirty(prefabList);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveOpenScenes();

        Debug.Log(
            "[Network Setup] 完成：已创建 Player/Enemy 网络预制体、NetworkManager、" +
            "UnityTransport 与网络预制体列表。进入 Play Mode 后用左上角菜单启动 Host/Client。",
            manager);
        Selection.activeGameObject = manager.gameObject;
    }

    private static void PrepareSceneNetworkObjects()
    {
        foreach (NetworkBehaviour behaviour in Object.FindObjectsByType<NetworkBehaviour>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            if (PrefabUtility.IsPartOfPrefabAsset(behaviour)) continue;
            GameObject root = behaviour.transform.root.gameObject;
            GetOrAdd<NetworkObject>(root);
        }

        foreach (BossHealth boss in Object.FindObjectsByType<BossHealth>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
            PrepareActor(boss.gameObject, false);

        foreach (FlowFieldEnemy enemy in Object.FindObjectsByType<FlowFieldEnemy>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
            PrepareActor(enemy.gameObject, false);
    }

    private static void PrepareActor(GameObject actor, bool isPlayer)
    {
        GetOrAdd<NetworkObject>(actor);
        NetworkTransform networkTransform = GetOrAdd<NetworkTransform>(actor);
        networkTransform.Interpolate = true;
        networkTransform.UseUnreliableDeltas = true;

        if (isPlayer) GetOrAdd<PlayerNetworkState>(actor);

        Animator animator = actor.GetComponentInChildren<Animator>(true);
        bool replicatesEnemyState = actor.GetComponent<FlowFieldEnemy>() != null;
        if (animator != null && !replicatesEnemyState)
        {
            NetworkAnimator networkAnimator = GetOrAdd<NetworkAnimator>(actor);
            networkAnimator.Animator = animator;
        }
        else if (replicatesEnemyState && actor.TryGetComponent(out NetworkAnimator networkAnimator))
        {
            Object.DestroyImmediate(networkAnimator);
        }
        EditorUtility.SetDirty(actor);
    }

    private static GameObject SaveActorPrefab(GameObject source, string path, bool isPlayer)
    {
        // Player.prefab and Enemy.prefab are canonical persisted assets. Once
        // they exist, this legacy authoring command must not replace their
        // NetworkObject file IDs or serialized component references with a
        // scene clone. Runtime composition owns the wiring now.
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null)
        {
            if (existing.GetComponent<NetworkObject>() == null)
            {
                Debug.LogError(
                    $"[Network Setup] Canonical prefab '{path}' has no NetworkObject; " +
                    "refusing to overwrite it from the scene.",
                    existing);
            }

            return existing;
        }

        GameObject clone = Object.Instantiate(source);
        clone.name = source.name;
        clone.SetActive(true);
        PrepareActor(clone, isPlayer);
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(clone, path);
        Object.DestroyImmediate(clone);
        return prefab;
    }

    private static NetworkPrefabsList LoadOrCreatePrefabList()
    {
        NetworkPrefabsList list = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
        if (list != null) return list;
        list = ScriptableObject.CreateInstance<NetworkPrefabsList>();
        AssetDatabase.CreateAsset(list, PrefabListPath);
        return list;
    }

    private static void Register(NetworkPrefabsList list, GameObject prefab)
    {
        if (list == null || prefab == null || list.Contains(prefab)) return;
        list.Add(new NetworkPrefab { Override = NetworkPrefabOverride.None, Prefab = prefab });
    }

    private static void RegisterConfiguredSpawnPrefabs(NetworkPrefabsList list)
    {
        foreach (BossConfig config in Object.FindObjectsByType<BossConfig>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            if (config.projectilePrefab == null) continue;
            PreparePrefabAsset(config.projectilePrefab);
            Register(list, config.projectilePrefab);
        }

        foreach (EnemyShoot shooter in Object.FindObjectsByType<EnemyShoot>(
                     FindObjectsInactive.Include,
                     FindObjectsSortMode.None))
        {
            if (shooter.bulletPrefab == null) continue;
            PreparePrefabAsset(shooter.bulletPrefab);
            Register(list, shooter.bulletPrefab);
        }

    }

    private static void PreparePrefabAsset(GameObject prefab)
    {
        string path = AssetDatabase.GetAssetPath(prefab);
        if (string.IsNullOrEmpty(path)) return;
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        PrepareActor(root, false);
        PrefabUtility.SaveAsPrefabAsset(root, path);
        PrefabUtility.UnloadPrefabContents(root);
    }

    private static NetworkManager CreateOrConfigureNetworkManager(
        GameObject playerPrefab,
        NetworkPrefabsList prefabList)
    {
        NetworkManager manager = Object.FindFirstObjectByType<NetworkManager>(
            FindObjectsInactive.Include);
        if (manager == null)
        {
            GameObject managerObject = new("NetworkManager");
            Undo.RegisterCreatedObjectUndo(managerObject, "Create NetworkManager");
            manager = managerObject.AddComponent<NetworkManager>();
        }

        UnityTransport transport = GetOrAdd<UnityTransport>(manager.gameObject);
        GetOrAdd<NetworkRuntimeLauncher>(manager.gameObject);
        manager.NetworkConfig.NetworkTransport = transport;
        manager.NetworkConfig.PlayerPrefab = playerPrefab;
        manager.NetworkConfig.EnableSceneManagement = true;
        manager.NetworkConfig.TickRate = 30;
        manager.NetworkConfig.Prefabs.NetworkPrefabsLists.RemoveAll(list =>
            list != null &&
            AssetDatabase.GetAssetPath(list) ==
            "Assets/Networking/VampireHuntNetworkPrefabs.asset");
        if (!manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Contains(prefabList))
            manager.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(prefabList);
        return manager;
    }

    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(target);
    }
}
#endif
