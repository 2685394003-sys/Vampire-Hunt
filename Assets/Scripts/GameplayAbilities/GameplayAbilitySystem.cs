using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Runtime snapshot created when an immutable effect is applied.</summary>
public readonly struct GameplayEffectSpec
{
    public readonly GameplayEffectDefinition Definition;
    public readonly string SourceId;
    public readonly IGameplayAbilitySystemHost Source;
    public readonly GameplayEventData Context;

    public GameplayEffectSpec(
        GameplayEffectDefinition definition,
        string sourceId,
        IGameplayAbilitySystemHost source,
        in GameplayEventData context)
    {
        Definition = definition;
        SourceId = sourceId;
        Source = source;
        Context = context;
    }
}

/// <summary>
/// Centralized GAS-style runtime. A single owner tick manages all durations and
/// periods; no effect creates a MonoBehaviour or subscribes directly to combat.
/// </summary>
public sealed class GameplayAbilitySystem
{
    private readonly struct PendingGameplayEvent
    {
        public readonly GameplayEventData Data;
        public readonly string OnlySourceId;

        public PendingGameplayEvent(in GameplayEventData data, string onlySourceId)
        {
            Data = data;
            OnlySourceId = onlySourceId;
        }
    }

    private sealed class GrantedAbility
    {
        public GameplayAbilityDefinition Definition;
        public string SourceId;
        public float CooldownUntil;
    }

    private sealed class ActiveGameplayEffect
    {
        public string Key;
        public GameplayEffectSpec Spec;
        public int Stacks;
        public float RemainingDuration;
        public float TimeUntilPeriod;
        public readonly List<string> ModifierIds = new();
    }

    private readonly IGameplayAbilitySystemHost owner;
    private readonly SortedDictionary<string, GrantedAbility> abilities =
        new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, ActiveGameplayEffect> activeEffects =
        new(StringComparer.Ordinal);
    private readonly List<string> removalScratch = new();
    private readonly Queue<PendingGameplayEvent> pendingEvents = new();
    private float elapsedTime;
    private int independentEffectSequence;
    private bool isDispatchingEvents;
    private bool isTickingEffects;

    public GameplayTagContainer Tags { get; } = new();
    public int GrantedAbilityCount => abilities.Count;
    public int ActiveEffectCount => activeEffects.Count;

    public GameplayAbilitySystem(IGameplayAbilitySystemHost owner)
    {
        this.owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    public bool CanGrant(IReadOnlyList<GameplayAbilityDefinition> definitions)
    {
        if (definitions == null || definitions.Count == 0) return true;
        HashSet<string> ids = new(StringComparer.Ordinal);
        for (int index = 0; index < definitions.Count; index++)
        {
            GameplayAbilityDefinition definition = definitions[index];
            if (definition == null || !definition.IsValid() || !ids.Add(definition.AbilityId))
                return false;
        }
        return true;
    }

    public bool GrantAbilities(
        IReadOnlyList<GameplayAbilityDefinition> definitions,
        string sourceId)
    {
        if (!CanGrant(definitions) || string.IsNullOrWhiteSpace(sourceId)) return false;
        if (definitions == null || definitions.Count == 0) return true;

        for (int index = 0; index < definitions.Count; index++)
        {
            GameplayAbilityDefinition definition = definitions[index];
            string key = BuildAbilityKey(sourceId, definition.AbilityId);
            if (abilities.ContainsKey(key)) continue;
            abilities.Add(key, new GrantedAbility
            {
                Definition = definition,
                SourceId = sourceId,
                CooldownUntil = 0f
            });
        }

        GameplayEventData granted = new(GameplayEventType.Granted, owner, owner);
        QueueEvent(in granted, sourceId);
        return true;
    }

    public int RemoveAbilitiesBySource(string sourceId)
    {
        if (string.IsNullOrWhiteSpace(sourceId)) return 0;

        removalScratch.Clear();
        foreach (KeyValuePair<string, GrantedAbility> pair in abilities)
            if (string.Equals(pair.Value.SourceId, sourceId, StringComparison.Ordinal))
                removalScratch.Add(pair.Key);

        int removed = removalScratch.Count;
        for (int index = 0; index < removalScratch.Count; index++)
            abilities.Remove(removalScratch[index]);

        removalScratch.Clear();
        foreach (KeyValuePair<string, ActiveGameplayEffect> pair in activeEffects)
            if (string.Equals(pair.Value.Spec.SourceId, sourceId, StringComparison.Ordinal))
                removalScratch.Add(pair.Key);
        for (int index = 0; index < removalScratch.Count; index++)
            RemoveActiveEffect(removalScratch[index]);
        return removed;
    }

    public void SendEvent(in GameplayEventData eventData) => QueueEvent(in eventData, null);

    private void QueueEvent(in GameplayEventData eventData, string onlySourceId)
    {
        if (eventData.Type == GameplayEventType.None) return;
        pendingEvents.Enqueue(new PendingGameplayEvent(in eventData, onlySourceId));
        if (!isDispatchingEvents && !isTickingEffects) DrainEvents();
    }

    private void DrainEvents()
    {
        if (isDispatchingEvents) return;
        isDispatchingEvents = true;
        int safetyBudget = 1024;
        try
        {
            while (pendingEvents.Count > 0 && safetyBudget-- > 0)
            {
                PendingGameplayEvent pending = pendingEvents.Dequeue();
                DispatchEvent(in pending.Data, pending.OnlySourceId);
            }

            if (pendingEvents.Count > 0)
            {
                pendingEvents.Clear();
                Debug.LogError("[GAS] Event cascade exceeded 1024 events and was aborted.");
            }
        }
        finally
        {
            isDispatchingEvents = false;
        }
    }

    private void DispatchEvent(in GameplayEventData eventData, string onlySourceId)
    {
        RefreshEffectsForEvent(eventData.Type);

        foreach (KeyValuePair<string, GrantedAbility> pair in abilities)
        {
            GrantedAbility granted = pair.Value;
            if (onlySourceId != null &&
                !string.Equals(granted.SourceId, onlySourceId, StringComparison.Ordinal))
                continue;
            if (granted.Definition.TriggerEvent != eventData.Type) continue;
            TryActivate(granted, in eventData);
        }
    }

    public void Tick(float deltaTime)
    {
        if (deltaTime <= 0f) return;
        elapsedTime += deltaTime;
        if (activeEffects.Count == 0)
        {
            DrainEvents();
            return;
        }

        isTickingEffects = true;
        removalScratch.Clear();
        try
        {
            foreach (KeyValuePair<string, ActiveGameplayEffect> pair in activeEffects)
            {
                ActiveGameplayEffect active = pair.Value;
                GameplayEffectDefinition definition = active.Spec.Definition;

                if (definition.PeriodSeconds > 0f)
                {
                    active.TimeUntilPeriod -= deltaTime;
                    int catchUpLimit = 8;
                    while (active.TimeUntilPeriod <= 0f && catchUpLimit-- > 0)
                    {
                        Execute(active.Spec, active.Stacks);
                        active.TimeUntilPeriod += definition.PeriodSeconds;
                    }
                }

                if (definition.DurationPolicy != GameplayEffectDurationPolicy.Duration) continue;
                active.RemainingDuration -= deltaTime;
                if (active.RemainingDuration <= 0f) removalScratch.Add(pair.Key);
            }

            for (int index = 0; index < removalScratch.Count; index++)
                RemoveActiveEffect(removalScratch[index]);
        }
        finally
        {
            isTickingEffects = false;
        }

        DrainEvents();
    }

    public void Clear()
    {
        removalScratch.Clear();
        foreach (string key in activeEffects.Keys) removalScratch.Add(key);
        for (int index = 0; index < removalScratch.Count; index++)
            RemoveActiveEffect(removalScratch[index]);

        abilities.Clear();
        Tags.Clear();
        pendingEvents.Clear();
        elapsedTime = 0f;
        independentEffectSequence = 0;
    }

    private bool TryActivate(GrantedAbility granted, in GameplayEventData eventData)
    {
        GameplayAbilityDefinition definition = granted.Definition;
        if (elapsedTime + 0.0001f < granted.CooldownUntil ||
            !Tags.HasAll(definition.RequiredOwnerTags) ||
            Tags.HasAny(definition.BlockedOwnerTags) ||
            (definition.Chance < 1f && UnityEngine.Random.value > definition.Chance))
        {
            return false;
        }

        IGameplayAbilitySystemHost target = definition.Target == GameplayAbilityTarget.Self
            ? owner
            : eventData.Target;
        if (target?.AbilitySystem == null) return false;

        bool appliedAny = false;
        for (int index = 0; index < definition.Effects.Count; index++)
        {
            GameplayEffectDefinition effect = definition.Effects[index];
            GameplayEffectSpec spec = new(effect, granted.SourceId, owner, in eventData);
            appliedAny |= target.AbilitySystem.ApplyEffect(in spec);
        }

        if (appliedAny && definition.InternalCooldownSeconds > 0f)
            granted.CooldownUntil = elapsedTime + definition.InternalCooldownSeconds;
        return appliedAny;
    }

    private bool ApplyEffect(in GameplayEffectSpec spec)
    {
        GameplayEffectDefinition definition = spec.Definition;
        if (definition == null || !definition.IsValid() ||
            !Tags.HasAll(definition.RequiredTargetTags) ||
            Tags.HasAny(definition.BlockedTargetTags))
        {
            return false;
        }

        if (definition.IsInstant)
        {
            Execute(spec, 1);
            EmitCue(definition, GameplayCueEventType.Executed, spec, 1);
            return true;
        }

        string key = BuildEffectKey(spec.SourceId, definition);
        if (activeEffects.TryGetValue(key, out ActiveGameplayEffect existing))
        {
            switch (definition.StackingPolicy)
            {
                case GameplayEffectStackingPolicy.AddStacks:
                    existing.Stacks = Mathf.Min(definition.MaxStacks, existing.Stacks + 1);
                    existing.RemainingDuration = definition.DurationSeconds;
                    RefreshModifiers(existing);
                    if (definition.ExecuteOnApplication) Execute(existing.Spec, existing.Stacks);
                    return true;
                case GameplayEffectStackingPolicy.RefreshDuration:
                    existing.RemainingDuration = definition.DurationSeconds;
                    existing.TimeUntilPeriod = definition.PeriodSeconds;
                    RefreshModifiers(existing);
                    if (definition.ExecuteOnApplication) Execute(existing.Spec, existing.Stacks);
                    return true;
                case GameplayEffectStackingPolicy.Replace:
                    RemoveActiveEffect(key);
                    break;
            }
        }

        ActiveGameplayEffect active = new()
        {
            Key = key,
            Spec = spec,
            Stacks = 1,
            RemainingDuration = definition.DurationSeconds,
            TimeUntilPeriod = definition.PeriodSeconds
        };

        activeEffects.Add(key, active);
        AddGrantedTags(definition);
        if (!ApplyModifiers(active))
        {
            RemoveActiveEffect(key);
            return false;
        }

        if (definition.ExecuteOnApplication) Execute(spec, 1);
        EmitCue(definition, GameplayCueEventType.Applied, spec, 1);
        return true;
    }

    private string BuildEffectKey(string sourceId, GameplayEffectDefinition definition)
    {
        if (definition.StackingPolicy != GameplayEffectStackingPolicy.Replace &&
            definition.StackingPolicy != GameplayEffectStackingPolicy.RefreshDuration &&
            definition.StackingPolicy != GameplayEffectStackingPolicy.AddStacks)
        {
            return $"{sourceId}:{definition.EffectId}:{++independentEffectSequence}";
        }
        return $"{sourceId}:{definition.EffectId}";
    }

    private void RefreshEffectsForEvent(GameplayEventType eventType)
    {
        if (eventType == GameplayEventType.None) return;
        foreach (KeyValuePair<string, ActiveGameplayEffect> pair in activeEffects)
            if (pair.Value.Spec.Definition.RefreshOnEvent == eventType)
                RefreshModifiers(pair.Value);
    }

    private bool ApplyModifiers(ActiveGameplayEffect active)
    {
        GameplayEffectDefinition definition = active.Spec.Definition;
        for (int index = 0; index < definition.Modifiers.Count; index++)
        {
            GameplayModifierDefinition modifier = definition.Modifiers[index];
            float value = modifier.Magnitude.Evaluate(
                active.Spec.Source,
                owner,
                in active.Spec.Context) * active.Stacks;
            string modifierId = $"gas:{active.Key}:{index}";
            if (!owner.AddGameplayModifier(
                    modifierId,
                    active.Spec.SourceId,
                    modifier.Stat,
                    modifier.Operation,
                    value))
            {
                return false;
            }
            active.ModifierIds.Add(modifierId);
        }
        return true;
    }

    private void RefreshModifiers(ActiveGameplayEffect active)
    {
        RemoveModifiers(active);
        if (!ApplyModifiers(active))
            Debug.LogError($"[GAS] Failed to refresh modifiers for '{active.Key}'.");
    }

    private void RemoveModifiers(ActiveGameplayEffect active)
    {
        for (int index = 0; index < active.ModifierIds.Count; index++)
            owner.RemoveGameplayModifier(active.ModifierIds[index]);
        active.ModifierIds.Clear();
    }

    private void Execute(in GameplayEffectSpec spec, int stacks)
    {
        GameplayEffectDefinition definition = spec.Definition;
        for (int index = 0; index < definition.Executions.Count; index++)
        {
            GameplayExecutionDefinition execution = definition.Executions[index];
            float raw = execution.Magnitude.Evaluate(
                spec.Source,
                owner,
                in spec.Context) * stacks;
            switch (execution.ExecutionType)
            {
                case GameplayExecutionType.Heal:
                    owner.HealGameplay(Mathf.Max(0, Mathf.CeilToInt(raw)));
                    break;
                case GameplayExecutionType.AddScarlet:
                    owner.AddScarletGameplay(Mathf.Max(0f, raw));
                    break;
                case GameplayExecutionType.AddCoins:
                    owner.AddCoinsGameplay(Mathf.Max(0, Mathf.RoundToInt(raw)));
                    break;
            }
        }
    }

    private void RemoveActiveEffect(string key)
    {
        if (!activeEffects.TryGetValue(key, out ActiveGameplayEffect active)) return;
        RemoveModifiers(active);
        RemoveGrantedTags(active.Spec.Definition);
        EmitCue(
            active.Spec.Definition,
            GameplayCueEventType.Removed,
            active.Spec,
            active.Stacks);
        activeEffects.Remove(key);
    }

    private void AddGrantedTags(GameplayEffectDefinition definition)
    {
        for (int index = 0; index < definition.GrantedTags.Count; index++)
            Tags.Add(definition.GrantedTags[index]);
    }

    private void RemoveGrantedTags(GameplayEffectDefinition definition)
    {
        for (int index = 0; index < definition.GrantedTags.Count; index++)
            Tags.Remove(definition.GrantedTags[index]);
    }

    private void EmitCue(
        GameplayEffectDefinition definition,
        GameplayCueEventType type,
        in GameplayEffectSpec spec,
        int stacks)
    {
        if (string.IsNullOrWhiteSpace(definition.CueTag)) return;
        float magnitude = spec.Context.Magnitude * stacks;
        GameplayCueEvent cue = new(definition.CueTag, type, magnitude);
        owner.EmitGameplayCue(in cue);
    }

    private static string BuildAbilityKey(string sourceId, string abilityId) =>
        $"{sourceId}:{abilityId}";
}
