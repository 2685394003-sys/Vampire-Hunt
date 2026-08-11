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

    public event Action<float> ContractCountdownChanged;
    public event Action ContractCountdownExpired;

    public event Action<IBossController, BossState, BossState> StateChanged;
    public event Action<IBossController, Transform> TargetChanged;
    public event Action<IBossController, bool> CombatEnabledChanged;
    public event Action<IBossController, BossSnapshot> SnapshotChanged;
    public event Action<IBossController, BossAttackType> AttackStarted;
    public event Action<IBossController, BossAttackType> AttackCompleted;
    public event Action<IBossController, BossAttackType> AttackCancelled;
    public event Action<IBossController> Defeated;

    private BossMovementMotor movement;
    private BossPresentationGateway presentation;
    private Camera viewCamera;
    private Coroutine phaseChangeCoroutine;
    private bool combatEnabled;
    private bool contractCountdownTriggered;
    private bool runtimeSetupValidated;
    private bool eventsBound;
    private bool combatCommandReceived;
    private float nextTargetResolveTime;

    private void Awake()
    {
        ResolveComponentReferences(true);
        EnsureRuntimeMounts();
        FindAndConfigureGuards();
        attackController.ConfigureGuards(leftGuard, rightGuard);

        movement = new BossMovementMotor(
            transform,
            bossRigidbody,
            bossCollider,
            stats);
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
            CurrentState == BossState.PhaseChange)
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
        if (attackController.TryStartAttack(
                bossHealth.CurrentPhase,
                bossVisible,
                distance))
        {
            TransitionTo(BossState.Attack);
            movement.Stop();
            return;
        }

        if (!bossVisible)
        {
            TransitionTo(BossState.OffscreenIdle);
            movement.Stop();
            return;
        }

        TransitionTo(BossState.Chase);
        if ((stats.stationaryAfterFirstPhase && bossHealth.CurrentPhase >= 1) ||
            distance <= stats.stoppingDistance ||
            toPlayer.sqrMagnitude < 0.001f)
        {
            movement.Stop();
            return;
        }

        movement.Move(toPlayer, bossHealth.CurrentPhase, Time.fixedDeltaTime);
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
        SetCombatEnabled(false);
        attackController?.CancelCurrentAttack();
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

        if (CurrentState == BossState.PhaseChange || attackController.IsBusy)
        {
            return BossCommandResult.Busy;
        }

        return attackController.TryForceAttack(attackType)
            ? BossCommandResult.Succeeded
            : BossCommandResult.Rejected;
    }

    private void HandlePhaseChangeStarted(int newPhase)
    {
        if (bossHealth.IsDead)
        {
            return;
        }

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
            TransitionTo(combatEnabled ? BossState.Chase : BossState.Dormant);
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
            TransitionTo(BossState.Chase);
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
            TransitionTo(BossState.Chase);
        }
        PublishSnapshot();
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

        poolObject.AddComponent<BossPhaseBloodPool>()
            .Initialize(stats, player, position);
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
            GetInstanceID(),
            CurrentState,
            combatEnabled,
            bossHealth != null ? bossHealth.CurrentHealth : 0,
            bossHealth != null ? bossHealth.MaxHealth : 0,
            bossHealth != null ? bossHealth.CurrentPhase : 0,
            bossHealth != null && bossHealth.IsInvulnerable,
            bossHealth != null && bossHealth.IsDead,
            RemainingContractSeconds,
            transform.position,
            attackController != null ? attackController.LastAttack : null);
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
        }

        if (attackController != null)
        {
            attackController.AttackStarted -= HandleAttackStarted;
            attackController.AttackCompleted -= HandleAttackCompleted;
            attackController.AttackCancelled -= HandleAttackCancelled;
        }

        eventsBound = false;
    }

    private void ResolveComponentReferences(bool createMissingCollider)
    {
        stats ??= GetComponent<BossConfig>();
        bossHealth ??= GetComponent<BossHealth>();
        attackController ??= GetComponent<BossAttackController>();
        bossRigidbody ??= GetComponent<Rigidbody>();
        bossCollider ??= GetComponent<Collider>();
        if (bossCollider == null && createMissingCollider)
        {
            CapsuleCollider generatedCollider = gameObject.AddComponent<CapsuleCollider>();
            generatedCollider.radius = 0.6f;
            generatedCollider.height = 2.5f;
            generatedCollider.center = Vector3.zero;
            bossCollider = generatedCollider;
        }

        animator ??= GetComponentInChildren<Animator>(true);
        audioSource ??= GetComponent<AudioSource>();
        visualRoot ??= transform.Find("Visual");
        vfxRoot ??= transform.Find("VFXRoot");
    }

    private void EnsureRuntimeMounts()
    {
        visualRoot ??= FindOrCreateChild("Visual", Vector3.zero);
        Transform meleePoint = FindOrCreateChild(
            "MeleePoint",
            new Vector3(0f, 0f, 2f));
        Transform projectileOrigin = FindOrCreateChild(
            "ProjectileOrigin",
            new Vector3(0f, 0.8f, 1.2f));
        Transform groundIndicator = FindOrCreateChild(
            "GroundIndicator",
            Vector3.zero);
        vfxRoot ??= FindOrCreateChild(
            "VFXRoot",
            new Vector3(0f, 0.5f, 0f));

        animator ??= visualRoot.GetComponentInChildren<Animator>(true);
        animator ??= GetComponent<Animator>();
        attackController.ConfigureMounts(
            meleePoint,
            projectileOrigin,
            groundIndicator,
            vfxRoot,
            animator);
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
            Debug.LogError(
                "[Boss 配置] 找不到玩家。请设置 Player Tag，或把玩家放到 " +
                "BossConfig.playerLayer 指定的层。",
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
        else if (animator.runtimeAnimatorController == null)
        {
            Debug.LogWarning(
                "[Boss 配置] Animator 没有绑定 Controller；动画触发器暂不生效。",
                animator);
        }
        else
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
                $"{(animator != null && animator.runtimeAnimatorController != null ? "已配置" : "未配置")}。",
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

    [ContextMenu("Boss/自动配置五个子节点")]
    private void ConfigureFiveChildNodes()
    {
        ResolveComponentReferences(false);
        EnsureRuntimeMounts();
        FindAndConfigureGuards();
        attackController.ConfigureGuards(leftGuard, rightGuard);

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.EditorUtility.SetDirty(attackController);
        }
#endif

        Debug.Log(
            "[Boss 配置] 五个子节点已连线：Visual、MeleePoint、" +
            "ProjectileOrigin、GroundIndicator、VFXRoot。",
            this);
    }

    private Transform FindOrCreateChild(
        string childName,
        Vector3 defaultLocalPosition)
    {
        Transform child = transform.Find(childName);
        if (child != null)
        {
            return child;
        }

        GameObject childObject = new(childName);
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            UnityEditor.Undo.RegisterCreatedObjectUndo(
                childObject,
                $"创建 {childName}");
        }
#endif
        childObject.layer = gameObject.layer;
        child = childObject.transform;
        child.SetParent(transform, false);
        child.localPosition = defaultLocalPosition;
        return child;
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
