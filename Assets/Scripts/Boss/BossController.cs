using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Application-layer coordinator and public facade for the Boss encounter.
///
/// Responsibilities are deliberately narrow: it coordinates health, attacks,
/// phase flow and the public API. Physics/navigation and presentation details
/// live behind BossMovementMotor and BossPresentationGateway.
/// </summary>
[DefaultExecutionOrder(-50)]
[RequireComponent(typeof(BossConfig))]
[RequireComponent(typeof(BossHealth))]
[RequireComponent(typeof(BossAttackController))]
[RequireComponent(typeof(Rigidbody))]
[DisallowMultipleComponent]
public sealed class BossController : MonoBehaviour, IBossController
{
    // Field names are kept compatible with BloodlineHunter0.0.1 scene data.
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
    [SerializeField] private BossNetworkState networkState;

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
    public BossSnapshot Snapshot => CreateSnapshot();
    public BossEncounterMode EncounterMode => encounterMode;
    public BossStaggerState StaggerState => staggerState;

    public event Action<float> ContractCountdownChanged;
    public event Action ContractCountdownExpired;

    public event Action<IBossController, BossState, BossState> StateChanged;
    public event Action<IBossController, Transform> TargetChanged;
    public event Action<IBossController, bool> CombatEnabledChanged;
    public event Action<IBossController, BossSnapshot> SnapshotChanged;
    public event Action<IBossController, BossAttackType> AttackStarted;
    public event Action<IBossController, BossAttackType> AttackCompleted;
    public event Action<IBossController, BossAttackType> AttackCancelled;
    public event Action<IBossController, BossEncounterMode, BossEncounterMode> EncounterModeChanged;
    public event Action<IBossController, BossStaggerState, BossStaggerState> StaggerStateChanged;
    public event Action<IBossController, Transform, int> StaggerExecuted;
    public event Action<IBossController> Defeated;

    private BossMovementMotor movement;
    private BossBehaviorPolicy behaviorPolicy;
    private BossPresentationGateway presentation;
    private Camera viewCamera;
    private Coroutine phaseChangeCoroutine;
    private Coroutine staggerCoroutine;
    private BossEncounterMode encounterMode;
    private BossStaggerState staggerState;
    private bool staggerExecutionRequested;
    private Transform staggerExecutor;
    private int staggerExecutionDamage;
    private bool combatEnabled;
    private bool contractCountdownTriggered;
    private bool runtimeSetupValidated;
    private bool eventsBound;
    private bool combatCommandReceived;
    private float nextTargetResolveTime;
    private BossAttackType? replicatedActiveAttack;

    private void Awake()
    {
        ResolveComponentReferences();
        FindAndConfigureGuards();
        attackController?.ConfigureGuards(leftGuard, rightGuard);

        movement = new BossMovementMotor(
            transform,
            bossRigidbody,
            bossCollider,
            stats);
        behaviorPolicy = new BossBehaviorPolicy(stats);
        encounterMode = stats != null
            ? stats.initialEncounterMode
            : BossEncounterMode.Hunt;
        presentation = new BossPresentationGateway(
            transform,
            stats,
            animator,
            audioSource,
            visualRoot,
            vfxRoot);
        presentation.Initialize();
        viewCamera = Camera.main;
    }

    private void OnEnable()
    {
        BindComponentEvents();
        BossRegistry.Register(this);

        if (ContractCountdownActive)
        {
            presentation?.CreateContractVfx();
        }

        if (bossHealth != null && bossHealth.CurrentPhase >= 3 && !bossHealth.IsDead)
        {
            presentation?.StartPhaseThreeRain();
        }
    }

    private IEnumerator Start()
    {
        if (!NetworkAuthority.IsServerOrOffline())
        {
            if (bossRigidbody != null) bossRigidbody.isKinematic = true;
            yield break;
        }

        ResolveTargetNow();
        ValidateRuntimeSetup();

        if (stats.randomSpawnOnStart)
        {
            movement.TryTeleportToArena(player);
        }

        TransitionTo(BossState.Dormant);
        if (stats.initialActionDelay > 0f)
        {
            yield return new WaitForSeconds(stats.initialActionDelay);
        }

        if (!combatCommandReceived && !bossHealth.IsDead)
        {
            SetCombatEnabled(true);
        }
    }

    private void Update()
    {
        if (!NetworkAuthority.IsServerOrOffline()) return;
        ResolveTargetWhenNeeded();
        UpdateContractCountdown();

        if (ContractCountdownActive)
        {
            presentation.UpdateContractVfx(player);
        }
    }

    private void FixedUpdate()
    {
        if (!NetworkAuthority.IsServerOrOffline()) return;
        if (!combatEnabled ||
            player == null ||
            bossHealth == null ||
            bossHealth.IsDead ||
            CurrentState == BossState.PhaseChange ||
            staggerState != BossStaggerState.None)
        {
            movement.Stop();
            if (!combatEnabled && CurrentState != BossState.Dead)
            {
                TransitionTo(BossState.Dormant);
            }
            return;
        }

        Vector3 toPlayer = movement.GetPlanarOffset(player);
        float distance = toPlayer.magnitude;
        movement.Face(toPlayer, Time.fixedDeltaTime);

        if (attackController.IsBusy)
        {
            TransitionTo(BossState.Attack);
            movement.Stop();
            return;
        }

        viewCamera ??= Camera.main;
        bool bossVisible = movement.IsVisible(viewCamera);
        BossBehaviorDecision decision = behaviorPolicy.Evaluate(
            encounterMode,
            bossVisible,
            distance,
            toPlayer,
            bossHealth.CurrentPhase);

        if (decision.AllowAttack && attackController.TryStartAttack(
                bossHealth.CurrentPhase,
                bossVisible,
                distance))
        {
            TransitionTo(BossState.Attack);
            movement.Stop();
            return;
        }

        TransitionTo(decision.State);
        switch (decision.Movement)
        {
            case BossMovementIntent.Approach:
                movement.Move(
                    decision.Direction,
                    bossHealth.CurrentPhase,
                    Time.fixedDeltaTime);
                break;
            case BossMovementIntent.Retreat:
                movement.MoveAtSpeed(
                    decision.Direction,
                    ResolveHuntRetreatSpeed(),
                    Time.fixedDeltaTime);
                break;
            default:
                movement.Stop();
                break;
        }
    }

    public BossCommandResult AssignTarget(Transform target)
    {
        if (!Application.isPlaying)
        {
            return BossCommandResult.NotPlaying;
        }

        if (!NetworkAuthority.IsServerOrOffline()) return BossCommandResult.NotAuthority;

        if (!BossTargetResolver.IsUsable(target))
        {
            return BossCommandResult.InvalidArgument;
        }

        SetTargetInternal(target);
        BossCombatTarget.EnsurePlayerAdapter(target, true);
        return BossCommandResult.Succeeded;
    }

    public BossCommandResult ClearTarget()
    {
        if (!Application.isPlaying)
        {
            return BossCommandResult.NotPlaying;
        }

        if (!NetworkAuthority.IsServerOrOffline()) return BossCommandResult.NotAuthority;

        SetTargetInternal(null);
        movement?.Stop();
        return BossCommandResult.Succeeded;
    }

    public BossCommandResult StartCombat()
    {
        if (!Application.isPlaying)
        {
            return BossCommandResult.NotPlaying;
        }

        if (!NetworkAuthority.IsServerOrOffline()) return BossCommandResult.NotAuthority;

        if (bossHealth == null || attackController == null)
        {
            return BossCommandResult.NotReady;
        }

        if (bossHealth.IsDead)
        {
            return BossCommandResult.Dead;
        }

        combatCommandReceived = true;
        behaviorPolicy?.Reset();
        SetCombatEnabled(true);
        return BossCommandResult.Succeeded;
    }

    public BossCommandResult StopCombat()
    {
        if (!Application.isPlaying)
        {
            return BossCommandResult.NotPlaying;
        }

        if (!NetworkAuthority.IsServerOrOffline()) return BossCommandResult.NotAuthority;

        if (bossHealth != null && bossHealth.IsDead)
        {
            return BossCommandResult.Dead;
        }

        combatCommandReceived = true;
        behaviorPolicy?.Reset();
        SetCombatEnabled(false);
        attackController?.CancelCurrentAttack();
        CancelStagger();
        movement?.Stop();
        TransitionTo(BossState.Dormant);
        return BossCommandResult.Succeeded;
    }

    public BossCommandResult ApplyDamage(int amount, Vector3 damageSource)
    {
        if (!Application.isPlaying)
        {
            return BossCommandResult.NotPlaying;
        }

        if (!NetworkAuthority.IsServerOrOffline()) return BossCommandResult.NotAuthority;

        if (amount <= 0)
        {
            return BossCommandResult.InvalidArgument;
        }

        if (bossHealth == null)
        {
            return BossCommandResult.NotReady;
        }

        if (bossHealth.IsDead)
        {
            return BossCommandResult.Dead;
        }

        if (bossHealth.IsInvulnerable)
        {
            return BossCommandResult.Invulnerable;
        }

        return bossHealth.TakeDamage(amount, damageSource)
            ? BossCommandResult.Succeeded
            : BossCommandResult.Rejected;
    }

    public BossCommandResult SetInvulnerable(bool value)
    {
        if (!Application.isPlaying)
        {
            return BossCommandResult.NotPlaying;
        }

        if (!NetworkAuthority.IsServerOrOffline()) return BossCommandResult.NotAuthority;

        if (bossHealth == null)
        {
            return BossCommandResult.NotReady;
        }

        if (bossHealth.IsDead)
        {
            return BossCommandResult.Dead;
        }

        bossHealth.SetInvulnerable(value);
        PublishSnapshot();
        return BossCommandResult.Succeeded;
    }

    public BossCommandResult TryForceAttack(BossAttackType attackType)
    {
        if (!Application.isPlaying)
        {
            return BossCommandResult.NotPlaying;
        }

        if (!NetworkAuthority.IsServerOrOffline()) return BossCommandResult.NotAuthority;

        if (!IsSupportedAttack(attackType))
        {
            return BossCommandResult.InvalidArgument;
        }

        if (bossHealth == null || attackController == null)
        {
            return BossCommandResult.NotReady;
        }

        if (bossHealth.IsDead)
        {
            return BossCommandResult.Dead;
        }

        if (!combatEnabled)
        {
            return BossCommandResult.CombatDisabled;
        }

        if (player == null)
        {
            return BossCommandResult.TargetMissing;
        }

        if (CurrentState == BossState.PhaseChange ||
            staggerState != BossStaggerState.None ||
            attackController.IsBusy)
        {
            return BossCommandResult.Busy;
        }

        return attackController.TryForceAttack(attackType)
            ? BossCommandResult.Succeeded
            : BossCommandResult.Rejected;
    }

    public BossCommandResult SetEncounterMode(BossEncounterMode mode)
    {
        if (!Application.isPlaying) return BossCommandResult.NotPlaying;
        if (!NetworkAuthority.IsServerOrOffline()) return BossCommandResult.NotAuthority;
        if (!Enum.IsDefined(typeof(BossEncounterMode), mode))
        {
            return BossCommandResult.InvalidArgument;
        }
        if (bossHealth == null || attackController == null)
        {
            return BossCommandResult.NotReady;
        }
        if (bossHealth.IsDead) return BossCommandResult.Dead;
        if (CurrentState == BossState.PhaseChange ||
            staggerState != BossStaggerState.None)
        {
            return BossCommandResult.Busy;
        }
        if (encounterMode == mode) return BossCommandResult.Succeeded;

        BossEncounterMode previous = encounterMode;
        encounterMode = mode;
        behaviorPolicy?.Reset();
        attackController.CancelCurrentAttack();
        movement?.Stop();
        EncounterModeChanged?.Invoke(this, previous, encounterMode);
        TransitionTo(combatEnabled ? GetRecoveryState() : BossState.Dormant);
        PublishSnapshot();
        return BossCommandResult.Succeeded;
    }

    public BossCommandResult RequestStagger()
    {
        if (!Application.isPlaying) return BossCommandResult.NotPlaying;
        if (!NetworkAuthority.IsServerOrOffline()) return BossCommandResult.NotAuthority;
        if (bossHealth == null || attackController == null || stats == null)
        {
            return BossCommandResult.NotReady;
        }
        if (bossHealth.IsDead) return BossCommandResult.Dead;
        if (!combatEnabled) return BossCommandResult.CombatDisabled;
        if (player == null) return BossCommandResult.TargetMissing;
        if (CurrentState == BossState.PhaseChange ||
            attackController.IsBusy ||
            staggerState != BossStaggerState.None)
        {
            return BossCommandResult.Busy;
        }

        float distance = movement.GetPlanarOffset(player).magnitude;
        if (distance > stats.staggerActivationDistance)
        {
            return BossCommandResult.Rejected;
        }

        staggerCoroutine = StartCoroutine(StaggerRoutine());
        return BossCommandResult.Succeeded;
    }

    public BossCommandResult ExecuteStagger(int damage, Transform executor)
    {
        if (!Application.isPlaying) return BossCommandResult.NotPlaying;
        if (!NetworkAuthority.IsServerOrOffline()) return BossCommandResult.NotAuthority;
        if (damage < 0) return BossCommandResult.InvalidArgument;
        if (bossHealth == null) return BossCommandResult.NotReady;
        if (bossHealth.IsDead) return BossCommandResult.Dead;
        if (staggerState != BossStaggerState.Vulnerable)
        {
            return BossCommandResult.Rejected;
        }
        if (bossHealth.IsInvulnerable) return BossCommandResult.Invulnerable;

        staggerExecutionRequested = true;
        staggerExecutor = executor;
        staggerExecutionDamage = damage;
        SetStaggerState(BossStaggerState.Executed);
        StaggerExecuted?.Invoke(this, staggerExecutor, staggerExecutionDamage);

        if (damage > 0)
        {
            Vector3 source = executor != null
                ? executor.position
                : player != null ? player.position : transform.position;
            bossHealth.TakeDamage(damage, source);
        }

        PublishSnapshot();
        return BossCommandResult.Succeeded;
    }

    public BossCommandResult TeleportToArena()
    {
        if (!Application.isPlaying) return BossCommandResult.NotPlaying;
        if (!NetworkAuthority.IsServerOrOffline()) return BossCommandResult.NotAuthority;
        if (bossHealth == null || movement == null || attackController == null)
        {
            return BossCommandResult.NotReady;
        }
        if (bossHealth.IsDead) return BossCommandResult.Dead;
        if (CurrentState == BossState.PhaseChange ||
            staggerState != BossStaggerState.None ||
            attackController.IsBusy)
        {
            return BossCommandResult.Busy;
        }

        return movement.TryTeleportToArena(player)
            ? BossCommandResult.Succeeded
            : BossCommandResult.Rejected;
    }

    private IEnumerator StaggerRoutine()
    {
        staggerExecutionRequested = false;
        staggerExecutor = null;
        staggerExecutionDamage = 0;
        SetStaggerState(BossStaggerState.Telegraph);
        movement.Stop();

        if (stats.playPreStaggerAttack)
        {
            BossAttackType preStaggerAttack = SelectPreStaggerAttack();
            if (attackController.TryForceAttack(preStaggerAttack))
            {
                while (attackController.IsBusy &&
                       bossHealth != null &&
                       !bossHealth.IsDead &&
                       CurrentState != BossState.PhaseChange)
                {
                    yield return null;
                }
            }
        }

        if (bossHealth == null || bossHealth.IsDead ||
            CurrentState == BossState.PhaseChange || !combatEnabled)
        {
            staggerCoroutine = null;
            SetStaggerState(BossStaggerState.None);
            yield break;
        }

        TransitionTo(BossState.Stagger);
        SetStaggerState(BossStaggerState.Vulnerable);
        float remaining = stats.staggerWindowDuration;
        while (remaining > 0f && !staggerExecutionRequested)
        {
            remaining -= Time.deltaTime;
            yield return null;
        }

        bool executed = staggerExecutionRequested;
        staggerCoroutine = null;
        if (bossHealth != null &&
            !bossHealth.IsDead &&
            CurrentState != BossState.PhaseChange &&
            encounterMode == BossEncounterMode.Hunt &&
            ((executed && stats.teleportAfterStaggerExecution) ||
             (!executed && stats.teleportAfterStaggerTimeout)))
        {
            movement.TryTeleportToArena(player);
        }

        staggerExecutionRequested = false;
        staggerExecutor = null;
        staggerExecutionDamage = 0;
        SetStaggerState(BossStaggerState.None);
        if (bossHealth != null && !bossHealth.IsDead &&
            CurrentState != BossState.PhaseChange)
        {
            TransitionTo(combatEnabled ? GetRecoveryState() : BossState.Dormant);
        }
    }

    private void HandlePhaseChangeStarted(int newPhase)
    {
        if (bossHealth.IsDead)
        {
            return;
        }

        CancelStagger();

        if (stats.logCombatEvents)
        {
            Debug.Log(
                $"[Boss] 进入阶段 {newPhase}，当前生命 " +
                $"{bossHealth.CurrentHealth}/{bossHealth.MaxHealth}。",
                this);
        }

        if (phaseChangeCoroutine != null)
        {
            StopCoroutine(phaseChangeCoroutine);
        }

        PublishSnapshot();
        if (!NetworkAuthority.IsServerOrOffline())
        {
            phaseChangeCoroutine = StartCoroutine(ClientPhasePresentationRoutine(newPhase));
            return;
        }
        phaseChangeCoroutine = StartCoroutine(PhaseChangeRoutine(newPhase));
    }

    private IEnumerator ClientPhasePresentationRoutine(int newPhase)
    {
        presentation.PlayOneShot(stats.phaseChangeClip);
        presentation.TrySetTrigger(stats.phaseChangeTrigger);
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, stats.phaseChangeDuration));
        presentation.SetRenderersEnabled(true);
        presentation.TrySetInteger(stats.phaseParameter, newPhase);
        if (newPhase >= 3) presentation.StartPhaseThreeRain();
        phaseChangeCoroutine = null;
    }

    private IEnumerator PhaseChangeRoutine(int newPhase)
    {
        TransitionTo(BossState.PhaseChange);
        attackController.CancelCurrentAttack();
        movement.Stop();
        ApplyPhaseTransitionKnockback();

        presentation.PlayOneShot(stats.phaseChangeClip);
        presentation.TrySetTrigger(stats.phaseChangeTrigger);

        if (stats.clearObstaclesOnPhaseChange)
        {
            movement.ClearNearbyObstacles();
        }

        float elapsed = 0f;
        float nextBlinkTime = 0f;
        bool renderersEnabled = true;
        float blinkInterval = Mathf.Max(0.02f, stats.phaseBlinkInterval);

        while (elapsed < stats.phaseChangeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            if (elapsed >= nextBlinkTime)
            {
                renderersEnabled = !renderersEnabled;
                presentation.SetRenderersEnabled(renderersEnabled);
                nextBlinkTime = elapsed + blinkInterval;
            }

            yield return null;
        }

        presentation.SetRenderersEnabled(true);

        if (stats.teleportAfterPhaseChange)
        {
            movement.TryTeleportToArena(player);
        }

        presentation.TrySetInteger(stats.phaseParameter, newPhase);
        if (newPhase >= 2)
        {
            SpawnPhaseBloodPool();
        }
        if (newPhase >= 3)
        {
            presentation.StartPhaseThreeRain();
        }

        // Clear before completing: BossHealth may synchronously queue the next
        // phase when one large damage event crossed multiple thresholds.
        phaseChangeCoroutine = null;
        bossHealth.CompletePhaseChange();
        PublishSnapshot();
        if (phaseChangeCoroutine == null && !bossHealth.IsDead)
        {
            TransitionTo(combatEnabled ? GetRecoveryState() : BossState.Dormant);
        }
    }

    private void UpdateContractCountdown()
    {
        if (bossHealth == null || bossHealth.IsDead || stats == null)
        {
            return;
        }

        if (!contractCountdownTriggered &&
            stats.enableFormat5Countdown &&
            bossHealth.HealthNormalized <= stats.format5TriggerHealthRate)
        {
            contractCountdownTriggered = true;
            ContractCountdownActive = true;
            RemainingContractSeconds = stats.format5CountdownSeconds;
            presentation.TrySetTrigger(stats.format5Trigger);
            presentation.CreateContractVfx();
            ContractCountdownChanged?.Invoke(RemainingContractSeconds);
            PublishSnapshot();

            if (stats.logCombatEvents)
            {
                Debug.Log(
                    $"[Boss] 契约倒计时启动：{RemainingContractSeconds:0.0} 秒。",
                    this);
            }
        }

        if (!ContractCountdownActive)
        {
            return;
        }

        RemainingContractSeconds = Mathf.Max(
            0f,
            RemainingContractSeconds -
            Time.deltaTime * stats.format5CountdownRate);
        ContractCountdownChanged?.Invoke(RemainingContractSeconds);

        if (RemainingContractSeconds > 0f)
        {
            return;
        }

        ContractCountdownActive = false;
        presentation.DestroyContractVfx();
        ForceKillTarget();
        ContractCountdownExpired?.Invoke();
        PublishSnapshot();
    }

    private void HandleDeath()
    {
        CancelStagger();
        if (!NetworkAuthority.IsServerOrOffline())
        {
            presentation.SetRenderersEnabled(true);
            presentation.TrySetTrigger(stats.deathTrigger);
            presentation.PlayOneShot(stats.deathClip);
            if (bossCollider != null) bossCollider.enabled = false;
            return;
        }

        SetCombatEnabled(false);
        TransitionTo(BossState.Dead);

        if (stats.logCombatEvents)
        {
            Debug.Log("[Boss] 已死亡，停止移动与攻击。", this);
        }

        if (phaseChangeCoroutine != null)
        {
            StopCoroutine(phaseChangeCoroutine);
            phaseChangeCoroutine = null;
        }

        attackController.CancelCurrentAttack();
        movement.Stop();
        ContractCountdownActive = false;
        presentation.DestroyContractVfx();

        leftGuard?.DisableForBossDeath();
        rightGuard?.DisableForBossDeath();

        if (bossCollider != null)
        {
            bossCollider.enabled = false;
        }

        presentation.SetRenderersEnabled(true);
        presentation.TrySetTrigger(stats.deathTrigger);
        presentation.PlayOneShot(stats.deathClip);
        Defeated?.Invoke(this);
        PublishSnapshot();
        StartCoroutine(DisableAfterDeathRoutine());
    }

    private IEnumerator DisableAfterDeathRoutine()
    {
        if (stats.deathDisableDelay > 0f)
        {
            yield return new WaitForSeconds(stats.deathDisableDelay);
        }

        NetworkSpawnUtility.Despawn(gameObject);
    }

    private void HandleHealthChanged(int currentHealth, int maxHealth)
    {
        if (stats != null && stats.logCombatEvents)
        {
            Debug.Log($"[Boss] 生命变化：{currentHealth}/{maxHealth}。", this);
        }

        PublishSnapshot();
    }

    private void HandleInvulnerableChanged(bool value)
    {
        PublishSnapshot();
    }

    private void HandleAttackStarted(BossAttackType attackType)
    {
        TransitionTo(BossState.Attack);
        AttackStarted?.Invoke(this, attackType);
        PublishSnapshot();
    }

    private void HandleAttackCompleted(BossAttackType attackType)
    {
        AttackCompleted?.Invoke(this, attackType);
        if (combatEnabled && !bossHealth.IsDead && CurrentState == BossState.Attack)
        {
            TransitionTo(GetRecoveryState());
        }
        PublishSnapshot();
    }

    private void HandleAttackCancelled(BossAttackType attackType)
    {
        AttackCancelled?.Invoke(this, attackType);
        if (combatEnabled &&
            !bossHealth.IsDead &&
            CurrentState != BossState.PhaseChange)
        {
            TransitionTo(GetRecoveryState());
        }
        PublishSnapshot();
    }

    private float ResolveHuntRetreatSpeed()
    {
        float speed = stats != null ? stats.huntFallbackRetreatSpeed : 0f;
        if (stats != null && stats.huntMatchTargetMoveSpeed && player != null)
        {
            PlayerNetworkState playerState =
                player.GetComponentInParent<PlayerNetworkState>();
            if (playerState != null)
            {
                speed = playerState.MoveSpeed;
            }
        }

        float multiplier = stats != null ? stats.huntRetreatSpeedMultiplier : 1f;
        return Mathf.Max(0f, speed * multiplier);
    }

    private BossAttackType SelectPreStaggerAttack()
    {
        int phase = bossHealth != null ? bossHealth.CurrentPhase : 0;
        if (phase <= 0)
        {
            return BossAttackType.Format2;
        }

        if (phase == 1)
        {
            return UnityEngine.Random.value < 0.5f
                ? BossAttackType.Format2
                : BossAttackType.Format4;
        }

        int roll = UnityEngine.Random.Range(0, 3);
        return roll switch
        {
            0 => BossAttackType.Format2,
            1 => BossAttackType.Format3,
            _ => BossAttackType.Format4
        };
    }

    private void SetStaggerState(BossStaggerState nextState)
    {
        if (staggerState == nextState)
        {
            return;
        }

        BossStaggerState previous = staggerState;
        staggerState = nextState;
        StaggerStateChanged?.Invoke(this, previous, staggerState);
        PublishSnapshot();
    }

    private void CancelStagger()
    {
        if (staggerCoroutine != null)
        {
            StopCoroutine(staggerCoroutine);
            staggerCoroutine = null;
        }

        staggerExecutionRequested = false;
        staggerExecutor = null;
        staggerExecutionDamage = 0;
        SetStaggerState(BossStaggerState.None);
    }

    private void ResolveTargetWhenNeeded()
    {
        if (BossTargetResolver.IsUsable(player))
        {
            return;
        }

        if (Time.unscaledTime < nextTargetResolveTime)
        {
            return;
        }

        nextTargetResolveTime = Time.unscaledTime + 0.5f;
        ResolveTargetNow();
    }

    private void ResolveTargetNow()
    {
        SetTargetInternal(BossTargetResolver.Resolve(player, stats));
    }

    private void SetTargetInternal(Transform newTarget)
    {
        if (player == newTarget)
        {
            if (attackController != null)
            {
                attackController.SetPlayer(newTarget);
            }
            return;
        }

        player = newTarget;
        behaviorPolicy?.Reset();
        attackController?.SetPlayer(player);
        TargetChanged?.Invoke(this, player);
        PublishSnapshot();
    }

    private void ForceKillTarget()
    {
        if (player == null)
        {
            return;
        }

        if (BossCombatTarget.TryGetInParent(player, out IForceKillable killable))
        {
            killable.ForceKill();
        }
        else
        {
            Debug.LogError("[Boss] 契约目标没有实现 IForceKillable。", player);
        }
    }

    private void ApplyPhaseTransitionKnockback()
    {
        if (player == null || stats.phaseTransitionKnockback <= 0f)
        {
            return;
        }

        if (BossCombatTarget.TryGetInParent(player, out IKnockbackReceiver receiver))
        {
            receiver.ApplyKnockback(
                transform,
                stats.phaseTransitionKnockback,
                0.22f);
        }
    }

    private void SpawnPhaseBloodPool()
    {
        if (!stats.createPhaseBloodPool)
        {
            return;
        }

        Vector2 randomDirection = UnityEngine.Random.insideUnitCircle.normalized;
        if (randomDirection.sqrMagnitude < 0.001f)
        {
            randomDirection = Vector2.right;
        }

        Vector3 position = transform.position + new Vector3(
            randomDirection.x,
            0f,
            randomDirection.y) * stats.bloodPoolSpawnDistance;
        position.x = Mathf.Clamp(
            position.x,
            stats.arenaCenter.x - stats.arenaHalfSize.x,
            stats.arenaCenter.x + stats.arenaHalfSize.x);
        position.z = Mathf.Clamp(
            position.z,
            stats.arenaCenter.z - stats.arenaHalfSize.y,
            stats.arenaCenter.z + stats.arenaHalfSize.y);
        position.y = stats.GetEffectHeight();

        GameObject poolObject = new("Boss_Phase_BloodPool");
        poolObject.layer = gameObject.layer;
        Transform groundRoot = attackController != null
            ? attackController.GroundIndicator
            : transform.Find("GroundIndicator");
        if (groundRoot != null)
        {
            poolObject.transform.SetParent(groundRoot, true);
        }

        BossPhaseBloodPool pool = poolObject.AddComponent<BossPhaseBloodPool>();
        pool.Initialize(stats, player, position);

        if (NetworkAuthority.IsNetworkActive && networkState != null)
        {
            int networkPoolId = networkState.ServerRegisterBloodPool(position);
            if (networkPoolId > 0)
            {
                pool.Consumed += consumedPool =>
                {
                    if (networkState != null)
                        networkState.ServerUnregisterBloodPool(networkPoolId);
                };
            }
        }
    }

    private void SetCombatEnabled(bool value)
    {
        if (combatEnabled == value)
        {
            return;
        }

        combatEnabled = value;
        CombatEnabledChanged?.Invoke(this, combatEnabled);
        PublishSnapshot();
    }

    private BossState GetRecoveryState()
    {
        return encounterMode == BossEncounterMode.Hunt
            ? BossState.OffscreenIdle
            : BossState.BattleIdle;
    }

    private void TransitionTo(BossState nextState)
    {
        if (CurrentState == nextState)
        {
            return;
        }

        BossState previous = CurrentState;
        CurrentState = nextState;
        StateChanged?.Invoke(this, previous, nextState);
        PublishSnapshot();
    }

    private BossSnapshot CreateSnapshot()
    {
        return new BossSnapshot(
            GetEntityId(),
            CurrentState,
            combatEnabled,
            bossHealth != null ? bossHealth.CurrentHealth : 0,
            bossHealth != null ? bossHealth.MaxHealth : 0,
            bossHealth != null ? bossHealth.CurrentPhase : 0,
            bossHealth != null && bossHealth.IsInvulnerable,
            bossHealth != null && bossHealth.IsDead,
            RemainingContractSeconds,
            transform.position,
            attackController != null ? attackController.LastAttack : null,
            encounterMode,
            staggerState);
    }

    private void PublishSnapshot()
    {
        SnapshotChanged?.Invoke(this, CreateSnapshot());
    }

    private void BindComponentEvents()
    {
        if (eventsBound || bossHealth == null || attackController == null)
        {
            return;
        }

        bossHealth.PhaseChangeStarted += HandlePhaseChangeStarted;
        bossHealth.Died += HandleDeath;
        bossHealth.HealthChanged += HandleHealthChanged;
        bossHealth.InvulnerableChanged += HandleInvulnerableChanged;
        attackController.AttackStarted += HandleAttackStarted;
        attackController.AttackCompleted += HandleAttackCompleted;
        attackController.AttackCancelled += HandleAttackCancelled;
        eventsBound = true;
    }

    private void UnbindComponentEvents()
    {
        if (!eventsBound)
        {
            return;
        }

        if (bossHealth != null)
        {
            bossHealth.PhaseChangeStarted -= HandlePhaseChangeStarted;
            bossHealth.Died -= HandleDeath;
            bossHealth.HealthChanged -= HandleHealthChanged;
            bossHealth.InvulnerableChanged -= HandleInvulnerableChanged;
        }

        if (attackController != null)
        {
            attackController.AttackStarted -= HandleAttackStarted;
            attackController.AttackCompleted -= HandleAttackCompleted;
            attackController.AttackCancelled -= HandleAttackCancelled;
        }

        eventsBound = false;
    }

    private void ResolveComponentReferences()
    {
        stats ??= GetComponent<BossConfig>();
        bossHealth ??= GetComponent<BossHealth>();
        attackController ??= GetComponent<BossAttackController>();
        networkState ??= GetComponent<BossNetworkState>();
        bossRigidbody ??= GetComponent<Rigidbody>();
        bossCollider ??= GetComponent<Collider>();
        animator ??= GetComponentInChildren<Animator>(true);
        audioSource ??= GetComponent<AudioSource>();
        visualRoot ??= transform.Find("Visual");
        vfxRoot ??= transform.Find("VFXRoot");
    }

    /// <summary>
    /// Applies replicated semantic state on non-authoritative peers. This path
    /// never runs AI or gameplay decisions; it only keeps the public Boss facade
    /// and its existing events accurate for UI, NPCs and encounter scripting.
    /// </summary>
    internal void ApplyReplicatedCoreState(
        BossState state,
        bool replicatedCombatEnabled,
        BossEncounterMode replicatedEncounterMode,
        BossStaggerState replicatedStaggerState,
        BossAttackType? activeAttack,
        BossAttackType? lastAttack)
    {
        if (NetworkAuthority.IsServerOrOffline()) return;

        bool changed = false;
        if (CurrentState != state)
        {
            BossState previous = CurrentState;
            CurrentState = state;
            StateChanged?.Invoke(this, previous, CurrentState);
            changed = true;
        }

        if (combatEnabled != replicatedCombatEnabled)
        {
            combatEnabled = replicatedCombatEnabled;
            CombatEnabledChanged?.Invoke(this, combatEnabled);
            changed = true;
        }

        if (encounterMode != replicatedEncounterMode)
        {
            BossEncounterMode previous = encounterMode;
            encounterMode = replicatedEncounterMode;
            EncounterModeChanged?.Invoke(this, previous, encounterMode);
            changed = true;
        }

        if (staggerState != replicatedStaggerState)
        {
            BossStaggerState previous = staggerState;
            staggerState = replicatedStaggerState;
            StaggerStateChanged?.Invoke(this, previous, staggerState);
            changed = true;
        }

        if (replicatedActiveAttack != activeAttack)
        {
            replicatedActiveAttack = activeAttack;
            changed = true;
        }

        BossAttackType? previousLastAttack = attackController != null
            ? attackController.LastAttack
            : null;
        attackController?.ApplyReplicatedAttackState(lastAttack, activeAttack.HasValue);
        if (previousLastAttack != lastAttack) changed = true;
        if (changed) PublishSnapshot();
    }

    internal void ApplyReplicatedTarget(Transform replicatedTarget)
    {
        if (NetworkAuthority.IsServerOrOffline() || player == replicatedTarget) return;
        player = replicatedTarget;
        attackController?.SetPlayer(player);
        TargetChanged?.Invoke(this, player);
        PublishSnapshot();
    }

    internal void ApplyReplicatedAttackLifecycle(
        BossAttackType attack,
        BossAttackLifecycle lifecycle)
    {
        if (NetworkAuthority.IsServerOrOffline()) return;

        switch (lifecycle)
        {
            case BossAttackLifecycle.Started:
                AttackStarted?.Invoke(this, attack);
                break;
            case BossAttackLifecycle.Completed:
                AttackCompleted?.Invoke(this, attack);
                break;
            case BossAttackLifecycle.Cancelled:
                AttackCancelled?.Invoke(this, attack);
                break;
        }
    }

    internal void ApplyReplicatedContractClock(float remainingSeconds, bool active)
    {
        if (NetworkAuthority.IsServerOrOffline()) return;

        remainingSeconds = Mathf.Max(0f, remainingSeconds);
        bool activeChanged = ContractCountdownActive != active;
        bool timeChanged = Mathf.Abs(RemainingContractSeconds - remainingSeconds) >= 0.01f;
        ContractCountdownActive = active;
        RemainingContractSeconds = remainingSeconds;

        if (ContractCountdownActive)
        {
            presentation?.CreateContractVfx();
            presentation?.UpdateContractVfx(player);
        }
        else if (activeChanged)
        {
            presentation?.DestroyContractVfx();
        }

        if (timeChanged || activeChanged)
        {
            ContractCountdownChanged?.Invoke(RemainingContractSeconds);
            if (activeChanged && !active) ContractCountdownExpired?.Invoke();
            PublishSnapshot();
        }
    }

    private void FindAndConfigureGuards()
    {
        BossGuard[] guards = GetComponentsInChildren<BossGuard>(true);
        foreach (BossGuard guard in guards)
        {
            if (guard == null)
            {
                continue;
            }

            if (guard.Side == BossGuardSide.Left)
            {
                leftGuard ??= guard;
            }
            else
            {
                rightGuard ??= guard;
            }
        }

        leftGuard?.Configure(BossGuardSide.Left, stats);
        rightGuard?.Configure(BossGuardSide.Right, stats);
    }

    private void ValidateRuntimeSetup()
    {
        if (runtimeSetupValidated)
        {
            return;
        }

        runtimeSetupValidated = true;
        if (player == null)
        {
            Debug.LogWarning(
                "[Boss 目标] 当前尚未生成可用玩家，Boss 将保持休眠并持续等待 " +
                "PlayerNetworkState、Player Tag 或 BossConfig.playerLayer 中的目标。",
                this);
        }

        string mountIssue = attackController.GetMountConfigurationIssue();
        if (!string.IsNullOrEmpty(mountIssue))
        {
            Debug.LogError($"[Boss 配置] {mountIssue}", this);
        }

        if (animator == null)
        {
            Debug.LogWarning(
                "[Boss 配置] 未找到 Animator。战斗逻辑可运行，但不会播放动画。",
                this);
        }
        else if (animator.runtimeAnimatorController == null &&
                 GetComponent<Boss3DAnimationPresenter>() == null)
        {
            Debug.LogWarning(
                "[Boss 配置] Animator 没有绑定 Controller，且未配置 3D 动画表现器。",
                animator);
        }
        else if (animator.runtimeAnimatorController != null)
        {
            ValidateAnimatorParameter(
                stats.phaseParameter,
                AnimatorControllerParameterType.Int);
            ValidateAnimatorParameter(
                stats.phaseChangeTrigger,
                AnimatorControllerParameterType.Trigger);
            ValidateAnimatorParameter(
                stats.deathTrigger,
                AnimatorControllerParameterType.Trigger);
            ValidateAnimatorParameter(
                stats.format1Trigger,
                AnimatorControllerParameterType.Trigger);
            ValidateAnimatorParameter(
                stats.format2Trigger,
                AnimatorControllerParameterType.Trigger);
            ValidateAnimatorParameter(
                stats.format3Trigger,
                AnimatorControllerParameterType.Trigger);
            ValidateAnimatorParameter(
                stats.format4Trigger,
                AnimatorControllerParameterType.Trigger);
            ValidateAnimatorParameter(
                stats.format5Trigger,
                AnimatorControllerParameterType.Trigger);
            ValidateAnimatorParameter(
                stats.format6Trigger,
                AnimatorControllerParameterType.Trigger);
        }

        if (stats.logCombatEvents)
        {
            Debug.Log(
                $"[Boss] 初始化完成。目标=" +
                $"{(player != null ? player.name : "未找到")}，" +
                $"Animator=" +
                $"{(animator != null ? "已配置" : "未配置")}。",
                this);
        }
    }

    private void ValidateAnimatorParameter(
        string parameterName,
        AnimatorControllerParameterType expectedType)
    {
        if (string.IsNullOrWhiteSpace(parameterName))
        {
            return;
        }

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.name != parameterName)
            {
                continue;
            }

            if (parameter.type != expectedType)
            {
                Debug.LogError(
                    $"[Boss Animator] 参数 {parameterName} 类型错误：" +
                    $"需要 {expectedType}，当前是 {parameter.type}。",
                    animator);
            }
            return;
        }

        Debug.LogError(
            $"[Boss Animator] 缺少参数 {parameterName}（{expectedType}）。",
            animator);
    }

    [ContextMenu("Boss/验证场景绑定")]
    private void ValidateSceneBindings()
    {
        ResolveComponentReferences();
        FindAndConfigureGuards();
        attackController?.ConfigureGuards(leftGuard, rightGuard);
        ValidateRuntimeSetup();
    }

    private static bool IsSupportedAttack(BossAttackType attackType)
    {
        return attackType is BossAttackType.Format1 or
            BossAttackType.Format2 or
            BossAttackType.Format3 or
            BossAttackType.Format4 or
            BossAttackType.Format6;
    }

    private void OnGUI()
    {
        if (stats == null || !stats.showDebugPanel)
        {
            return;
        }

        float x = stats.debugPanelPosition.x;
        float y = stats.debugPanelPosition.y;
        const float width = 360f;
        GUI.Box(new Rect(x, y, width, 218f), "Boss 运行时调试");

        string animatorState = animator == null
            ? "无 Animator"
            : animator.runtimeAnimatorController == null
                ? "未绑定 Controller"
                : "已配置";
        string healthText = bossHealth == null
            ? "生命组件缺失"
            : $"{bossHealth.CurrentHealth}/{bossHealth.MaxHealth}  " +
              $"阶段 {bossHealth.CurrentPhase}";

        GUI.Label(
            new Rect(x + 12f, y + 26f, width - 24f, 22f),
            $"状态：{CurrentState}  战斗：{combatEnabled}");
        GUI.Label(
            new Rect(x + 12f, y + 48f, width - 24f, 22f),
            $"生命：{healthText}");
        GUI.Label(
            new Rect(x + 12f, y + 70f, width - 24f, 22f),
            $"目标：{(player != null ? player.name : "未找到")}  动画：{animatorState}");
        GUI.Label(
            new Rect(x + 12f, y + 92f, width - 24f, 22f),
            $"最近攻击：{(attackController != null ? attackController.LastAttackName : "无")}");

        bool previousEnabled = GUI.enabled;
        GUI.enabled = Application.isPlaying &&
                      bossHealth != null &&
                      !bossHealth.IsDead;
        if (GUI.Button(
                new Rect(x + 12f, y + 120f, 104f, 28f),
                $"Boss -{stats.debugDamageAmount} HP"))
        {
            bossHealth.DebugApplyDamage(stats.debugDamageAmount);
        }

        if (GUI.Button(
                new Rect(x + 124f, y + 120f, 104f, 28f),
                "直接击杀 Boss"))
        {
            bossHealth.DebugApplyDamage(Mathf.Max(1, bossHealth.CurrentHealth));
        }

        GUI.enabled = Application.isPlaying &&
                      attackController != null &&
                      player != null &&
                      bossHealth != null &&
                      !bossHealth.IsDead;
        BossAttackType[] debugAttacks =
        {
            BossAttackType.Format1,
            BossAttackType.Format2,
            BossAttackType.Format3,
            BossAttackType.Format4,
            BossAttackType.Format6
        };
        for (int i = 0; i < debugAttacks.Length; i++)
        {
            const float buttonWidth = 64f;
            float buttonX = x + 12f + i * (buttonWidth + 4f);
            if (GUI.Button(
                    new Rect(buttonX, y + 158f, buttonWidth, 28f),
                    $"攻击 {(int)debugAttacks[i]}"))
            {
                attackController.DebugStartAttack(debugAttacks[i]);
            }
        }

        GUI.enabled = previousEnabled;
        GUI.Label(
            new Rect(x + 12f, y + 190f, width - 24f, 22f),
            "Animator 未配置不会阻止移动、伤害、阶段和弹幕测试。");
    }

    private void OnDisable()
    {
        CancelStagger();
        attackController?.CancelCurrentAttack();
        movement?.Stop();
        BossRegistry.Unregister(this);
        UnbindComponentEvents();
        presentation?.Dispose();
    }

    private void OnDestroy()
    {
        BossRegistry.Unregister(this);
    }

    private void OnDrawGizmosSelected()
    {
        BossConfig config = stats != null ? stats : GetComponent<BossConfig>();
        if (config == null || !config.drawCombatGizmos)
        {
            return;
        }

        Gizmos.color = new Color(0.7f, 0f, 0f, 0.25f);
        Gizmos.DrawWireCube(
            config.arenaCenter,
            new Vector3(
                config.arenaHalfSize.x * 2f,
                0.1f,
                config.arenaHalfSize.y * 2f));

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, config.stoppingDistance);

        BossAttackController attacks = attackController != null
            ? attackController
            : GetComponent<BossAttackController>();
        if (attacks == null)
        {
            return;
        }

        if (attacks.MeleePoint != null)
        {
            Gizmos.color = new Color(1f, 0.35f, 0.1f, 0.9f);
            Gizmos.DrawWireSphere(
                attacks.MeleePoint.position,
                config.format1Radius);
        }

        if (attacks.ProjectileOrigin != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawSphere(attacks.ProjectileOrigin.position, 0.12f);
            Gizmos.DrawRay(
                attacks.ProjectileOrigin.position,
                transform.forward * 2f);
        }

        if (attacks.GroundIndicator != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(attacks.GroundIndicator.position, 0.18f);
        }

        if (attacks.VFXRoot != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawWireCube(attacks.VFXRoot.position, Vector3.one * 0.3f);
        }
    }
}
