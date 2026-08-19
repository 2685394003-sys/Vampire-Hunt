using System;
using System.Collections.Generic;
using Unity.Netcode.Components;
using UnityEngine;
using VampireHunt.Boss.Application;
using VampireHunt.Boss.Contracts;
using VampireHunt.Boss.Domain;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;
using EntityId = VampireHunt.Core.EntityId;

/// <summary>Unity clock adapter; no domain code reads Time directly.</summary>
internal sealed class BossUnityClock : IGameClock
{
    public double Now => Time.timeAsDouble;
    public float DeltaTime => Time.deltaTime;
}

internal sealed class BossUnityRandom : IRandomSource
{
    public float NextFloat() => UnityEngine.Random.value;
    public int NextInt(int minInclusive, int maxExclusive) => UnityEngine.Random.Range(minInclusive, maxExclusive);
}

/// <summary>Physics/motor adapter for immutable attack movement plans.</summary>
internal sealed class BossMovementMotor : IBossMotor
{
    private readonly Transform actor;
    private readonly Rigidbody body;
    private readonly Collider hitCollider;
    private readonly BossConfig config;

    public BossMovementMotor(Transform actorTransform, Rigidbody rigidbody, Collider collider, BossConfig bossConfig)
    {
        actor = actorTransform;
        body = rigidbody;
        hitCollider = collider;
        config = bossConfig;
        ConfigureTopDownPhysics();
    }

    public Vector3 GetPlanarOffset(Transform target) => target == null
        ? Vector3.zero
        : Vector3.ProjectOnPlane(target.position - actor.position, Vector3.up);

    public void Face(Vector3 planarDirection, float deltaTime)
    {
        if (actor == null || planarDirection.sqrMagnitude <= 0.000001f || config == null) return;
        Quaternion target = Quaternion.LookRotation(planarDirection.normalized, Vector3.up);
        actor.rotation = Quaternion.Slerp(actor.rotation, target,
            Mathf.Clamp01(Mathf.Max(0f, config.turnSpeed) * Mathf.Max(0f, deltaTime)));
    }

    public void Move(Vector3 planarDirection, int phase, float deltaTime) =>
        MoveAtSpeed(planarDirection, config != null ? config.GetMoveSpeed(phase) : 0f, deltaTime);

    public void MoveAtSpeed(Vector3 planarDirection, float speed, float deltaTime)
    {
        if (actor == null || planarDirection.sqrMagnitude <= 0.000001f || speed <= 0f)
        {
            Stop();
            return;
        }

        Vector3 current = body != null ? body.position : actor.position;
        Vector3 next = current + planarDirection.normalized * (speed * Mathf.Max(0f, deltaTime));
        next.y = current.y;
        SetPosition(next);
    }

    public void Execute(EntityId bossId, in MovementPlan plan)
    {
        if (actor == null || plan.Kind == MovementPlanKind.None) return;
        if (plan.Kind == MovementPlanKind.Dash)
        {
            SetPosition(new Vector3(plan.To.X, actor.position.y, plan.To.Z));
            return;
        }

        Vector3 direction = new(plan.To.X - plan.From.X, 0f, plan.To.Z - plan.From.Z);
        MoveAtSpeed(direction, plan.Speed, plan.Duration);
    }

    public void Stop()
    {
        if (body == null) return;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
    }

    public bool IsVisible(Camera camera)
    {
        if (actor == null || camera == null || config == null) return true;
        Vector3 viewport = camera.WorldToViewportPoint(actor.position);
        float padding = Mathf.Max(0f, config.viewportPadding);
        return viewport.z > 0f && viewport.x >= -padding && viewport.x <= 1f + padding &&
               viewport.y >= -padding && viewport.y <= 1f + padding;
    }

    public bool TryTeleportToArena(Transform targetToAvoid)
    {
        if (actor == null || config == null) return false;
        Vector3 fallback = actor.position;
        int attempts = Mathf.Max(1, config.spawnPositionAttempts);
        for (int i = 0; i < attempts; i++)
        {
            Vector3 candidate = new(
                config.arenaCenter.x + UnityEngine.Random.Range(-config.arenaHalfSize.x, config.arenaHalfSize.x),
                fallback.y,
                config.arenaCenter.z + UnityEngine.Random.Range(-config.arenaHalfSize.y, config.arenaHalfSize.y));
            if (targetToAvoid != null &&
                Vector3.ProjectOnPlane(candidate - targetToAvoid.position, Vector3.up).magnitude < config.spawnMinDistanceFromPlayer)
                continue;
            if (config.obstacleLayer.value != 0 && Physics.CheckSphere(
                    candidate, config.spawnObstacleCheckRadius, config.obstacleLayer, QueryTriggerInteraction.Ignore))
                continue;
            SetPosition(candidate);
            return true;
        }
        SetPosition(fallback);
        return false;
    }

    public void ClearNearbyObstacles()
    {
        if (config == null || config.obstacleLayer.value == 0 || actor == null) return;
        Collider[] obstacles = Physics.OverlapSphere(actor.position, config.phaseClearObstacleRadius,
            config.obstacleLayer, QueryTriggerInteraction.Ignore);
        foreach (Collider obstacle in obstacles)
        {
            if (obstacle != null && obstacle.transform != actor && !obstacle.transform.IsChildOf(actor))
                obstacle.gameObject.SetActive(false);
        }
    }

    private void ConfigureTopDownPhysics()
    {
        if (hitCollider != null) hitCollider.isTrigger = true;
        if (body == null) return;
        body.useGravity = false;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.constraints |= RigidbodyConstraints.FreezePositionY |
                            RigidbodyConstraints.FreezeRotationX |
                            RigidbodyConstraints.FreezeRotationZ;
    }

    private void SetPosition(Vector3 position)
    {
        if (body != null) body.MovePosition(position);
        else if (actor != null) actor.position = position;
    }
}

/// <summary>Authoritative target pose adapter for BossAttackService.</summary>
internal sealed class BossWorldStateAdapter : IBossWorldState
{
    private readonly Transform actor;
    private readonly Func<Transform> targetProvider;
    private readonly BossConfig config;

    public BossWorldStateAdapter(Transform actorTransform, Func<Transform> target, BossConfig bossConfig)
    {
        actor = actorTransform;
        targetProvider = target;
        config = bossConfig;
    }

    public bool TryGetAttackContext(EntityId bossId, BossPhase phase, double now, out BossAttackContext context)
    {
        Transform target = targetProvider?.Invoke();
        if (actor == null || !BossTargetResolver.IsUsable(target))
        {
            context = default;
            return false;
        }
        Vector3 origin = actor.position;
        Vector3 targetPosition = target.position;
        float effectHeight = config != null ? config.GetEffectHeight() : -0.99f;
        origin.y = Mathf.Max(origin.y, effectHeight);
        targetPosition.y = Mathf.Max(targetPosition.y, effectHeight);
        context = new BossAttackContext(
            bossId,
            phase,
            new WorldPosition(origin.x, origin.y, origin.z),
            new WorldPosition(targetPosition.x, targetPosition.y, targetPosition.z),
            now);
        return true;
    }
}

/// <summary>Physics query adapter; all damage is still applied by CombatApplicationService.</summary>
internal sealed class BossAttackWorldQueryAdapter : IAttackWorldQuery
{
    private readonly BossCombatEntityDirectory directory;
    private readonly LayerMask targetMask;
    private readonly Collider[] colliders = new Collider[128];
    private readonly HashSet<EntityId> seen = new();

    public BossAttackWorldQueryAdapter(BossCombatEntityDirectory entityDirectory, LayerMask playerLayer)
    {
        directory = entityDirectory ?? throw new ArgumentNullException(nameof(entityDirectory));
        targetMask = playerLayer;
    }

    public int CollectTargets(EntityId bossId, in DamageWindow window, IList<ICombatTarget> buffer)
    {
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));
        buffer.Clear();
        seen.Clear();
        if (!bossId.IsValid || window.Range <= 0f) return 0;

        Vector3 origin = ToVector3(window.Origin);
        int count = Physics.OverlapSphereNonAlloc(origin, window.Range, colliders, targetMask,
            QueryTriggerInteraction.Ignore);
        Vector3 axis = ToVector3(window.Target) - origin;
        axis.y = 0f;
        axis = axis.sqrMagnitude > 0.000001f ? axis.normalized : Vector3.forward;
        for (int i = 0; i < count; i++)
        {
            Collider collider = colliders[i];
            if (!BossCombatTarget.TryGetCombatTarget(collider, out ICombatTarget target) ||
                target == null || !target.IsAlive || target.Id == bossId || !seen.Add(target.Id))
                continue;
            if (!Contains(window, origin, axis, ToVector3(target.Position))) continue;
            directory.Bind(target);
            buffer.Add(target);
        }
        return buffer.Count;
    }

    private static bool Contains(in DamageWindow window, Vector3 origin, Vector3 axis, Vector3 point)
    {
        Vector3 delta = point - origin;
        float range = delta.magnitude;
        if (range <= 0f) return true;
        if (window.Shape is TelegraphShape.Circle or TelegraphShape.Radial)
            return range <= window.Range;
        float along = Vector3.Dot(delta, axis);
        if (along < 0f || along > window.Range) return false;
        Vector3 perpendicular = delta - axis * along;
        float halfWidth = Mathf.Max(0.01f, window.Width * 0.5f);
        return perpendicular.sqrMagnitude <= halfWidth * halfWidth;
    }

    private static Vector3 ToVector3(WorldPosition value) => new(value.X, value.Y, value.Z);
}

/// <summary>Server projectile spawn adapter. Projectile collision feeds Combat via the hit callback.</summary>
internal sealed class BossProjectileSpawnerAdapter : IProjectileSpawner
{
    private readonly BossConfig config;
    private readonly Transform owner;
    private readonly Action<EntityId, ICombatTarget, int, WorldPosition, float> hit;

    public BossProjectileSpawnerAdapter(
        BossConfig bossConfig,
        Transform ownerTransform,
        Action<EntityId, ICombatTarget, int, WorldPosition, float> hitCallback)
    {
        config = bossConfig;
        owner = ownerTransform;
        hit = hitCallback;
    }

    public void Spawn(EntityId bossId, BossAttackId attackId, in ProjectileRequest request)
    {
        if (config == null || config.projectilePrefab == null || request.Count <= 0) return;
        Vector3 origin = ToVector3(request.Origin);
        Vector3 target = ToVector3(request.Target);
        Vector3 direction = Vector3.ProjectOnPlane(target - origin, Vector3.up);
        if (direction.sqrMagnitude <= 0.000001f) direction = owner != null ? owner.forward : Vector3.forward;
        direction.Normalize();

        for (int i = 0; i < request.Count; i++)
        {
            float angle = request.Count > 1 ? request.RotationOffsetDegrees * i : 0f;
            Vector3 launch = Quaternion.AngleAxis(angle, Vector3.up) * direction;
            GameObject instance = NetworkSpawnUtility.Spawn(config.projectilePrefab, origin,
                Quaternion.LookRotation(launch, Vector3.up));
            if (instance == null) continue;
            BossProjectile projectile = instance.GetComponent<BossProjectile>();
            projectile?.Initialize(
                launch,
                request.Speed,
                request.Damage,
                0f,
                request.Lifetime,
                config.playerLayer,
                config.obstacleLayer,
                owner,
                config.minimumEffectHeight,
                (targetObject, damage, position, projectileKnockback) =>
                    hit?.Invoke(bossId, targetObject, damage, position, projectileKnockback));
        }
    }

    private static Vector3 ToVector3(WorldPosition value) => new(value.X, value.Y, value.Z);
}

/// <summary>Presentation boundary for Animator/audio/effects; safe when all are absent.</summary>
internal sealed class BossPresentationGateway
{
    private readonly Transform actor;
    private readonly BossConfig config;
    private readonly Animator animator;
    private readonly AudioSource audioSource;
    private readonly Transform visualRoot;
    private readonly Transform vfxRoot;
    private readonly NetworkAnimator networkAnimator;
    private Renderer[] renderers = Array.Empty<Renderer>();
    private GameObject contractVfxObject;
    private LineRenderer contractVfxLine;
    private GameObject phaseRainObject;

    public Animator Animator => animator;

    public BossPresentationGateway(Transform actorTransform, BossConfig bossConfig, Animator targetAnimator,
        AudioSource targetAudioSource, Transform targetVisualRoot, Transform targetVfxRoot)
    {
        actor = actorTransform;
        config = bossConfig;
        animator = targetAnimator;
        audioSource = targetAudioSource;
        visualRoot = targetVisualRoot;
        vfxRoot = targetVfxRoot;
        networkAnimator = actor != null ? actor.GetComponent<NetworkAnimator>() : null;
    }

    public void Initialize() => RefreshRenderers();
    public void RefreshRenderers() => renderers = actor != null ? actor.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
    public void SetRenderersEnabled(bool value)
    {
        foreach (Renderer renderer in renderers) if (renderer != null) renderer.enabled = value;
    }
    public void PlayOneShot(AudioClip clip) { if (audioSource != null && clip != null) audioSource.PlayOneShot(clip); }

    public bool TrySetTrigger(string triggerName)
    {
        if (!HasParameter(triggerName, AnimatorControllerParameterType.Trigger)) return false;
        if (NetworkAuthority.IsNetworkActive && networkAnimator != null && NetworkAuthority.IsServerOrOffline() && networkAnimator.IsSpawned)
            networkAnimator.SetTrigger(triggerName);
        else animator.SetTrigger(triggerName);
        return true;
    }

    public bool TrySetInteger(string parameterName, int value)
    {
        if (!HasParameter(parameterName, AnimatorControllerParameterType.Int)) return false;
        animator.SetInteger(parameterName, value);
        return true;
    }

    public bool HasParameter(string parameterName, AnimatorControllerParameterType expectedType)
    {
        if (animator == null || animator.runtimeAnimatorController == null || string.IsNullOrWhiteSpace(parameterName)) return false;
        foreach (AnimatorControllerParameter parameter in animator.parameters)
            if (parameter.name == parameterName && parameter.type == expectedType) return true;
        return false;
    }

    public void CreateContractVfx()
    {
        if (contractVfxObject != null || config == null || actor == null || vfxRoot == null) return;
        contractVfxObject = new GameObject("Boss_Format5_ContractSiphon");
        contractVfxObject.transform.SetParent(vfxRoot, false);
        contractVfxLine = contractVfxObject.AddComponent<LineRenderer>();
        contractVfxLine.useWorldSpace = true;
        contractVfxLine.positionCount = 2;
        contractVfxLine.startWidth = config.telegraphLineWidth * 1.4f;
        contractVfxLine.endWidth = config.telegraphLineWidth * 0.55f;
        contractVfxLine.startColor = config.projectileColor;
        contractVfxLine.endColor = config.warningColor;
        contractVfxLine.sortingOrder = 102;
        if (config.telegraphMaterial != null) contractVfxLine.sharedMaterial = config.telegraphMaterial;
    }

    public void UpdateContractVfx(Transform target)
    {
        if (contractVfxLine == null || target == null || actor == null || config == null) return;
        float y = config.GetEffectHeight();
        Vector3 targetPoint = target.position;
        Vector3 bossPoint = actor.position;
        targetPoint.y = Mathf.Max(targetPoint.y, y);
        bossPoint.y = Mathf.Max(bossPoint.y, y);
        contractVfxLine.SetPosition(0, targetPoint);
        contractVfxLine.SetPosition(1, bossPoint);
    }

    public void DestroyContractVfx()
    {
        if (contractVfxObject != null) UnityEngine.Object.Destroy(contractVfxObject);
        contractVfxObject = null;
        contractVfxLine = null;
    }

    public void StartPhaseThreeRain()
    {
        if (config == null || !config.createPhase3Rain || phaseRainObject != null || actor == null) return;
        phaseRainObject = new GameObject("Boss_Phase3_BloodRain");
        phaseRainObject.transform.SetParent(vfxRoot != null ? vfxRoot : actor, true);
        phaseRainObject.AddComponent<BossPhaseRain>().Initialize(config);
    }

    public void Dispose()
    {
        SetRenderersEnabled(true);
        DestroyContractVfx();
        if (phaseRainObject != null) UnityEngine.Object.Destroy(phaseRainObject);
        phaseRainObject = null;
    }
}

/// <summary>Scene/physics target discovery adapter. It does not depend on Player types.</summary>
internal static class BossTargetResolver
{
    public static Transform Resolve(Transform current, BossConfig config)
    {
        if (IsUsable(current))
        {
            BossCombatTarget.EnsurePlayerAdapter(current, false);
            return current;
        }

        GameObject candidate = FindTaggedPlayer();
        if (candidate == null && config != null && config.playerLayer.value != 0)
        {
            foreach (Collider collider in UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsInactive.Exclude))
            {
                if (collider != null && (config.playerLayer.value & (1 << collider.gameObject.layer)) != 0)
                {
                    candidate = collider.transform.root.gameObject;
                    break;
                }
            }
        }
        Transform result = candidate != null ? candidate.transform : null;
        if (result != null) BossCombatTarget.EnsurePlayerAdapter(result, true);
        return result;
    }

    public static bool IsUsable(Transform target) => target != null && target.gameObject.activeInHierarchy;

    private static GameObject FindTaggedPlayer()
    {
        try { return GameObject.FindGameObjectWithTag("Player"); }
        catch (UnityException) { return null; }
    }
}
