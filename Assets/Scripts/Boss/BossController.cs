using System;
using System.Collections;
using UnityEngine;
using VampireHunt.Boss.Application;
using VampireHunt.Boss.Contracts;
using VampireHunt.Combat.Application;
using VampireHunt.Combat.Contracts;
using VampireHunt.Combat.Domain;
using VampireHunt.Core;
using EntityId = VampireHunt.Core.EntityId;
using EntityIdAllocator = VampireHunt.Core.EntityIdAllocator;
using RuntimeBossSnapshot = VampireHunt.Boss.Contracts.BossSnapshot;
using RuntimeEncounterMode = VampireHunt.Boss.Contracts.EncounterMode;

/// <summary>
/// Unity/NGO compatibility adapter for one authoritative BossRuntime.
///
/// The component keeps legacy serialized references and UnityEvent/AnimationEvent
/// entry points, but it does not decide phases, attacks, windows, mitigation or
/// death. Those responsibilities are delegated to Boss Application/Domain.
/// </summary>
[DefaultExecutionOrder(-50)]
[RequireComponent(typeof(BossConfig))]
[RequireComponent(typeof(BossHealth))]
[RequireComponent(typeof(BossAttackController))]
[RequireComponent(typeof(Rigidbody))]
[DisallowMultipleComponent]
public sealed class BossController : MonoBehaviour, IBossController, IGameplayEventSink,
    IBossRuntimeBinding, IBossRuntimeDependencyProvider
{
    // Existing field names are intentionally preserved for prefab/scene data.
    [SerializeField] private BossConfig stats;
    [SerializeField] private BossHealth bossHealth;
    [SerializeField] private BossAttackController attackController;
    [SerializeField] private Rigidbody bossRigidbody;
    [SerializeField] private Collider bossCollider;
    [SerializeField] private Animator animator;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private Transform player;
    [SerializeField] private Transform visualRoot;
    [SerializeField] private Transform vfxRoot;
    [SerializeField] private BossGuard leftGuard;
    [SerializeField] private BossGuard rightGuard;

    private static readonly EntityIdAllocator EntityIds = new(1000UL);
    private static readonly EntityIdAllocator SourceIds = new(1000000UL);

    private IBossRuntimePort runtime;
    private BossUnityClock clock;
    private BossCombatEntityDirectory directory;
    private BossMovementMotor movement;
    private BossPresentationGateway presentation;
    private CombatApplicationService combat;
    private RuntimeBossSnapshot lastRuntimeSnapshot;
    private EntityId entityId;
    private bool runtimeReady;
    private bool combatEnabled;
    private bool combatCommandReceived;
    private bool eventsBound;
    private bool runtimeEventsBound;
    private bool runtimeOwnedByBootstrap;
    private bool defeatedRaised;
    private bool legacyFallbackWarningLogged;
    private bool contractWasActive;
    private bool contractExpiredRaised;
    private float nextTargetResolveTime;
    private BossEncounterMode encounterMode;
    private BossStaggerState staggerState;

    public BossState CurrentState { get; private set; } = BossState.Dormant;
    public float RemainingContractSeconds { get; private set; }
    public bool ContractCountdownActive { get; private set; }
    public bool CombatEnabled => combatEnabled;
    public Transform Player => player;
    public BossHealth Health => bossHealth;
    public BossAttackController Attacks => attackController;
    public Transform ActorTransform => transform;
    public Transform Target => player;
    public BossState State => CurrentState;
    public global::BossSnapshot Snapshot => CreateSnapshot();
    public BossEncounterMode EncounterMode => encounterMode;
    public BossStaggerState StaggerState => staggerState;
    public EntityId RuntimeEntityId => entityId;
    private bool IsDead => bossHealth != null
        ? bossHealth.IsDead
        : runtimeReady && !runtime.Snapshot.IsAlive;

    public event Action<float> ContractCountdownChanged;
    public event Action ContractCountdownExpired;
    public event Action<IBossController, BossState, BossState> StateChanged;
    public event Action<IBossController, Transform> TargetChanged;
    public event Action<IBossController, bool> CombatEnabledChanged;
    public event Action<IBossController, global::BossSnapshot> SnapshotChanged;
    public event Action<IBossController, BossAttackType> AttackStarted;
    public event Action<IBossController, BossAttackType> AttackCompleted;
    public event Action<IBossController, BossAttackType> AttackCancelled;
    public event Action<IBossController, BossEncounterMode, BossEncounterMode> EncounterModeChanged;
    public event Action<IBossController, BossStaggerState, BossStaggerState> StaggerStateChanged;
    public event Action<IBossController, Transform, int> StaggerExecuted;
    public event Action<IBossController> Defeated;

    private void Awake()
    {
        ResolveComponentReferences();
        FindAndConfigureGuards();
        attackController?.ConfigureGuards(leftGuard, rightGuard);
        encounterMode = stats != null ? stats.initialEncounterMode : BossEncounterMode.Hunt;
        movement = new BossMovementMotor(transform, bossRigidbody, bossCollider, stats);
        presentation = new BossPresentationGateway(transform, stats, animator, audioSource, visualRoot, vfxRoot);
        presentation.Initialize();
        entityId = EntityIds.Allocate();
        clock = new BossUnityClock();
    }

    private void OnEnable()
    {
        BindComponentEvents();
        BossRegistry.Register(this);
        if (runtime != null && !runtimeEventsBound)
        {
            runtime.GameplayEventProduced += Publish;
            runtimeEventsBound = true;
        }
        if (runtimeReady) SyncRuntimeSnapshot();
    }

    private IEnumerator Start()
    {
        if (!NetworkAuthority.IsServerOrOffline()) yield break;
        // Bootstrap may inject the application endpoint before Start. The
        // compatibility fallback keeps an uncomposed legacy scene playable,
        // but the shell never owns domain state or rules itself.
        if (!runtimeReady) BuildRuntime();
        if (!runtimeReady) yield break;
        ResolveTargetNow();
        if (stats != null && stats.randomSpawnOnStart) movement?.TryTeleportToArena(player);
        if (stats != null && stats.initialActionDelay > 0f)
            yield return new WaitForSeconds(stats.initialActionDelay);
        if (!combatCommandReceived && !IsDead)
            StartCombat();
    }

    private void Update()
    {
        if (!NetworkAuthority.IsServerOrOffline()) return;
        ResolveTargetWhenNeeded();
        if (!runtimeReady || !combatEnabled || runtime.Snapshot.Mode == RuntimeEncounterMode.Defeated) return;
        if (!runtimeOwnedByBootstrap) runtime.Tick(Time.deltaTime);
        SyncRuntimeSnapshot();
        if (ContractCountdownActive) presentation?.UpdateContractVfx(player);
    }

    public BossCommandResult AssignTarget(Transform target)
    {
        if (!Application.isPlaying) return BossCommandResult.NotPlaying;
        if (!NetworkAuthority.IsServerOrOffline()) return BossCommandResult.NotAuthority;
        if (!BossTargetResolver.IsUsable(target)) return BossCommandResult.InvalidArgument;
        SetTargetInternal(target);
        BossCombatTarget.EnsurePlayerAdapter(target, true);
        return BossCommandResult.Succeeded;
    }

    public BossCommandResult ClearTarget()
    {
        if (!Application.isPlaying) return BossCommandResult.NotPlaying;
        if (!NetworkAuthority.IsServerOrOffline()) return BossCommandResult.NotAuthority;
        SetTargetInternal(null);
        return BossCommandResult.Succeeded;
    }

    /// <summary>
    /// Binds the application endpoint supplied by Bootstrap. Only the
    /// Contracts facade crosses the Unity/assembly boundary; no aggregate is
    /// exposed to this compatibility component.
    /// </summary>
    public bool TryBind(IBossRuntimePort next)
    {
        if (next == null || runtimeReady) return false;
        runtime = next;
        entityId = next.BossId;
        runtime.GameplayEventProduced += Publish;
        runtimeEventsBound = true;
        runtimeOwnedByBootstrap = true;
        runtimeReady = true;
        lastRuntimeSnapshot = next.Snapshot;
        SyncRuntimeSnapshot();
        return true;
    }

    /// <summary>
    /// Supplies scene-specific Application ports to Bootstrap. This method
    /// creates adapters only; it never creates a Boss aggregate or runtime.
    /// </summary>
    public bool TryCreateRuntimeDependencies(out BossRuntimeDependencies dependencies)
    {
        dependencies = default;
        ResolveComponentReferences();
        if (stats == null || !entityId.IsValid) return false;
        movement ??= new BossMovementMotor(transform, bossRigidbody, bossCollider, stats);
        clock ??= new BossUnityClock();
        directory ??= new BossCombatEntityDirectory();

        BossAttackWorldQueryAdapter worldQuery = new(directory, stats.playerLayer);
        BossWorldStateAdapter worldState = new(transform, () => player, stats);
        BossProjectileSpawnerAdapter projectiles = new(stats, transform, HandleProjectileHit);
        dependencies = new BossRuntimeDependencies(
            entityId,
            directory,
            movement,
            worldQuery,
            projectiles,
            worldState,
            runtimeToRegister => directory.Bind(runtimeToRegister.BossId, runtimeToRegister.DamageReceiver));
        return true;
    }

    public BossCommandResult StartCombat()
    {
        if (!Application.isPlaying) return BossCommandResult.NotPlaying;
        if (!NetworkAuthority.IsServerOrOffline()) return BossCommandResult.NotAuthority;
        if (!runtimeReady) return BossCommandResult.NotReady;
        if (IsDead) return BossCommandResult.Dead;
        combatCommandReceived = true;
        runtime.Start(ToDomainMode(encounterMode));
        SetCombatEnabled(true);
        SyncRuntimeSnapshot();
        return BossCommandResult.Succeeded;
    }

    public BossCommandResult StopCombat()
    {
        if (!Application.isPlaying) return BossCommandResult.NotPlaying;
        if (!NetworkAuthority.IsServerOrOffline()) return BossCommandResult.NotAuthority;
        if (!runtimeReady) return BossCommandResult.NotReady;
        if (IsDead) return BossCommandResult.Dead;
        combatCommandReceived = true;
        runtime.CancelAttack();
        runtime.SetEncounterMode(RuntimeEncounterMode.Inactive);
        SetCombatEnabled(false);
        TransitionTo(BossState.Dormant);
        SyncRuntimeSnapshot();
        return BossCommandResult.Succeeded;
    }

    public BossCommandResult ApplyDamage(int amount, Vector3 damageSource)
    {
        if (!Application.isPlaying) return BossCommandResult.NotPlaying;
        if (!NetworkAuthority.IsServerOrOffline()) return BossCommandResult.NotAuthority;
        return ApplyAuthoritativeDamage(amount, damageSource, SourceIds.Allocate());
    }

    public BossCommandResult SetInvulnerable(bool value)
    {
        if (!Application.isPlaying) return BossCommandResult.NotPlaying;
        if (!NetworkAuthority.IsServerOrOffline()) return BossCommandResult.NotAuthority;
        return SetInvulnerableAuthoritative(value);
    }

    public BossCommandResult TryForceAttack(BossAttackType attackType)
    {
        if (!Application.isPlaying) return BossCommandResult.NotPlaying;
        if (!NetworkAuthority.IsServerOrOffline()) return BossCommandResult.NotAuthority;
        return TryForceAuthoritativeAttack(attackType, false)
            ? BossCommandResult.Succeeded
            : (IsDead ? BossCommandResult.Dead : BossCommandResult.Rejected);
    }

    public BossCommandResult SetEncounterMode(BossEncounterMode mode)
    {
        if (!Application.isPlaying) return BossCommandResult.NotPlaying;
        if (!NetworkAuthority.IsServerOrOffline()) return BossCommandResult.NotAuthority;
        if (!Enum.IsDefined(typeof(BossEncounterMode), mode)) return BossCommandResult.InvalidArgument;
        if (!runtimeReady) return BossCommandResult.NotReady;
        if (IsDead) return BossCommandResult.Dead;
        if (encounterMode == mode) return BossCommandResult.Succeeded;

        BossEncounterMode previous = encounterMode;
        encounterMode = mode;
        runtime.SetEncounterMode(ToDomainMode(mode));
        if (!combatEnabled) runtime.Start(ToDomainMode(mode));
        EncounterModeChanged?.Invoke(this, previous, mode);
        SyncRuntimeSnapshot();
        return BossCommandResult.Succeeded;
    }

    public BossCommandResult RequestStagger()
    {
        if (!Application.isPlaying) return BossCommandResult.NotPlaying;
        if (!NetworkAuthority.IsServerOrOffline()) return BossCommandResult.NotAuthority;
        if (!runtimeReady) return BossCommandResult.NotReady;
        if (IsDead) return BossCommandResult.Dead;
        float seconds = stats != null ? stats.staggerWindowDuration : 4f;
        if (!runtime.BeginStagger(seconds)) return BossCommandResult.Rejected;
        SetStaggerState(BossStaggerState.Vulnerable);
        TransitionTo(BossState.Stagger);
        SyncRuntimeSnapshot();
        return BossCommandResult.Succeeded;
    }

    public BossCommandResult ExecuteStagger(int damage, Transform executor)
    {
        if (!Application.isPlaying) return BossCommandResult.NotPlaying;
        if (!NetworkAuthority.IsServerOrOffline()) return BossCommandResult.NotAuthority;
        if (damage < 0) return BossCommandResult.InvalidArgument;
        if (!runtimeReady) return BossCommandResult.NotReady;
        if (IsDead) return BossCommandResult.Dead;
        if (!runtime.ExecuteStagger()) return BossCommandResult.Rejected;

        SetStaggerState(BossStaggerState.Executed);
        StaggerExecuted?.Invoke(this, executor, damage);
        if (damage > 0)
        {
            Vector3 source = executor != null ? executor.position : transform.position;
            ApplyAuthoritativeDamage(damage, source, SourceIds.Allocate());
        }
        SyncRuntimeSnapshot();
        return BossCommandResult.Succeeded;
    }

    public BossCommandResult TeleportToArena()
    {
        if (!Application.isPlaying) return BossCommandResult.NotPlaying;
        if (!NetworkAuthority.IsServerOrOffline()) return BossCommandResult.NotAuthority;
        if (!runtimeReady || IsDead) return runtimeReady ? BossCommandResult.Dead : BossCommandResult.NotReady;
        return movement != null && movement.TryTeleportToArena(player)
            ? BossCommandResult.Succeeded
            : BossCommandResult.Rejected;
    }

    internal bool TryStartAuthoritativeAttack()
    {
        if (!runtimeReady || !combatEnabled || IsDead) return false;
        bool started = runtime.TryStartAttack();
        SyncRuntimeSnapshot();
        return started;
    }

    internal bool TryForceAuthoritativeAttack(BossAttackType attackType, bool interruptCurrentAttack)
    {
        if (!runtimeReady || !combatEnabled || IsDead) return false;
        BossAttackId id = BossAttackController.ToDomain(attackType);
        if (id == BossAttackId.None) return false;
        if (interruptCurrentAttack) runtime.CancelAttack();
        bool started = runtime.TryStartAttack(id);
        SyncRuntimeSnapshot();
        return started;
    }

    internal void CancelAuthoritativeAttack()
    {
        if (!runtimeReady) return;
        runtime.CancelAttack();
        SyncRuntimeSnapshot();
    }

    internal bool ApplyDamageFromHealthShell(int amount, Vector3 source) =>
        ApplyAuthoritativeDamage(amount, source, SourceIds.Allocate()) == BossCommandResult.Succeeded;

    internal BossCommandResult ApplyDamageFromGuardShell(int amount, Vector3 source) =>
        ApplyAuthoritativeDamage(amount, source, SourceIds.Allocate());

    internal void CompletePhaseChangeFromHealthShell() =>
        runtime?.SetInvulnerable(false);

    internal void SetInvulnerableFromHealthShell(bool value) => SetInvulnerableAuthoritative(value);

    internal bool RestoreGuardFromShell()
    {
        if (runtime == null || !NetworkAuthority.IsServerOrOffline()) return false;
        bool restored = runtime.RestoreGuard();
        SyncRuntimeSnapshot();
        return restored;
    }

    internal bool TryGetGuardProjection(out int current, out int maximum)
    {
        if (runtime == null)
        {
            current = 0;
            maximum = 0;
            return false;
        }
        current = runtime.GuardIntegrity;
        maximum = runtime.MaxGuardIntegrity;
        return maximum > 0;
    }

    public void Publish(IGameplayEvent @event)
    {
        if (@event is BossAttackCueEvent attackCue)
        {
            switch (attackCue.Phase)
            {
                case BossAttackCuePhase.Started:
                    attackController?.NotifyStarted(attackCue.AttackId);
                    if (TryMap(attackCue.AttackId, out BossAttackType started)) AttackStarted?.Invoke(this, started);
                    TransitionTo(BossState.Attack);
                    break;
                case BossAttackCuePhase.Completed:
                    attackController?.NotifyCompleted(attackCue.AttackId);
                    if (TryMap(attackCue.AttackId, out BossAttackType completed)) AttackCompleted?.Invoke(this, completed);
                    if (combatEnabled) TransitionTo(RecoveryState());
                    break;
                case BossAttackCuePhase.Cancelled:
                    attackController?.NotifyCancelled(attackCue.AttackId);
                    if (TryMap(attackCue.AttackId, out BossAttackType cancelled)) AttackCancelled?.Invoke(this, cancelled);
                    if (combatEnabled) TransitionTo(RecoveryState());
                    break;
            }
        }
        else if (@event is BossPhaseChangedEvent phase)
        {
            TransitionTo(BossState.PhaseChange);
            presentation?.TrySetInteger(stats != null ? stats.phaseParameter : "Phase", (int)phase.Current);
        }
        else if (@event is BossDefeatedEvent)
        {
            if (!defeatedRaised)
            {
                defeatedRaised = true;
                combatEnabled = false;
                TransitionTo(BossState.Dead);
                Defeated?.Invoke(this);
            }
        }

        SyncRuntimeSnapshot();
    }

    private BossCommandResult ApplyAuthoritativeDamage(int amount, Vector3 source, EntityId sourceId)
    {
        if (!runtimeReady) return BossCommandResult.NotReady;
        if (amount <= 0) return BossCommandResult.InvalidArgument;
        RuntimeBossSnapshot snapshot = runtime.Snapshot;
        if (!snapshot.IsAlive) return BossCommandResult.Dead;
        if (snapshot.IsInvulnerable) return BossCommandResult.Invulnerable;

        WorldPosition hitPosition = new(source.x, source.y, source.z);
        DamageRequest request = new(sourceId, runtime.BossId, amount, DamageFlags.NoCritical,
            new HitContext(hitPosition, new DamageTag("boss")));
        DamageResult result = runtime.ApplyDamage(in request);
        SyncRuntimeSnapshot();
        if (result.WasKilled) return BossCommandResult.Succeeded;
        return result.AppliedDamage > 0 ? BossCommandResult.Succeeded : BossCommandResult.Rejected;
    }

    private BossCommandResult SetInvulnerableAuthoritative(bool value)
    {
        if (!runtimeReady) return BossCommandResult.NotReady;
        if (runtime.Snapshot.Mode == RuntimeEncounterMode.Defeated) return BossCommandResult.Dead;
        runtime.SetInvulnerable(value);
        SyncRuntimeSnapshot();
        return BossCommandResult.Succeeded;
    }

    private void BuildRuntime()
    {
        if (!TryCreateRuntimeDependencies(out BossRuntimeDependencies dependencies)) return;
        if (!legacyFallbackWarningLogged)
        {
            legacyFallbackWarningLogged = true;
            Debug.LogWarning(
                "[Boss] Bootstrap did not bind a runtime before Start; using the legacy fallback composition bridge.",
                this);
        }
        try
        {
            BossUnityRandom random = new();
            combat = new CombatApplicationService(
                new CombatResolver(random), dependencies.CombatEntities, this, clock);
            BossRuntime created = BossRuntimeFactory.Create(
                stats.BuildSpec(),
                random,
                combat,
                dependencies,
                null,
                clock);
            dependencies.Register(created);
            if (!TryBind(created))
                throw new InvalidOperationException("Boss runtime was already bound during fallback composition.");
            runtimeOwnedByBootstrap = false;
        }
        catch (Exception exception)
        {
            runtimeReady = false;
            Debug.LogError($"[Boss] Runtime composition failed: {exception.Message}", this);
        }
    }

    private void HandleProjectileHit(EntityId bossId, ICombatTarget target, int damage, WorldPosition position, float knockback)
    {
        if (!runtimeReady || target == null || !target.IsAlive || damage <= 0) return;
        directory.Bind(target);
        DamageRequest request = new(bossId, target.Id, damage, DamageFlags.NoCritical,
            new HitContext(position, new DamageTag("boss-projectile")));
        runtime.ApplyDamage(in request);
        if (knockback > 0f)
        {
            KnockbackRequest knockbackRequest = new(
                bossId, target.Id, target.Position - position, knockback, 0.18f);
            runtime.ApplyKnockback(in knockbackRequest);
        }
    }

    private void SyncRuntimeSnapshot()
    {
        if (!runtimeReady) return;
        RuntimeBossSnapshot snapshot = runtime.Snapshot;
        lastRuntimeSnapshot = snapshot;
        bossHealth?.ApplySnapshot(snapshot);
        attackController?.SyncSnapshot(snapshot);
        leftGuard?.SyncFromController(false);
        rightGuard?.SyncFromController(false);

        float previousContract = RemainingContractSeconds;
        RemainingContractSeconds = snapshot.ContractSeconds;
        ContractCountdownActive = snapshot.HealthRatio <= (stats != null ? stats.format5TriggerHealthRate : 0.2f) &&
                                  snapshot.ContractSeconds > 0f;
        if (ContractCountdownActive && !contractWasActive)
        {
            contractWasActive = true;
            presentation?.CreateContractVfx();
        }
        if (Mathf.Abs(previousContract - RemainingContractSeconds) > 0.0001f)
            ContractCountdownChanged?.Invoke(RemainingContractSeconds);
        if (contractWasActive && !contractExpiredRaised && RemainingContractSeconds <= 0f)
        {
            contractExpiredRaised = true;
            ContractCountdownActive = false;
            presentation?.DestroyContractVfx();
            ContractCountdownExpired?.Invoke();
        }

        staggerState = ToLegacyStagger(snapshot.Stagger);
        if (!snapshot.IsAlive && CurrentState != BossState.Dead && runtime != null)
            TransitionTo(BossState.Dead);
        SnapshotChanged?.Invoke(this, CreateSnapshot());
    }

    private void ResolveTargetWhenNeeded()
    {
        if (BossTargetResolver.IsUsable(player) || Time.unscaledTime < nextTargetResolveTime) return;
        nextTargetResolveTime = Time.unscaledTime + 0.5f;
        ResolveTargetNow();
    }

    private void ResolveTargetNow() => SetTargetInternal(BossTargetResolver.Resolve(player, stats));

    private void SetTargetInternal(Transform newTarget)
    {
        if (player == newTarget)
        {
            attackController?.SetPlayer(newTarget);
            return;
        }
        player = newTarget;
        attackController?.SetPlayer(player);
        TargetChanged?.Invoke(this, player);
        if (player != null) BossCombatTarget.EnsurePlayerAdapter(player, false);
        PublishSnapshot();
    }

    private void SetCombatEnabled(bool value)
    {
        if (combatEnabled == value) return;
        combatEnabled = value;
        CombatEnabledChanged?.Invoke(this, value);
        if (value && ContractCountdownActive) presentation?.CreateContractVfx();
        PublishSnapshot();
    }

    private void SetStaggerState(BossStaggerState next)
    {
        if (staggerState == next) return;
        BossStaggerState previous = staggerState;
        staggerState = next;
        StaggerStateChanged?.Invoke(this, previous, next);
        PublishSnapshot();
    }

    private BossState RecoveryState() => encounterMode == BossEncounterMode.Hunt
        ? BossState.OffscreenIdle
        : BossState.BattleIdle;

    private void TransitionTo(BossState next)
    {
        if (CurrentState == next) return;
        BossState previous = CurrentState;
        CurrentState = next;
        StateChanged?.Invoke(this, previous, next);
    }

    private global::BossSnapshot CreateSnapshot()
    {
        RuntimeBossSnapshot snapshot = runtimeReady ? runtime.Snapshot : lastRuntimeSnapshot;
        return new global::BossSnapshot(
            entityId,
            CurrentState,
            combatEnabled,
            snapshot.Health,
            snapshot.MaxHealth,
            (int)snapshot.Phase,
            snapshot.IsInvulnerable,
            !snapshot.IsAlive,
            snapshot.ContractSeconds,
            transform.position,
            TryMap(snapshot.CurrentAttack, out BossAttackType attack) ? attack : (BossAttackType?)null,
            encounterMode,
            staggerState);
    }

    private void PublishSnapshot() => SnapshotChanged?.Invoke(this, CreateSnapshot());

    private void BindComponentEvents()
    {
        if (eventsBound) return;
        if (bossHealth != null) bossHealth.Died += HandleHealthDied;
        if (attackController != null)
        {
            attackController.AttackStarted += HandleLegacyAttackStarted;
            attackController.AttackCompleted += HandleLegacyAttackCompleted;
            attackController.AttackCancelled += HandleLegacyAttackCancelled;
        }
        eventsBound = true;
    }

    private void UnbindComponentEvents()
    {
        if (!eventsBound) return;
        if (bossHealth != null) bossHealth.Died -= HandleHealthDied;
        if (attackController != null)
        {
            attackController.AttackStarted -= HandleLegacyAttackStarted;
            attackController.AttackCompleted -= HandleLegacyAttackCompleted;
            attackController.AttackCancelled -= HandleLegacyAttackCancelled;
        }
        eventsBound = false;
    }

    private void HandleHealthDied()
    {
        if (CurrentState != BossState.Dead) TransitionTo(BossState.Dead);
    }

    private void HandleLegacyAttackStarted(BossAttackType attack) { }
    private void HandleLegacyAttackCompleted(BossAttackType attack) { }
    private void HandleLegacyAttackCancelled(BossAttackType attack) { }

    private void ResolveComponentReferences()
    {
        stats ??= GetComponent<BossConfig>();
        bossHealth ??= GetComponent<BossHealth>();
        attackController ??= GetComponent<BossAttackController>();
        bossRigidbody ??= GetComponent<Rigidbody>();
        bossCollider ??= GetComponent<Collider>();
        animator ??= GetComponentInChildren<Animator>(true);
        audioSource ??= GetComponent<AudioSource>();
        visualRoot ??= transform.Find("Visual");
        vfxRoot ??= transform.Find("VFXRoot");
    }

    private void FindAndConfigureGuards()
    {
        foreach (BossGuard guard in GetComponentsInChildren<BossGuard>(true))
        {
            if (guard == null) continue;
            if (guard.Side == BossGuardSide.Left) leftGuard ??= guard;
            else rightGuard ??= guard;
        }
        leftGuard?.Configure(BossGuardSide.Left, stats);
        rightGuard?.Configure(BossGuardSide.Right, stats);
    }

    [ContextMenu("Boss/验证场景绑定")]
    private void ValidateSceneBindings()
    {
        ResolveComponentReferences();
        FindAndConfigureGuards();
        attackController?.ConfigureGuards(leftGuard, rightGuard);
        if (animator == null) Debug.LogWarning("[Boss 配置] 未找到 Animator；Boss 逻辑仍可运行。", this);
    }

    private void OnGUI()
    {
        if (stats == null || !stats.showDebugPanel || !Application.isPlaying) return;
        float x = stats.debugPanelPosition.x;
        float y = stats.debugPanelPosition.y;
        GUI.Box(new Rect(x, y, 360f, 120f), "Boss 运行时调试");
        GUI.Label(new Rect(x + 12f, y + 26f, 336f, 20f), $"状态：{CurrentState}  战斗：{combatEnabled}");
        GUI.Label(new Rect(x + 12f, y + 48f, 336f, 20f), $"生命：{(bossHealth != null ? $"{bossHealth.CurrentHealth}/{bossHealth.MaxHealth}" : "缺失")}");
        GUI.Label(new Rect(x + 12f, y + 70f, 336f, 20f), $"阶段：{(bossHealth != null ? bossHealth.CurrentPhase : 0)}  攻击：{attackController?.LastAttackName ?? "无"}");
    }

    private void OnDisable()
    {
        if (NetworkAuthority.IsServerOrOffline()) runtime?.CancelAttack();
        if (runtimeEventsBound && runtime != null) runtime.GameplayEventProduced -= Publish;
        runtimeEventsBound = false;
        BossRegistry.Unregister(this);
        UnbindComponentEvents();
        presentation?.Dispose();
    }

    private void OnDestroy() => BossRegistry.Unregister(this);

    private void OnDrawGizmosSelected()
    {
        BossConfig config = stats != null ? stats : GetComponent<BossConfig>();
        if (config == null || !config.drawCombatGizmos) return;
        Gizmos.color = new Color(0.7f, 0f, 0f, 0.25f);
        Gizmos.DrawWireCube(config.arenaCenter,
            new Vector3(config.arenaHalfSize.x * 2f, 0.1f, config.arenaHalfSize.y * 2f));
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, config.stoppingDistance);
    }

    private static RuntimeEncounterMode ToDomainMode(BossEncounterMode mode) => mode == BossEncounterMode.Battle
        ? RuntimeEncounterMode.Battle
        : RuntimeEncounterMode.Hunt;

    private static BossStaggerState ToLegacyStagger(VampireHunt.Boss.Contracts.StaggerState state) => state switch
    {
        VampireHunt.Boss.Contracts.StaggerState.Telegraph => BossStaggerState.Telegraph,
        VampireHunt.Boss.Contracts.StaggerState.Vulnerable => BossStaggerState.Vulnerable,
        VampireHunt.Boss.Contracts.StaggerState.Executed => BossStaggerState.Executed,
        _ => BossStaggerState.None
    };

    private static bool TryMap(BossAttackId attackId, out BossAttackType attackType)
    {
        attackType = attackId switch
        {
            BossAttackId.GuardSweep => BossAttackType.Format1,
            BossAttackId.RotatingBarrage => BossAttackType.Format2,
            BossAttackId.CrossSlash => BossAttackType.Format3,
            BossAttackId.ChargedSlash => BossAttackType.Format4,
            BossAttackId.RectangleDash => BossAttackType.Format6,
            _ => default
        };
        return attackId != BossAttackId.None;
    }
}
