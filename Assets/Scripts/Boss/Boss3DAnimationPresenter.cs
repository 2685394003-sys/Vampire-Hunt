using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

[Serializable]
public sealed class Boss3DAnimationSet
{
    [Header("Locomotion")]
    [SerializeField] private AnimationClip idle;
    [SerializeField] private AnimationClip run;
    [SerializeField] private AnimationClip retreat;

    [Header("Encounter")]
    [SerializeField] private AnimationClip phaseChange;
    [SerializeField] private AnimationClip stagger;
    [SerializeField] private AnimationClip executionImpact;
    [SerializeField] private AnimationClip death;

    [Header("Attacks")]
    [SerializeField] private AnimationClip format1Sweep;
    [SerializeField] private AnimationClip format2Barrage;
    [SerializeField] private AnimationClip format3CrossSlash;
    [SerializeField] private AnimationClip format4ChargedSlash;
    [SerializeField] private AnimationClip format6Dash;

    public AnimationClip Idle => idle;
    public AnimationClip Run => run;
    public AnimationClip Retreat => retreat != null ? retreat : run;
    public AnimationClip PhaseChange => phaseChange;
    public AnimationClip Stagger => stagger;
    public AnimationClip ExecutionImpact => executionImpact != null
        ? executionImpact
        : stagger;
    public AnimationClip Death => death;

    public AnimationClip GetAttack(BossAttackType attackType)
    {
        return attackType switch
        {
            BossAttackType.Format1 => format1Sweep,
            BossAttackType.Format2 => format2Barrage,
            BossAttackType.Format3 => format3CrossSlash,
            BossAttackType.Format4 => format4ChargedSlash,
            BossAttackType.Format6 => format6Dash,
            _ => null
        };
    }

    public void CollectMissing(List<string> missing)
    {
        if (idle == null) missing.Add(nameof(idle));
        if (run == null) missing.Add(nameof(run));
        if (phaseChange == null) missing.Add(nameof(phaseChange));
        if (stagger == null) missing.Add(nameof(stagger));
        if (death == null) missing.Add(nameof(death));
        if (format1Sweep == null) missing.Add(nameof(format1Sweep));
        if (format2Barrage == null) missing.Add(nameof(format2Barrage));
        if (format3CrossSlash == null) missing.Add(nameof(format3CrossSlash));
        if (format4ChargedSlash == null) missing.Add(nameof(format4ChargedSlash));
        if (format6Dash == null) missing.Add(nameof(format6Dash));
    }
}

/// <summary>
/// 3D presentation adapter for a Humanoid Boss. Gameplay stays server driven;
/// this component consumes only the stable IBossController event surface and
/// plays retargeted clips through Playables, so no Animator Controller state
/// names are coupled to combat code.
/// </summary>
[AddComponentMenu("Vampire Hunt/Boss/3D Animation Presenter")]
[DisallowMultipleComponent]
public sealed class Boss3DAnimationPresenter : NetworkBehaviour
{
    private const int IdleCommand = 1;
    private const int RunCommand = 2;
    private const int RetreatCommand = 3;
    private const int PhaseChangeCommand = 4;
    private const int StaggerCommand = 5;
    private const int DeathCommand = 6;
    private const int AttackCommandBase = 20;
    private const int ExecutionImpactCommand = 40;

    [SerializeField] private BossController controller;
    [SerializeField] private Animator humanoidAnimator;
    [SerializeField] private Boss3DAnimationSet animations = new();
    [SerializeField, Min(0f)] private float crossFadeDuration = 0.12f;
    [SerializeField] private bool applyFootIk = true;
    [SerializeField] private bool logMissingClips = true;

    public Animator Animator => humanoidAnimator;
    public Boss3DAnimationSet Animations => animations;
    public bool IsReady => graph.IsValid() && humanoidAnimator != null;

    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private AnimationClipPlayable activePlayable;
    private AnimationClipPlayable incomingPlayable;
    private AnimationClip activeClip;
    private AnimationClip incomingClip;
    private int activePort = -1;
    private int incomingPort = -1;
    private float blendElapsed;
    private bool activeLoops;
    private bool incomingLoops;
    private bool eventsBound;
    private bool missingClipsLogged;
    private int localCommandSequence;

    private readonly NetworkVariable<int> networkAnimationCommand = new(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private void OnEnable()
    {
        ResolveReferences();
        if (!NetworkAuthority.IsNetworkActive)
        {
            BindEvents();
        }
        if (!Application.isPlaying)
        {
            return;
        }

        TryInitializeGraph();
        if (!NetworkAuthority.IsNetworkActive)
        {
            PlayForState(controller != null ? controller.State : BossState.Dormant);
        }
    }

    public override void OnNetworkSpawn()
    {
        networkAnimationCommand.OnValueChanged += HandleNetworkAnimationCommand;
        if (IsServer)
        {
            BindEvents();
        }
        else
        {
            UnbindEvents();
        }

        int command = networkAnimationCommand.Value & 0xFF;
        if (command > 0)
        {
            ApplyPresentationCommand(command);
        }
        else if (controller != null)
        {
            PlayForState(controller.State);
        }
    }

    public override void OnNetworkDespawn()
    {
        networkAnimationCommand.OnValueChanged -= HandleNetworkAnimationCommand;
        UnbindEvents();
    }

    private void Update()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (!graph.IsValid() && !TryInitializeGraph())
        {
            return;
        }

        UpdateLoop(activePlayable, activeClip, activeLoops);
        UpdateLoop(incomingPlayable, incomingClip, incomingLoops);

        if (incomingPort < 0)
        {
            return;
        }

        blendElapsed += Time.deltaTime;
        float duration = Mathf.Max(0.0001f, crossFadeDuration);
        float weight = crossFadeDuration <= 0f
            ? 1f
            : Mathf.Clamp01(blendElapsed / duration);
        mixer.SetInputWeight(activePort, 1f - weight);
        mixer.SetInputWeight(incomingPort, weight);
        if (weight >= 1f)
        {
            CompleteBlend();
        }
    }

    public string GetConfigurationIssue()
    {
        ResolveReferences();
        List<string> issues = new();
        if (controller == null) issues.Add("BossController");
        if (humanoidAnimator == null)
        {
            issues.Add("Humanoid Animator");
        }
        else if (humanoidAnimator.avatar == null ||
                 !humanoidAnimator.avatar.isValid ||
                 !humanoidAnimator.avatar.isHuman)
        {
            issues.Add("有效的 Humanoid Avatar");
        }

        animations ??= new Boss3DAnimationSet();
        List<string> missingClips = new();
        animations.CollectMissing(missingClips);
        if (missingClips.Count > 0)
        {
            issues.Add($"动画片段({string.Join(", ", missingClips)})");
        }

        return issues.Count == 0
            ? string.Empty
            : $"Boss 3D 表现缺少：{string.Join("、", issues)}";
    }

    [ContextMenu("Boss/验证 3D 动画配置")]
    private void ValidateConfiguration()
    {
        string issue = GetConfigurationIssue();
        if (string.IsNullOrEmpty(issue))
        {
            Debug.Log("[Boss 3D] Humanoid 与动画片段配置有效。", this);
        }
        else
        {
            Debug.LogError($"[Boss 3D] {issue}", this);
        }
    }

    private bool TryInitializeGraph()
    {
        if (graph.IsValid())
        {
            return true;
        }

        ResolveReferences();
        if (humanoidAnimator == null ||
            humanoidAnimator.avatar == null ||
            !humanoidAnimator.avatar.isValid ||
            !humanoidAnimator.avatar.isHuman)
        {
            return false;
        }

        humanoidAnimator.applyRootMotion = false;
        graph = PlayableGraph.Create($"Boss3D_{name}");
        graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        mixer = AnimationMixerPlayable.Create(graph, 2);
        AnimationPlayableOutput output = AnimationPlayableOutput.Create(
            graph,
            "Boss3DAnimation",
            humanoidAnimator);
        output.SetSourcePlayable(mixer);
        graph.Play();

        string issue = GetConfigurationIssue();
        if (logMissingClips && !missingClipsLogged && !string.IsNullOrEmpty(issue))
        {
            Debug.LogWarning($"[Boss 3D] {issue}", this);
            missingClipsLogged = true;
        }
        return true;
    }

    private void PlayForState(BossState state)
    {
        animations ??= new Boss3DAnimationSet();
        switch (state)
        {
            case BossState.Dormant:
            case BossState.OffscreenIdle:
            case BossState.BattleIdle:
                PlayClip(animations.Idle, true);
                break;
            case BossState.Chase:
                PlayClip(animations.Run, true);
                break;
            case BossState.Retreat:
                PlayClip(animations.Retreat, true);
                break;
            case BossState.PhaseChange:
                PlayClip(animations.PhaseChange, false);
                break;
            case BossState.Stagger:
                PlayClip(animations.Stagger, false);
                break;
            case BossState.Dead:
                PlayClip(animations.Death, false);
                break;
        }
    }

    private void PlayClip(AnimationClip clip, bool loop)
    {
        if (clip == null || (!graph.IsValid() && !TryInitializeGraph()))
        {
            return;
        }

        if (incomingClip == clip || (incomingPort < 0 && activeClip == clip))
        {
            return;
        }

        if (incomingPort >= 0)
        {
            CompleteBlend();
        }

        if (activePort < 0)
        {
            activePort = 0;
            activePlayable = CreatePlayable(clip);
            graph.Connect(activePlayable, 0, mixer, activePort);
            mixer.SetInputWeight(activePort, 1f);
            activeClip = clip;
            activeLoops = loop;
            return;
        }

        incomingPort = activePort == 0 ? 1 : 0;
        incomingPlayable = CreatePlayable(clip);
        graph.Connect(incomingPlayable, 0, mixer, incomingPort);
        mixer.SetInputWeight(incomingPort, 0f);
        incomingClip = clip;
        incomingLoops = loop;
        blendElapsed = 0f;
    }

    private AnimationClipPlayable CreatePlayable(AnimationClip clip)
    {
        AnimationClipPlayable playable = AnimationClipPlayable.Create(graph, clip);
        playable.SetApplyFootIK(applyFootIk);
        playable.SetApplyPlayableIK(false);
        playable.SetTime(0d);
        return playable;
    }

    private void CompleteBlend()
    {
        if (incomingPort < 0)
        {
            return;
        }

        if (activePort >= 0)
        {
            graph.Disconnect(mixer, activePort);
            if (activePlayable.IsValid()) graph.DestroyPlayable(activePlayable);
        }

        activePort = incomingPort;
        activePlayable = incomingPlayable;
        activeClip = incomingClip;
        activeLoops = incomingLoops;
        mixer.SetInputWeight(activePort, 1f);

        incomingPort = -1;
        incomingPlayable = default;
        incomingClip = null;
        incomingLoops = false;
        blendElapsed = 0f;
    }

    private static void UpdateLoop(
        AnimationClipPlayable playable,
        AnimationClip clip,
        bool loop)
    {
        if (!loop || clip == null || clip.length <= 0f || !playable.IsValid())
        {
            return;
        }

        double time = playable.GetTime();
        if (time >= clip.length)
        {
            playable.SetTime(time % clip.length);
        }
    }

    private void BindEvents()
    {
        if (eventsBound || controller == null)
        {
            return;
        }

        controller.StateChanged += HandleStateChanged;
        controller.AttackStarted += HandleAttackStarted;
        controller.StaggerStateChanged += HandleStaggerStateChanged;
        controller.Defeated += HandleDefeated;
        eventsBound = true;
    }

    private void UnbindEvents()
    {
        if (!eventsBound || controller == null)
        {
            return;
        }

        controller.StateChanged -= HandleStateChanged;
        controller.AttackStarted -= HandleAttackStarted;
        controller.StaggerStateChanged -= HandleStaggerStateChanged;
        controller.Defeated -= HandleDefeated;
        eventsBound = false;
    }

    private void HandleStateChanged(
        IBossController boss,
        BossState previous,
        BossState current)
    {
        int command = GetStateCommand(current);
        if (command > 0)
        {
            DispatchPresentationCommand(command);
        }
    }

    private void HandleAttackStarted(IBossController boss, BossAttackType attackType)
    {
        DispatchPresentationCommand(AttackCommandBase + (int)attackType);
    }

    private void HandleStaggerStateChanged(
        IBossController boss,
        BossStaggerState previous,
        BossStaggerState current)
    {
        animations ??= new Boss3DAnimationSet();
        if (current == BossStaggerState.Executed)
        {
            DispatchPresentationCommand(ExecutionImpactCommand);
        }
        else if (current == BossStaggerState.Vulnerable)
        {
            DispatchPresentationCommand(StaggerCommand);
        }
    }

    private void HandleDefeated(IBossController boss)
    {
        DispatchPresentationCommand(DeathCommand);
    }

    private void DispatchPresentationCommand(int command)
    {
        if (command <= 0)
        {
            return;
        }

        if (NetworkAuthority.IsNetworkActive && IsSpawned)
        {
            if (!IsServer)
            {
                return;
            }

            localCommandSequence = (localCommandSequence + 1) & 0x007FFFFF;
            networkAnimationCommand.Value =
                (localCommandSequence << 8) | (command & 0xFF);
        }

        ApplyPresentationCommand(command);
    }

    private void HandleNetworkAnimationCommand(int previous, int current)
    {
        int command = current & 0xFF;
        if (command > 0)
        {
            ApplyPresentationCommand(command);
        }
    }

    private void ApplyPresentationCommand(int command)
    {
        animations ??= new Boss3DAnimationSet();
        switch (command)
        {
            case IdleCommand:
                PlayClip(animations.Idle, true);
                return;
            case RunCommand:
                PlayClip(animations.Run, true);
                return;
            case RetreatCommand:
                PlayClip(animations.Retreat, true);
                return;
            case PhaseChangeCommand:
                PlayClip(animations.PhaseChange, false);
                return;
            case StaggerCommand:
                PlayClip(animations.Stagger, false);
                return;
            case DeathCommand:
                PlayClip(animations.Death, false);
                return;
            case ExecutionImpactCommand:
                PlayClip(animations.ExecutionImpact, false);
                return;
        }

        int attackValue = command - AttackCommandBase;
        if (Enum.IsDefined(typeof(BossAttackType), attackValue))
        {
            PlayClip(animations.GetAttack((BossAttackType)attackValue), false);
        }
    }

    private static int GetStateCommand(BossState state)
    {
        return state switch
        {
            BossState.Dormant => IdleCommand,
            BossState.OffscreenIdle => IdleCommand,
            BossState.BattleIdle => IdleCommand,
            BossState.Chase => RunCommand,
            BossState.Retreat => RetreatCommand,
            BossState.PhaseChange => PhaseChangeCommand,
            BossState.Stagger => StaggerCommand,
            BossState.Dead => DeathCommand,
            _ => 0
        };
    }

    private void ResolveReferences()
    {
        controller ??= GetComponentInParent<BossController>();
        if (humanoidAnimator != null)
        {
            return;
        }

        Animator[] candidates = GetComponentsInChildren<Animator>(true);
        foreach (Animator candidate in candidates)
        {
            if (candidate != null &&
                candidate.avatar != null &&
                candidate.avatar.isValid &&
                candidate.avatar.isHuman)
            {
                humanoidAnimator = candidate;
                return;
            }
        }
    }

    private void OnDisable()
    {
        UnbindEvents();
        if (graph.IsValid())
        {
            graph.Destroy();
        }

        activePort = -1;
        incomingPort = -1;
        activeClip = null;
        incomingClip = null;
    }

    private void OnValidate()
    {
        crossFadeDuration = Mathf.Max(0f, crossFadeDuration);
        animations ??= new Boss3DAnimationSet();
        ResolveReferences();
    }
}
