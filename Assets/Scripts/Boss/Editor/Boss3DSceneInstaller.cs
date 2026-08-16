using System;
using System.Collections.Generic;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Edit-time installer for the scene-authored 3D Boss.
/// It deliberately lives under Boss/Editor so builds contain no scene-mutation code.
/// </summary>
internal static class Boss3DSceneInstaller
{
    private const string MenuPath = "Vampire Hunt/Boss/安装或更新当前场景的 3D Boss";
    private const string ModelPath =
        "Assets/Art/Characters/reimi_black/Re_reimi_black.fbx";
    private const string AnimationRoot =
        "Assets/Art/Characters/TEST Animation/Sword and Shield Pack/";
    private const string ProjectilePrefabPath =
        "Assets/Scripts/Boss/Prefabs/BossProjectile_3D.prefab";

    [MenuItem(MenuPath, priority = 120)]
    private static void InstallOrUpdate()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[Boss 安装] 请退出 Play 模式后再安装 3D Boss。");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("[Boss 安装] 当前没有可编辑的已加载场景。");
            return;
        }

        GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (modelAsset == null)
        {
            Debug.LogError($"[Boss 安装] 找不到模型：{ModelPath}");
            return;
        }

        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("安装 3D Boss");
        try
        {
            BossController existingController = FindSceneBoss(scene);
            GameObject existingRoot = existingController != null
                ? existingController.gameObject
                : null;

            GameObject bossRoot = IsInstalled3DBoss(existingRoot)
                ? existingRoot
                : ReplaceLegacyBoss(scene, existingRoot);

            ConfigureBoss(scene, bossRoot, modelAsset);
            bossRoot.SetActive(true);

            Selection.activeGameObject = bossRoot;
            EditorGUIUtility.PingObject(bossRoot);
            SceneView.lastActiveSceneView?.FrameSelected();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene))
            {
                Debug.LogError("[Boss 安装] 3D Boss 已放入场景，但场景保存失败。", bossRoot);
                return;
            }

            string issue = bossRoot
                .GetComponent<Boss3DAnimationPresenter>()
                .GetConfigurationIssue();
            if (!string.IsNullOrEmpty(issue))
            {
                Debug.LogWarning($"[Boss 安装] 3D Boss 已保存，但配置仍有问题：{issue}", bossRoot);
                return;
            }

            Debug.Log(
                "[Boss 安装] 已用 Re_reimi_black 3D 模型替换场景旧 2D Boss；" +
                "模型、碰撞体、挂点和剑盾动画均为场景内显式配置。",
                bossRoot);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            Undo.CollapseUndoOperations(undoGroup);
        }
    }

    [MenuItem(MenuPath, validate = true)]
    private static bool ValidateInstallOrUpdate()
    {
        return !EditorApplication.isPlayingOrWillChangePlaymode &&
               SceneManager.GetActiveScene().isLoaded;
    }

    private static BossController FindSceneBoss(Scene scene)
    {
        BossController selected = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponentInParent<BossController>(true)
            : null;
        if (selected != null && selected.gameObject.scene == scene)
        {
            return selected;
        }

        BossController[] candidates = UnityEngine.Object.FindObjectsByType<BossController>(
            FindObjectsInactive.Include);
        BossController fallback = null;
        foreach (BossController candidate in candidates)
        {
            if (candidate == null || candidate.gameObject.scene != scene)
            {
                continue;
            }

            if (candidate.name.Contains("Boss_BloodLord", StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }

            fallback ??= candidate;
        }

        return fallback;
    }

    private static bool IsInstalled3DBoss(GameObject root)
    {
        return root != null &&
               root.GetComponent<Boss3DAnimationPresenter>() != null &&
               root.transform.Find("Visual/Re_reimi_black_Model") != null;
    }

    private static GameObject ReplaceLegacyBoss(Scene scene, GameObject legacyRoot)
    {
        Transform oldTransform = legacyRoot != null ? legacyRoot.transform : null;
        Transform parent = oldTransform != null ? oldTransform.parent : null;
        int siblingIndex = oldTransform != null ? oldTransform.GetSiblingIndex() : -1;
        Vector3 localPosition = oldTransform != null
            ? oldTransform.localPosition
            : Vector3.zero;
        Quaternion localRotation = oldTransform != null
            ? oldTransform.localRotation
            : Quaternion.identity;
        Vector3 localScale = oldTransform != null
            ? oldTransform.localScale
            : Vector3.one;
        int layer = legacyRoot != null
            ? legacyRoot.layer
            : Mathf.Max(0, LayerMask.NameToLayer("Enemy"));
        string tag = legacyRoot != null ? legacyRoot.tag : "Untagged";
        BossConfig legacyConfig = legacyRoot != null
            ? legacyRoot.GetComponent<BossConfig>()
            : null;

        GameObject replacement = new("Boss_BloodLord_3D_INSTALLING");
        Undo.RegisterCreatedObjectUndo(replacement, "创建 3D Boss");
        SceneManager.MoveGameObjectToScene(replacement, scene);
        replacement.SetActive(false);
        replacement.layer = layer;
        replacement.tag = tag;
        replacement.transform.SetParent(parent, false);
        replacement.transform.SetLocalPositionAndRotation(localPosition, localRotation);
        replacement.transform.localScale = localScale;
        if (siblingIndex >= 0)
        {
            replacement.transform.SetSiblingIndex(siblingIndex);
        }

        AddCoreComponents(replacement, legacyConfig);

        if (legacyRoot != null)
        {
            Undo.DestroyObjectImmediate(legacyRoot);
        }

        replacement.name = "Boss_BloodLord";
        return replacement;
    }

    private static void AddCoreComponents(GameObject root, BossConfig legacyConfig)
    {
        Rigidbody body = Undo.AddComponent<Rigidbody>(root);
        body.mass = 1f;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.constraints = RigidbodyConstraints.FreezePositionY |
                           RigidbodyConstraints.FreezeRotation;

        CapsuleCollider capsule = Undo.AddComponent<CapsuleCollider>(root);
        capsule.isTrigger = true;
        capsule.radius = 0.55f;
        capsule.height = 2.2f;
        capsule.center = new Vector3(0f, capsule.height * 0.5f, 0f);

        AudioSource audioSource = Undo.AddComponent<AudioSource>(root);
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0.85f;

        BossConfig config = Undo.AddComponent<BossConfig>(root);
        if (legacyConfig != null)
        {
            EditorUtility.CopySerialized(legacyConfig, config);
        }

        Undo.AddComponent<NetworkObject>(root);
        Undo.AddComponent<BossHealth>(root);
        Undo.AddComponent<BossAttackController>(root);
        Undo.AddComponent<BossController>(root);
        Undo.AddComponent<Boss3DAnimationPresenter>(root);
    }

    private static void ConfigureBoss(Scene scene, GameObject root, GameObject modelAsset)
    {
        BossConfig config = GetOrAdd<BossConfig>(root);
        NetworkObject networkObject = GetOrAdd<NetworkObject>(root);
        BossHealth health = GetOrAdd<BossHealth>(root);
        BossAttackController attacks = GetOrAdd<BossAttackController>(root);
        BossController controller = GetOrAdd<BossController>(root);
        Boss3DAnimationPresenter presenter = GetOrAdd<Boss3DAnimationPresenter>(root);
        GetOrAdd<BossNetworkPrefabRegistrar>(root);
        Rigidbody body = GetOrAdd<Rigidbody>(root);
        CapsuleCollider capsule = GetOrAdd<CapsuleCollider>(root);
        AudioSource audioSource = GetOrAdd<AudioSource>(root);

        Transform visual = GetOrCreateChild(root.transform, "Visual", Vector3.zero);
        Transform meleePoint = GetOrCreateChild(
            root.transform,
            "MeleePoint",
            new Vector3(0f, 1.05f, 1.4f));
        Transform projectileOrigin = GetOrCreateChild(
            root.transform,
            "ProjectileOrigin",
            new Vector3(0f, 1.35f, 0.85f));
        Transform groundIndicator = GetOrCreateChild(
            root.transform,
            "GroundIndicator",
            Vector3.zero);
        Transform vfxRoot = GetOrCreateChild(
            root.transform,
            "VFXRoot",
            new Vector3(0f, 1.05f, 0f));

        GameObject model = GetOrCreateModel(scene, visual, modelAsset);
        NormalizeModelPrefabInstance(model);
        Animator animator = model.GetComponentInChildren<Animator>(true);
        if (animator == null)
        {
            throw new InvalidOperationException(
                $"模型 {ModelPath} 没有可用的 Animator。");
        }

        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        FitModelToBoss(model, root.transform, 2.2f);

        BossGuard leftGuard = GetOrCreateGuard(
            root.transform,
            "Guard_Left_Hitbox",
            BossGuardSide.Left,
            new Vector3(-2f, 0.9f, 0f),
            config);
        BossGuard rightGuard = GetOrCreateGuard(
            root.transform,
            "Guard_Right_Hitbox",
            BossGuardSide.Right,
            new Vector3(2f, 0.9f, 0f),
            config);

        Transform player = FindSceneTransform(scene, "Player");
        GameObject projectilePrefab = CreateOrUpdateProjectilePrefab();
        config.randomSpawnOnStart = false;
        config.arenaCenter = root.transform.position;
        config.minimumEffectHeight = root.transform.position.y + 0.02f;
        config.projectilePrefab = projectilePrefab;

        Assign(health, "stats", config);
        Assign(controller, "stats", config);
        Assign(controller, "bossHealth", health);
        Assign(controller, "attackController", attacks);
        Assign(controller, "bossRigidbody", body);
        Assign(controller, "bossCollider", capsule);
        Assign(controller, "animator", animator);
        Assign(controller, "audioSource", audioSource);
        Assign(controller, "player", player);
        Assign(controller, "visualRoot", visual);
        Assign(controller, "vfxRoot", vfxRoot);
        Assign(controller, "leftGuard", leftGuard);
        Assign(controller, "rightGuard", rightGuard);

        Assign(attacks, "stats", config);
        Assign(attacks, "player", player);
        Assign(attacks, "meleePoint", meleePoint);
        Assign(attacks, "projectileOrigin", projectileOrigin);
        Assign(attacks, "groundIndicator", groundIndicator);
        Assign(attacks, "vfxRoot", vfxRoot);
        Assign(attacks, "bossRigidbody", body);
        Assign(attacks, "animator", animator);
        Assign(attacks, "audioSource", audioSource);
        Assign(attacks, "leftGuard", leftGuard);
        Assign(attacks, "rightGuard", rightGuard);

        Assign(presenter, "controller", controller);
        Assign(presenter, "humanoidAnimator", animator);
        ConfigureAnimationClips(presenter);

        attacks.ConfigureMounts(
            meleePoint,
            projectileOrigin,
            groundIndicator,
            vfxRoot,
            animator);
        attacks.ConfigureGuards(leftGuard, rightGuard);

        EditorUtility.SetDirty(config);
        EditorUtility.SetDirty(networkObject);
        EditorUtility.SetDirty(root);
    }

    private static GameObject CreateOrUpdateProjectilePrefab()
    {
        EnsureAssetFolder("Assets/Scripts/Boss/Prefabs");

        bool loadedPrefabContents =
            AssetDatabase.LoadAssetAtPath<GameObject>(ProjectilePrefabPath) != null;
        GameObject contents = loadedPrefabContents
            ? PrefabUtility.LoadPrefabContents(ProjectilePrefabPath)
            : GameObject.CreatePrimitive(PrimitiveType.Sphere);

        try
        {
            contents.name = "BossProjectile_3D";
            contents.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            contents.transform.localScale = Vector3.one * 0.44f;

            SphereCollider collider = contents.GetComponent<SphereCollider>();
            if (collider == null)
            {
                collider = contents.AddComponent<SphereCollider>();
            }
            collider.isTrigger = true;
            collider.radius = 0.5f;

            Rigidbody body = contents.GetComponent<Rigidbody>();
            if (body == null)
            {
                body = contents.AddComponent<Rigidbody>();
            }
            body.useGravity = false;
            body.isKinematic = true;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

            if (contents.GetComponent<NetworkObject>() == null)
            {
                contents.AddComponent<NetworkObject>();
            }

            if (contents.GetComponent<NetworkTransform>() == null)
            {
                contents.AddComponent<NetworkTransform>();
            }

            if (contents.GetComponent<BossProjectile>() == null)
            {
                contents.AddComponent<BossProjectile>();
            }

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(contents, ProjectilePrefabPath);
            if (saved == null)
            {
                throw new InvalidOperationException(
                    $"无法创建 Boss 联机投射物预制体：{ProjectilePrefabPath}");
            }
        }
        finally
        {
            if (loadedPrefabContents)
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(contents);
            }
        }

        AssetDatabase.ImportAsset(ProjectilePrefabPath, ImportAssetOptions.ForceUpdate);
        return AssetDatabase.LoadAssetAtPath<GameObject>(ProjectilePrefabPath);
    }

    private static void EnsureAssetFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }

        int separator = folderPath.LastIndexOf('/');
        string parent = folderPath[..separator];
        string name = folderPath[(separator + 1)..];
        EnsureAssetFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    private static GameObject GetOrCreateModel(
        Scene scene,
        Transform visual,
        GameObject modelAsset)
    {
        Transform existing = visual.Find("Re_reimi_black_Model");
        if (existing != null)
        {
            return existing.gameObject;
        }

        GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, scene);
        if (model == null)
        {
            throw new InvalidOperationException($"无法实例化模型：{ModelPath}");
        }

        Undo.RegisterCreatedObjectUndo(model, "创建 Boss 3D 模型");
        model.name = "Re_reimi_black_Model";
        model.transform.SetParent(visual, false);
        model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        model.transform.localScale = Vector3.one;
        return model;
    }

    private static void FitModelToBoss(
        GameObject model,
        Transform bossRoot,
        float targetWorldHeight)
    {
        if (!TryGetBounds(model, out Bounds bounds) || bounds.size.y <= 0.001f)
        {
            return;
        }

        float scale = Mathf.Clamp(targetWorldHeight / bounds.size.y, 0.01f, 100f);
        model.transform.localScale *= scale;
        if (!TryGetBounds(model, out bounds))
        {
            return;
        }

        model.transform.position += Vector3.up * (bossRoot.position.y - bounds.min.y);
    }

    private static bool TryGetBounds(GameObject root, out Bounds bounds)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bounds = default;
        bool found = false;
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
            {
                continue;
            }

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return found;
    }

    private static BossGuard GetOrCreateGuard(
        Transform root,
        string name,
        BossGuardSide side,
        Vector3 localPosition,
        BossConfig config)
    {
        Transform guardTransform = GetOrCreateChild(root, name, localPosition);
        SphereCollider collider = GetOrAdd<SphereCollider>(guardTransform.gameObject);
        collider.isTrigger = true;
        collider.radius = 0.45f;
        BossGuard guard = GetOrAdd<BossGuard>(guardTransform.gameObject);
        guard.Configure(side, config);
        Assign(guard, "hitCollider", collider);
        Assign(guard, "bossRoot", root);
        return guard;
    }

    private static Transform GetOrCreateChild(
        Transform parent,
        string name,
        Vector3 localPosition)
    {
        Transform child = parent.Find(name);
        if (child == null)
        {
            GameObject childObject = new(name);
            Undo.RegisterCreatedObjectUndo(childObject, $"创建 {name}");
            childObject.layer = parent.gameObject.layer;
            child = childObject.transform;
            child.SetParent(parent, false);
        }

        child.localPosition = localPosition;
        child.localRotation = Quaternion.identity;
        child.localScale = Vector3.one;
        return child;
    }

    private static Transform FindSceneTransform(Scene scene, string rootName)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name.Equals(rootName, StringComparison.OrdinalIgnoreCase))
            {
                return root.transform;
            }
        }

        return null;
    }

    private static void ConfigureAnimationClips(Boss3DAnimationPresenter presenter)
    {
        SerializedObject serialized = new(presenter);
        SerializedProperty set = serialized.FindProperty("animations");
        SetClip(set, "idle", "Idle.anim");
        SetClip(set, "run", "run.anim");
        SetClip(set, "retreat", "walk.anim");
        SetClip(set, "phaseChange", "sword and shield power up.fbx");
        SetClip(set, "stagger", "sword and shield impact.fbx");
        SetClip(set, "executionImpact", "sword and shield impact (2).fbx");
        SetClip(set, "death", "sword and shield death.fbx");
        SetClip(set, "format1Sweep", "slash.anim");
        SetClip(set, "format2Barrage", "sword and shield casting.fbx");
        SetClip(set, "format3CrossSlash", "sword and shield slash (2).fbx");
        SetClip(set, "format4ChargedSlash", "sword and shield attack (4).fbx");
        SetClip(set, "format6Dash", "sword and shield run.fbx");
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(presenter);
    }

    private static void SetClip(
        SerializedProperty animationSet,
        string fieldName,
        string fileName)
    {
        AnimationClip clip = LoadAnimationClip(AnimationRoot + fileName);
        if (clip == null)
        {
            throw new InvalidOperationException($"找不到 Boss 动画：{fileName}");
        }

        animationSet.FindPropertyRelative(fieldName).objectReferenceValue = clip;
    }

    private static AnimationClip LoadAnimationClip(string assetPath)
    {
        AnimationClip direct = AssetDatabase.LoadAssetAtPath<AnimationClip>(assetPath);
        if (direct != null)
        {
            return direct;
        }

        foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
        {
            if (asset is AnimationClip clip &&
                !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
            {
                return clip;
            }
        }

        return null;
    }

    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(target);
    }

    private static void Assign(UnityEngine.Object target, string field, UnityEngine.Object value)
    {
        SerializedObject serialized = new(target);
        SerializedProperty property = serialized.FindProperty(field);
        if (property == null)
        {
            throw new MissingFieldException(target.GetType().Name, field);
        }

        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }

    private static void NormalizeModelPrefabInstance(GameObject model)
    {
        PropertyModification[] modifications =
            PrefabUtility.GetPropertyModifications(model);
        if (modifications != null)
        {
            List<PropertyModification> required = new(modifications.Length);
            foreach (PropertyModification modification in modifications)
            {
                if (modification != null && modification.propertyPath != "m_Layer")
                {
                    required.Add(modification);
                }
            }

            PrefabUtility.SetPropertyModifications(model, required.ToArray());
        }

        model.name = "Re_reimi_black_Model";
        model.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        model.transform.localScale = Vector3.one;
    }
}
