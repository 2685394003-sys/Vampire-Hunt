using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Server-side events that can activate abilities or refresh effects.</summary>
public enum GameplayEventType
{
    None = 0,
    Granted = 1,
    AttackHit = 2,
    CriticalHit = 3,
    EnemyKilled = 4,
    HealthChanged = 5
}

public enum GameplayAbilityTarget
{
    Self = 0,
    EventTarget = 1
}

public enum GameplayEffectDurationPolicy
{
    Instant = 0,
    Duration = 1,
    Infinite = 2
}

public enum GameplayEffectStackingPolicy
{
    RefreshDuration = 0,
    AddStacks = 1,
    Replace = 2
}

public enum GameplayMagnitudeSource
{
    Fixed = 0,
    EventMagnitude = 1,
    SourceMissingHealthRatio = 2,
    TargetMissingHealthRatio = 3
}

public enum GameplayExecutionType
{
    Heal = 0,
    AddScarlet = 1,
    AddCoins = 2
}

public enum GameplayCueEventType : byte
{
    Applied = 0,
    Executed = 1,
    Removed = 2
}

/// <summary>
/// Immutable payload routed through an ability system. Gameplay changes are
/// produced on the server; replicated presentation is handled by gameplay cues.
/// </summary>
public readonly struct GameplayEventData
{
    public readonly GameplayEventType Type;
    public readonly IGameplayAbilitySystemHost Source;
    public readonly IGameplayAbilitySystemHost Target;
    public readonly float Magnitude;
    public readonly bool WasCritical;
    public readonly Vector3 Position;

    public GameplayEventData(
        GameplayEventType type,
        IGameplayAbilitySystemHost source,
        IGameplayAbilitySystemHost target = null,
        float magnitude = 0f,
        bool wasCritical = false,
        Vector3 position = default)
    {
        Type = type;
        Source = source;
        Target = target;
        Magnitude = magnitude;
        WasCritical = wasCritical;
        Position = position;
    }
}

public readonly struct GameplayCueEvent
{
    public readonly string CueTag;
    public readonly GameplayCueEventType Type;
    public readonly float Magnitude;

    public GameplayCueEvent(string cueTag, GameplayCueEventType type, float magnitude)
    {
        CueTag = cueTag;
        Type = type;
        Magnitude = magnitude;
    }
}

/// <summary>
/// Bridge between the engine-agnostic effect runner and an owning gameplay
/// entity. PlayerNetworkState is the first implementation; enemies can adopt
/// the same boundary without changing the effect runner.
/// </summary>
public interface IGameplayAbilitySystemHost
{
    string GameplayOwnerId { get; }
    float GameplayHealthRatio { get; }
    GameplayAbilitySystem AbilitySystem { get; }

    bool AddGameplayModifier(
        string modifierId,
        string sourceId,
        PlayerStatType stat,
        PlayerModifierOperation operation,
        float value);

    bool RemoveGameplayModifier(string modifierId);
    int HealGameplay(int amount);
    void AddScarletGameplay(float amount);
    void AddCoinsGameplay(int amount);
    void EmitGameplayCue(in GameplayCueEvent cueEvent);
}

/// <summary>
/// Data-authored magnitude calculation. This is the light-weight equivalent of
/// GAS scalable floats/MMCs and avoids allocating calculator objects at runtime.
/// </summary>
[Serializable]
public sealed class GameplayMagnitudeDefinition
{
    [SerializeField] private GameplayMagnitudeSource source;
    [SerializeField] private float coefficient = 1f;
    [SerializeField] private float additive;
    [SerializeField] private bool clampResult;
    [SerializeField] private float minimum;
    [SerializeField] private float maximum = float.MaxValue;

    public GameplayMagnitudeSource Source => source;
    public float Coefficient => coefficient;
    public float Additive => additive;

    public GameplayMagnitudeDefinition() { }

    public GameplayMagnitudeDefinition(
        GameplayMagnitudeSource source,
        float coefficient = 1f,
        float additive = 0f,
        bool clampResult = false,
        float minimum = 0f,
        float maximum = float.MaxValue)
    {
        this.source = source;
        this.coefficient = coefficient;
        this.additive = additive;
        this.clampResult = clampResult;
        this.minimum = minimum;
        this.maximum = maximum;
    }

    public float Evaluate(
        IGameplayAbilitySystemHost sourceHost,
        IGameplayAbilitySystemHost targetHost,
        in GameplayEventData eventData)
    {
        float basis = source switch
        {
            GameplayMagnitudeSource.EventMagnitude => eventData.Magnitude,
            GameplayMagnitudeSource.SourceMissingHealthRatio =>
                1f - Mathf.Clamp01(sourceHost?.GameplayHealthRatio ?? 1f),
            GameplayMagnitudeSource.TargetMissingHealthRatio =>
                1f - Mathf.Clamp01(targetHost?.GameplayHealthRatio ?? 1f),
            _ => 1f
        };

        float result = basis * coefficient + additive;
        if (!clampResult) return result;
        return Mathf.Clamp(result, minimum, Mathf.Max(minimum, maximum));
    }
}

[Serializable]
public sealed class GameplayModifierDefinition
{
    [SerializeField] private PlayerStatType stat;
    [SerializeField] private PlayerModifierOperation operation;
    [SerializeField] private GameplayMagnitudeDefinition magnitude = new();

    public PlayerStatType Stat => stat;
    public PlayerModifierOperation Operation => operation;
    public GameplayMagnitudeDefinition Magnitude => magnitude;

    public GameplayModifierDefinition() { }

    public GameplayModifierDefinition(
        PlayerStatType stat,
        PlayerModifierOperation operation,
        GameplayMagnitudeDefinition magnitude)
    {
        this.stat = stat;
        this.operation = operation;
        this.magnitude = magnitude;
    }

    public bool IsValid() =>
        Enum.IsDefined(typeof(PlayerStatType), stat) &&
        Enum.IsDefined(typeof(PlayerModifierOperation), operation) &&
        magnitude != null;
}

[Serializable]
public sealed class GameplayExecutionDefinition
{
    [SerializeField] private GameplayExecutionType executionType;
    [SerializeField] private GameplayMagnitudeDefinition magnitude = new();

    public GameplayExecutionType ExecutionType => executionType;
    public GameplayMagnitudeDefinition Magnitude => magnitude;

    public GameplayExecutionDefinition() { }

    public GameplayExecutionDefinition(
        GameplayExecutionType executionType,
        GameplayMagnitudeDefinition magnitude)
    {
        this.executionType = executionType;
        this.magnitude = magnitude;
    }

    public bool IsValid() =>
        Enum.IsDefined(typeof(GameplayExecutionType), executionType) &&
        magnitude != null;
}

/// <summary>
/// Immutable, design-authored gameplay effect. Runtime state lives exclusively
/// in GameplayEffectSpec/ActiveGameplayEffect, never in this definition.
/// </summary>
[Serializable]
public sealed class GameplayEffectDefinition
{
    [SerializeField] private string effectId;
    [SerializeField] private GameplayEffectDurationPolicy durationPolicy;
    [SerializeField, Min(0f)] private float durationSeconds;
    [SerializeField, Min(0f)] private float periodSeconds;
    [SerializeField] private bool executeOnApplication = true;
    [SerializeField] private GameplayEffectStackingPolicy stackingPolicy;
    [SerializeField, Min(1)] private int maxStacks = 1;
    [SerializeField] private GameplayEventType refreshOnEvent;
    [SerializeField] private string[] grantedTags = Array.Empty<string>();
    [SerializeField] private string[] requiredTargetTags = Array.Empty<string>();
    [SerializeField] private string[] blockedTargetTags = Array.Empty<string>();
    [SerializeField] private string cueTag;
    [SerializeField] private List<GameplayModifierDefinition> modifiers = new();
    [SerializeField] private List<GameplayExecutionDefinition> executions = new();

    public string EffectId => effectId;
    public GameplayEffectDurationPolicy DurationPolicy => durationPolicy;
    public float DurationSeconds => durationSeconds;
    public float PeriodSeconds => periodSeconds;
    public bool ExecuteOnApplication => executeOnApplication;
    public GameplayEffectStackingPolicy StackingPolicy => stackingPolicy;
    public int MaxStacks => Mathf.Max(1, maxStacks);
    public GameplayEventType RefreshOnEvent => refreshOnEvent;
    public IReadOnlyList<string> GrantedTags => grantedTags ?? Array.Empty<string>();
    public IReadOnlyList<string> RequiredTargetTags => requiredTargetTags ?? Array.Empty<string>();
    public IReadOnlyList<string> BlockedTargetTags => blockedTargetTags ?? Array.Empty<string>();
    public string CueTag => cueTag;
    public IReadOnlyList<GameplayModifierDefinition> Modifiers =>
        modifiers ?? (IReadOnlyList<GameplayModifierDefinition>)Array.Empty<GameplayModifierDefinition>();
    public IReadOnlyList<GameplayExecutionDefinition> Executions =>
        executions ?? (IReadOnlyList<GameplayExecutionDefinition>)Array.Empty<GameplayExecutionDefinition>();

    public bool IsInstant => durationPolicy == GameplayEffectDurationPolicy.Instant;

    public GameplayEffectDefinition() { }

    public GameplayEffectDefinition(
        string effectId,
        GameplayEffectDurationPolicy durationPolicy,
        IEnumerable<GameplayModifierDefinition> modifiers = null,
        IEnumerable<GameplayExecutionDefinition> executions = null,
        float durationSeconds = 0f,
        float periodSeconds = 0f,
        bool executeOnApplication = true,
        GameplayEffectStackingPolicy stackingPolicy = GameplayEffectStackingPolicy.RefreshDuration,
        int maxStacks = 1,
        GameplayEventType refreshOnEvent = GameplayEventType.None,
        string[] grantedTags = null,
        string[] requiredTargetTags = null,
        string[] blockedTargetTags = null,
        string cueTag = null)
    {
        this.effectId = effectId;
        this.durationPolicy = durationPolicy;
        this.durationSeconds = durationSeconds;
        this.periodSeconds = periodSeconds;
        this.executeOnApplication = executeOnApplication;
        this.stackingPolicy = stackingPolicy;
        this.maxStacks = maxStacks;
        this.refreshOnEvent = refreshOnEvent;
        this.grantedTags = grantedTags ?? Array.Empty<string>();
        this.requiredTargetTags = requiredTargetTags ?? Array.Empty<string>();
        this.blockedTargetTags = blockedTargetTags ?? Array.Empty<string>();
        this.cueTag = cueTag;
        this.modifiers = modifiers != null
            ? new List<GameplayModifierDefinition>(modifiers)
            : new List<GameplayModifierDefinition>();
        this.executions = executions != null
            ? new List<GameplayExecutionDefinition>(executions)
            : new List<GameplayExecutionDefinition>();
    }

    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(effectId) ||
            !Enum.IsDefined(typeof(GameplayEffectDurationPolicy), durationPolicy) ||
            !Enum.IsDefined(typeof(GameplayEffectStackingPolicy), stackingPolicy) ||
            durationSeconds < 0f || periodSeconds < 0f || maxStacks < 1 ||
            (durationPolicy == GameplayEffectDurationPolicy.Duration && durationSeconds <= 0f) ||
            (durationPolicy == GameplayEffectDurationPolicy.Instant && Modifiers.Count > 0))
        {
            return false;
        }

        foreach (GameplayModifierDefinition modifier in Modifiers)
            if (modifier == null || !modifier.IsValid()) return false;
        foreach (GameplayExecutionDefinition execution in Executions)
            if (execution == null || !execution.IsValid()) return false;
        return true;
    }
}

/// <summary>
/// A granted ability listens for one gameplay event and applies one or more
/// effects. Game-specific multi-stage behaviours can later wrap the same API.
/// </summary>
[Serializable]
public sealed class GameplayAbilityDefinition
{
    [SerializeField] private string abilityId;
    [SerializeField] private GameplayEventType triggerEvent;
    [SerializeField] private GameplayAbilityTarget target;
    [SerializeField, Range(0f, 1f)] private float chance = 1f;
    [SerializeField, Min(0f)] private float internalCooldownSeconds;
    [SerializeField] private string[] requiredOwnerTags = Array.Empty<string>();
    [SerializeField] private string[] blockedOwnerTags = Array.Empty<string>();
    [SerializeField] private List<GameplayEffectDefinition> effects = new();

    public string AbilityId => abilityId;
    public GameplayEventType TriggerEvent => triggerEvent;
    public GameplayAbilityTarget Target => target;
    public float Chance => Mathf.Clamp01(chance);
    public float InternalCooldownSeconds => Mathf.Max(0f, internalCooldownSeconds);
    public IReadOnlyList<string> RequiredOwnerTags => requiredOwnerTags ?? Array.Empty<string>();
    public IReadOnlyList<string> BlockedOwnerTags => blockedOwnerTags ?? Array.Empty<string>();
    public IReadOnlyList<GameplayEffectDefinition> Effects =>
        effects ?? (IReadOnlyList<GameplayEffectDefinition>)Array.Empty<GameplayEffectDefinition>();

    public GameplayAbilityDefinition() { }

    public GameplayAbilityDefinition(
        string abilityId,
        GameplayEventType triggerEvent,
        GameplayAbilityTarget target,
        IEnumerable<GameplayEffectDefinition> effects,
        float chance = 1f,
        float internalCooldownSeconds = 0f,
        string[] requiredOwnerTags = null,
        string[] blockedOwnerTags = null)
    {
        this.abilityId = abilityId;
        this.triggerEvent = triggerEvent;
        this.target = target;
        this.chance = chance;
        this.internalCooldownSeconds = internalCooldownSeconds;
        this.requiredOwnerTags = requiredOwnerTags ?? Array.Empty<string>();
        this.blockedOwnerTags = blockedOwnerTags ?? Array.Empty<string>();
        this.effects = effects != null
            ? new List<GameplayEffectDefinition>(effects)
            : new List<GameplayEffectDefinition>();
    }

    public bool IsValid()
    {
        if (string.IsNullOrWhiteSpace(abilityId) ||
            triggerEvent == GameplayEventType.None ||
            !Enum.IsDefined(typeof(GameplayAbilityTarget), target) ||
            chance < 0f || chance > 1f || internalCooldownSeconds < 0f ||
            Effects.Count == 0)
        {
            return false;
        }

        foreach (GameplayEffectDefinition effect in Effects)
            if (effect == null || !effect.IsValid()) return false;
        return true;
    }
}
