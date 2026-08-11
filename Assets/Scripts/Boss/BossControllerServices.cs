using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// Physics and navigation boundary for BossController.
/// Keeps movement policy out of the encounter coordinator.
/// </summary>
internal sealed class BossMovementMotor
{
    private readonly Transform actor;
    private readonly Rigidbody body;
    private readonly BossConfig config;

    public BossMovementMotor(
        Transform actorTransform,
        Rigidbody rigidbody,
        Collider hitCollider,
        BossConfig bossConfig)
    {
        actor = actorTransform;
        body = rigidbody;
        config = bossConfig;

        ConfigureTopDownPhysics(hitCollider);
    }

    public Vector3 GetPlanarOffset(Transform target)
    {
        return target == null
            ? Vector3.zero
            : Vector3.ProjectOnPlane(target.position - actor.position, Vector3.up);
    }

    public void Face(Vector3 planarDirection, float deltaTime)
    {
        if (planarDirection.sqrMagnitude < 0.001f || config == null)
        {
            return;
        }

        Quaternion targetRotation =
            Quaternion.LookRotation(planarDirection.normalized, Vector3.up);
        actor.rotation = Quaternion.Slerp(
            actor.rotation,
            targetRotation,
            config.turnSpeed * deltaTime);
    }

    public void Move(Vector3 planarDirection, int phase, float deltaTime)
    {
        if (planarDirection.sqrMagnitude < 0.001f || config == null)
        {
            Stop();
            return;
        }

        Vector3 current = body != null ? body.position : actor.position;
        Vector3 next = current +
                       planarDirection.normalized *
                       (config.GetMoveSpeed(phase) * deltaTime);
        next.y = current.y;

        if (body != null)
        {
            body.MovePosition(next);
        }
        else
        {
            actor.position = next;
        }
    }

    public void Stop()
    {
        if (body == null)
        {
            return;
        }

        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
    }

    public bool IsVisible(Camera camera)
    {
        if (camera == null || config == null)
        {
            return true;
        }

        Vector3 viewport = camera.WorldToViewportPoint(actor.position);
        float padding = config.viewportPadding;
        return viewport.z > 0f &&
               viewport.x >= -padding &&
               viewport.x <= 1f + padding &&
               viewport.y >= -padding &&
               viewport.y <= 1f + padding;
    }

    public bool TryTeleportToArena(Transform targetToAvoid)
    {
        if (config == null)
        {
            return false;
        }

        Vector3 fallback = actor.position;
        int attempts = Mathf.Max(1, config.spawnPositionAttempts);
        for (int i = 0; i < attempts; i++)
        {
            Vector3 candidate = new(
                config.arenaCenter.x +
                Random.Range(-config.arenaHalfSize.x, config.arenaHalfSize.x),
                fallback.y,
                config.arenaCenter.z +
                Random.Range(-config.arenaHalfSize.y, config.arenaHalfSize.y));

            if (targetToAvoid != null)
            {
                Vector3 offset = Vector3.ProjectOnPlane(
                    candidate - targetToAvoid.position,
                    Vector3.up);
                if (offset.magnitude < config.spawnMinDistanceFromPlayer)
                {
                    continue;
                }
            }

            if (config.obstacleLayer.value != 0 &&
                Physics.CheckSphere(
                    candidate,
                    config.spawnObstacleCheckRadius,
                    config.obstacleLayer,
                    QueryTriggerInteraction.Ignore))
            {
                continue;
            }

            SetPosition(candidate);
            return true;
        }

        SetPosition(fallback);
        return false;
    }

    public void ClearNearbyObstacles()
    {
        if (config == null || config.obstacleLayer.value == 0)
        {
            return;
        }

        LevelGenerator generator = Object.FindFirstObjectByType<LevelGenerator>();
        generator?.RemoveObstaclesNear(actor.position, config.phaseClearObstacleRadius);
        if (NetworkAuthority.IsNetworkActive)
        {
            return;
        }

        Collider[] obstacles = Physics.OverlapSphere(
            actor.position,
            config.phaseClearObstacleRadius,
            config.obstacleLayer,
            QueryTriggerInteraction.Ignore);

        foreach (Collider obstacle in obstacles)
        {
            if (obstacle != null &&
                obstacle.transform != actor &&
                !obstacle.transform.IsChildOf(actor))
            {
                obstacle.gameObject.SetActive(false);
            }
        }
    }

    private void ConfigureTopDownPhysics(Collider hitCollider)
    {
        if (hitCollider != null)
        {
            hitCollider.isTrigger = true;
        }

        if (body == null)
        {
            return;
        }

        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.constraints |= RigidbodyConstraints.FreezePositionY |
                            RigidbodyConstraints.FreezeRotationX |
                            RigidbodyConstraints.FreezeRotationZ;
    }

    private void SetPosition(Vector3 position)
    {
        if (body != null)
        {
            body.position = position;
        }

        actor.position = position;
    }
}

/// <summary>
/// Presentation boundary for Animator, audio and runtime-only effects.
/// The gameplay coordinator never needs to know LineRenderer details.
/// </summary>
internal sealed class BossPresentationGateway
{
    private readonly Transform actor;
    private readonly BossConfig config;
    private readonly Animator animator;
    private readonly AudioSource audioSource;
    private readonly Transform visualRoot;
    private readonly Transform vfxRoot;
    private readonly NetworkAnimator networkAnimator;

    private Renderer[] renderers = System.Array.Empty<Renderer>();
    private GameObject contractVfxObject;
    private LineRenderer contractVfxLine;
    private GameObject phaseRainObject;

    public Animator Animator => animator;

    public BossPresentationGateway(
        Transform actorTransform,
        BossConfig bossConfig,
        Animator targetAnimator,
        AudioSource targetAudioSource,
        Transform targetVisualRoot,
        Transform targetVfxRoot)
    {
        actor = actorTransform;
        config = bossConfig;
        animator = targetAnimator;
        audioSource = targetAudioSource;
        visualRoot = targetVisualRoot;
        vfxRoot = targetVfxRoot;
        networkAnimator = actor != null ? actor.GetComponent<NetworkAnimator>() : null;
    }

    public void Initialize()
    {
        EnsureDebugVisual();
        RefreshRenderers();
    }

    public void RefreshRenderers()
    {
        renderers = actor.GetComponentsInChildren<Renderer>(true);
    }

    public void SetRenderersEnabled(bool value)
    {
        foreach (Renderer targetRenderer in renderers)
        {
            if (targetRenderer != null)
            {
                targetRenderer.enabled = value;
            }
        }
    }

    public void PlayOneShot(AudioClip clip)
    {
        if (audioSource != null && clip != null)
        {
            audioSource.PlayOneShot(clip);
        }
    }

    public bool TrySetTrigger(string triggerName)
    {
        if (!HasParameter(triggerName, AnimatorControllerParameterType.Trigger))
        {
            return false;
        }

        if (NetworkAuthority.IsNetworkActive && networkAnimator != null)
        {
            if (NetworkAuthority.IsServerOrOffline() && networkAnimator.IsSpawned)
                networkAnimator.SetTrigger(triggerName);
            return true;
        }

        animator.SetTrigger(triggerName);
        return true;
    }

    public bool TrySetInteger(string parameterName, int value)
    {
        if (!HasParameter(parameterName, AnimatorControllerParameterType.Int))
        {
            return false;
        }

        animator.SetInteger(parameterName, value);
        return true;
    }

    public bool HasParameter(
        string parameterName,
        AnimatorControllerParameterType expectedType)
    {
        if (animator == null ||
            animator.runtimeAnimatorController == null ||
            string.IsNullOrWhiteSpace(parameterName))
        {
            return false;
        }

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.name == parameterName && parameter.type == expectedType)
            {
                return true;
            }
        }

        return false;
    }

    public void CreateContractVfx()
    {
        if (contractVfxObject != null || config == null)
        {
            return;
        }

        contractVfxObject = new GameObject("Boss_Format5_ContractSiphon");
        contractVfxObject.layer = actor.gameObject.layer;
        contractVfxObject.transform.SetParent(vfxRoot != null ? vfxRoot : actor, false);

        contractVfxLine = contractVfxObject.AddComponent<LineRenderer>();
        contractVfxLine.useWorldSpace = true;
        contractVfxLine.positionCount = 2;
        contractVfxLine.startWidth = config.telegraphLineWidth * 1.4f;
        contractVfxLine.endWidth = config.telegraphLineWidth * 0.55f;
        contractVfxLine.startColor = config.projectileColor;
        contractVfxLine.endColor = config.warningColor;
        contractVfxLine.sortingOrder = 102;

        if (config.telegraphMaterial != null)
        {
            contractVfxLine.sharedMaterial = config.telegraphMaterial;
            return;
        }

        Shader shader = Shader.Find("Sprites/Default");
        if (shader != null)
        {
            contractVfxLine.material = new Material(shader);
        }
    }

    public void UpdateContractVfx(Transform target)
    {
        if (contractVfxLine == null || target == null || config == null)
        {
            return;
        }

        float minimumY = config.GetEffectHeight();
        Vector3 targetPoint = target.position;
        Vector3 bossPoint = actor.position;
        targetPoint.y = Mathf.Max(targetPoint.y, minimumY);
        bossPoint.y = Mathf.Max(bossPoint.y, minimumY);
        contractVfxLine.SetPosition(0, targetPoint);
        contractVfxLine.SetPosition(1, bossPoint);

        float pulse = 0.65f + Mathf.Sin(Time.unscaledTime * 12f) * 0.35f;
        Color color = config.projectileColor;
        color.a *= pulse;
        contractVfxLine.startColor = color;
    }

    public void DestroyContractVfx()
    {
        if (contractVfxObject != null)
        {
            Object.Destroy(contractVfxObject);
        }

        contractVfxObject = null;
        contractVfxLine = null;
    }

    public void StartPhaseThreeRain()
    {
        if (config == null || !config.createPhase3Rain || phaseRainObject != null)
        {
            return;
        }

        phaseRainObject = new GameObject("Boss_Phase3_BloodRain");
        phaseRainObject.layer = actor.gameObject.layer;
        phaseRainObject.transform.SetParent(vfxRoot != null ? vfxRoot : actor, true);
        phaseRainObject.AddComponent<BossPhaseRain>().Initialize(config);
    }

    public void Dispose()
    {
        SetRenderersEnabled(true);
        DestroyContractVfx();

        if (phaseRainObject != null)
        {
            Object.Destroy(phaseRainObject);
            phaseRainObject = null;
        }
    }

    private void EnsureDebugVisual()
    {
        Renderer existingVisual = visualRoot != null
            ? visualRoot.GetComponentInChildren<Renderer>(true)
            : actor.GetComponentInChildren<Renderer>(true);
        if (config == null || !config.createDebugVisualIfMissing || existingVisual != null)
        {
            return;
        }

        Transform parent = visualRoot != null ? visualRoot : actor;
        GameObject debugVisual = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        debugVisual.name = "Boss_DebugVisual_Runtime";
        debugVisual.layer = actor.gameObject.layer;
        debugVisual.transform.SetParent(parent, false);
        debugVisual.transform.localPosition = new Vector3(0f, 1f, 0f);
        debugVisual.transform.localScale = new Vector3(1.2f, 1.2f, 1.2f);

        Collider generatedCollider = debugVisual.GetComponent<Collider>();
        if (generatedCollider != null)
        {
            Object.Destroy(generatedCollider);
        }

        Renderer renderer = debugVisual.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.material.color = config.debugBossColor;
        }
    }
}

/// <summary>
/// Resolves the configured combat target without a compile-time dependency on
/// Player or NPC implementations.
/// </summary>
internal static class BossTargetResolver
{
    public static Transform Resolve(Transform current, BossConfig config)
    {
        PlayerNetworkState closest = NetworkPlayerRegistry.GetClosestAlive(
            current != null ? current.position : Vector3.zero);
        if (closest != null)
        {
            BossCombatTarget.EnsurePlayerAdapter(closest.transform, true);
            return closest.transform;
        }

        if (IsUsable(current))
        {
            BossCombatTarget.EnsurePlayerAdapter(current, false);
            return current;
        }

        GameObject candidate = FindTaggedPlayer();
        if (candidate == null && config != null && config.playerLayer.value != 0)
        {
            Collider[] colliders = Object.FindObjectsByType<Collider>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            foreach (Collider collider in colliders)
            {
                if (collider != null &&
                    (config.playerLayer.value & (1 << collider.gameObject.layer)) != 0)
                {
                    candidate = collider.transform.root.gameObject;
                    break;
                }
            }
        }

        Transform resolved = candidate != null ? candidate.transform : null;
        if (resolved != null)
        {
            BossCombatTarget.EnsurePlayerAdapter(resolved, true);
        }

        return resolved;
    }

    public static bool IsUsable(Transform target)
    {
        if (target == null || !target.gameObject.activeInHierarchy) return false;
        PlayerNetworkState state = target.GetComponentInParent<PlayerNetworkState>();
        return state == null || state.IsAlive;
    }

    private static GameObject FindTaggedPlayer()
    {
        try
        {
            return GameObject.FindGameObjectWithTag("Player");
        }
        catch (UnityException)
        {
            return null;
        }
    }
}

/// <summary>
/// Migration safety net: legacy scenes that contain BossHealth but omitted the
/// coordinator receive BossController after the scene is loaded.
/// </summary>
internal static class BossControllerMigrationBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachMissingControllers()
    {
        BossHealth[] healthComponents = Object.FindObjectsByType<BossHealth>(
            FindObjectsInactive.Include,
            FindObjectsSortMode.None);
        foreach (BossHealth health in healthComponents)
        {
            if (health != null && health.GetComponent<BossController>() == null)
            {
                health.gameObject.AddComponent<BossController>();
            }
        }
    }
}
