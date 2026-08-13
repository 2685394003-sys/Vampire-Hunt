#if UNITY_EDITOR
using System;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Reproducibly upgrades the default network enemy from its legacy sprite
/// visual to the Sword and Shield Pack X Bot while preserving gameplay data.
/// </summary>
public static class DefaultEnemy3DBuilder
{
    private const string EnemyPrefabPath = "Assets/Prefabs/Network/Enemy.prefab";
    private const string XBotPath =
        "Assets/Art/Characters/TEST Animation/Sword and Shield Pack/X Bot.fbx";
    private const string PlayerControllerPath =
        "Assets/Art/Characters/TEST/Animation/TEST.controller";
    private const string VisualName = "X Bot";
    private const int EnemyLayer = 7;

    [InitializeOnLoadMethod]
    private static void ScheduleRequestedUpgrade()
    {
        EditorApplication.delayCall += TryRunRequestedUpgrade;
    }

    private static void TryRunRequestedUpgrade()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
            return;
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(EnemyPrefabPath);
        if (prefab == null ||
            (prefab.transform.Find(VisualName) != null &&
             prefab.GetComponent<SpriteRenderer>() == null))
        {
            return;
        }

        try
        {
            Rebuild();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
    }

    private static void HandlePlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode)
            return;

        EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
        EditorApplication.delayCall += TryRunRequestedUpgrade;
    }

    [MenuItem("Tools/Vampire Hunt/Rebuild Default Enemy as 3D X Bot")]
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
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(XBotPath);
        RuntimeAnimatorController playerController =
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PlayerControllerPath);
        if (model == null)
            throw new InvalidOperationException($"Could not load X Bot at '{XBotPath}'.");
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
            "[Default Enemy 3D] Enemy.prefab now uses Sword and Shield Pack/X Bot " +
            "with the temporary Player Animator Controller. Existing stats and spawn references were preserved.");
    }

    private static void RemoveLegacyPresentation(GameObject root)
    {
        Transform existingVisual = root.transform.Find(VisualName);
        if (existingVisual != null)
            UnityEngine.Object.DestroyImmediate(existingVisual.gameObject);

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
            throw new InvalidOperationException("Unity could not instantiate the X Bot model prefab.");

        visual.name = VisualName;
        visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        visual.transform.localScale = Vector3.one;
        SetLayerRecursively(visual, EnemyLayer);

        Animator animator = visual.GetComponentInChildren<Animator>(true);
        if (animator == null)
            throw new InvalidOperationException("The imported X Bot does not contain an Animator.");

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
            prefab.GetComponent<FlowFieldEnemy>() == null ||
            prefab.GetComponent<EnemyHealth>() == null ||
            prefab.GetComponent<EnemyCombat>() == null ||
            prefab.GetComponent<EnemyAnimationController>() == null ||
            prefab.transform.Find(VisualName) == null ||
            prefab.GetComponentInChildren<Animator>(true) == null ||
            prefab.GetComponentInChildren<SkinnedMeshRenderer>(true) == null)
        {
            throw new InvalidOperationException(
                "The saved default enemy prefab failed its required component validation.");
        }
    }
}
#endif
