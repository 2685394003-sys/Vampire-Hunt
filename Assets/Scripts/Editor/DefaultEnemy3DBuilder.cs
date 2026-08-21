#if UNITY_EDITOR
using System;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Reproducibly builds the default network enemy with the low-poly Knight
/// visual while preserving gameplay data and animation behaviour.
/// </summary>
public static class DefaultEnemy3DBuilder
{
    private const string EnemyPrefabPath = "Assets/Prefabs/Network/Enemy.prefab";
    private const string KnightPrefabPath = "Assets/Prefabs/Characters/knight1.prefab";
    private const string PlayerControllerPath =
        "Assets/Art/Characters/TEST/Animation/TEST.controller";
    private const string VisualName = "Knight";
    private const string PreviousVisualName = "X Bot";
    private const int EnemyLayer = 7;

    [MenuItem("Tools/Vampire Hunt/Rebuild Default Enemy as 3D Knight")]
    public static void RebuildFromMenu()
    {
        Rebuild();
        Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
    }

    public static void RebuildFromCommandLine()
    {
        Rebuild();
    }

    private static void Rebuild()
    {
        ConfigureKnightAsHumanoid();
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(KnightPrefabPath);
        RuntimeAnimatorController playerController =
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PlayerControllerPath);
        if (model == null)
            throw new InvalidOperationException($"Could not load Knight at '{KnightPrefabPath}'.");
        if (playerController == null)
        {
            throw new InvalidOperationException(
                $"Could not load the temporary player Animator Controller at '{PlayerControllerPath}'.");
        }

        GameObject root = PrefabUtility.LoadPrefabContents(EnemyPrefabPath);
        try
        {
            RemoveLegacyPresentation(root);
            GameObject visual = CreateVisual(root.transform, model, playerController);
            ConfigureGameplayRoot(root, visual.GetComponentInChildren<Animator>(true));

            PrefabUtility.SaveAsPrefabAsset(root, EnemyPrefabPath, out bool saved);
            if (!saved)
                throw new InvalidOperationException($"Failed to save '{EnemyPrefabPath}'.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        ValidateSavedPrefab();
        Debug.Log(
            "[Default Enemy 3D] Enemy.prefab now uses Prefabs/Characters/knight1 " +
            "with the temporary Player Animator Controller. Existing stats and spawn references were preserved.");
    }

    private static void ConfigureKnightAsHumanoid()
    {
        string sourceModelPath = null;
        foreach (string dependency in AssetDatabase.GetDependencies(KnightPrefabPath, true))
        {
            if (dependency.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))
            {
                sourceModelPath = dependency;
                break;
            }
        }

        if (string.IsNullOrEmpty(sourceModelPath) ||
            AssetImporter.GetAtPath(sourceModelPath) is not ModelImporter importer)
        {
            throw new InvalidOperationException(
                $"Could not find the Knight source FBX used by '{KnightPrefabPath}'.");
        }

        if (importer.animationType != ModelImporterAnimationType.Human ||
            importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.SaveAndReimport();
        }

        GameObject sourceModel = AssetDatabase.LoadAssetAtPath<GameObject>(sourceModelPath);
        Animator sourceAnimator = sourceModel != null
            ? sourceModel.GetComponentInChildren<Animator>(true)
            : null;
        Avatar avatar = sourceAnimator != null ? sourceAnimator.avatar : null;
        if (avatar == null || !avatar.isHuman || !avatar.isValid)
        {
            throw new InvalidOperationException(
                $"Knight Humanoid Avatar is invalid after importing '{sourceModelPath}'.");
        }
    }

    private static void RemoveLegacyPresentation(GameObject root)
    {
        Transform existingVisual = root.transform.Find(VisualName);
        if (existingVisual != null)
            UnityEngine.Object.DestroyImmediate(existingVisual.gameObject);

        Transform previousVisual = root.transform.Find(PreviousVisualName);
        if (previousVisual != null)
            UnityEngine.Object.DestroyImmediate(previousVisual.gameObject);

        if (root.TryGetComponent(out SpriteRenderer spriteRenderer))
            UnityEngine.Object.DestroyImmediate(spriteRenderer);
        if (root.TryGetComponent(out Animator rootAnimator))
            UnityEngine.Object.DestroyImmediate(rootAnimator);
        if (root.TryGetComponent(out NetworkAnimator networkAnimator))
            UnityEngine.Object.DestroyImmediate(networkAnimator);
    }

    private static GameObject CreateVisual(
        Transform parent,
        GameObject model,
        RuntimeAnimatorController controller)
    {
        GameObject visual = PrefabUtility.InstantiatePrefab(model, parent) as GameObject;
        if (visual == null)
            throw new InvalidOperationException("Unity could not instantiate the Knight model prefab.");

        visual.name = VisualName;
        visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        visual.transform.localScale = Vector3.one;
        SetLayerRecursively(visual, EnemyLayer);

        Animator animator = visual.GetComponentInChildren<Animator>(true);
        if (animator == null)
            throw new InvalidOperationException("The imported Knight does not contain an Animator.");

        if (animator.avatar == null || !animator.avatar.isHuman || !animator.avatar.isValid)
            throw new InvalidOperationException("The imported Knight does not have a valid Humanoid Avatar.");

        animator.runtimeAnimatorController = controller;
        animator.applyRootMotion = false;
        animator.updateMode = AnimatorUpdateMode.Normal;
        animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;

        foreach (Renderer renderer in visual.GetComponentsInChildren<Renderer>(true))
        {
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        return visual;
    }

    private static void ConfigureGameplayRoot(GameObject root, Animator animator)
    {
        root.layer = EnemyLayer;

        if (root.TryGetComponent(out BoxCollider boxCollider))
            UnityEngine.Object.DestroyImmediate(boxCollider);
        CapsuleCollider capsule = root.GetComponent<CapsuleCollider>();
        if (capsule == null)
            capsule = root.AddComponent<CapsuleCollider>();
        capsule.direction = 1;
        capsule.center = new Vector3(0f, 0.9f, 0f);
        capsule.height = 1.8f;
        capsule.radius = 0.35f;

        Rigidbody body = root.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.constraints = RigidbodyConstraints.FreezeRotationX |
                               RigidbodyConstraints.FreezeRotationZ;
            body.interpolation = RigidbodyInterpolation.Interpolate;
        }

        Transform attackPoint = root.transform.Find("EnemyAttackpoint");
        if (attackPoint != null)
        {
            attackPoint.localPosition = new Vector3(0f, 1f, 0.75f);
            attackPoint.localRotation = Quaternion.identity;
            attackPoint.localScale = Vector3.one;
            attackPoint.gameObject.layer = EnemyLayer;
        }

        EnemyAnimationController animationController =
            root.GetComponent<EnemyAnimationController>();
        if (animationController == null)
            animationController = root.AddComponent<EnemyAnimationController>();
        animationController.Configure(animator);
    }

    private static void SetLayerRecursively(GameObject target, int layer)
    {
        target.layer = layer;
        foreach (Transform child in target.transform)
            SetLayerRecursively(child.gameObject, layer);
    }

    private static void ValidateSavedPrefab()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
        if (prefab == null ||
            prefab.GetComponent<NetworkObject>() == null ||
            prefab.GetComponent<FlowFieldEnemy>() == null ||
            prefab.GetComponent<EnemyHealth>() == null ||
            prefab.GetComponent<EnemyCombat>() == null ||
            prefab.GetComponent<EnemyAnimationController>() == null ||
            prefab.transform.Find(VisualName) == null ||
            prefab.GetComponentInChildren<Animator>(true) == null ||
            prefab.GetComponentInChildren<Renderer>(true) == null)
        {
            throw new InvalidOperationException(
                "The saved default enemy prefab failed its required component validation.");
        }
    }
}
#endif
