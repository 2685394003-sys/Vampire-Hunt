using System;
using System.Collections.Generic;
using VampireHunt.Abilities.Contracts;
using VampireHunt.Core;

namespace VampireHunt.Abilities.Domain
{
    /// <summary>
    /// Server-side runtime for duration, periodic and stacked effects. The
    /// system is bound to one target; it owns no Unity object or network state.
    /// </summary>
    public sealed class GameplayAbilitySystem
    {
        private readonly EntityId targetId;
        private readonly GameplayEffectExecutor executor;
        private readonly Dictionary<string, ActiveGameplayEffect> activeEffects =
            new Dictionary<string, ActiveGameplayEffect>(StringComparer.Ordinal);
        private readonly List<string> removalScratch = new List<string>();

        public EntityId TargetId => targetId;
        public int ActiveEffectCount => activeEffects.Count;

        public GameplayAbilitySystem(
            EntityId targetId,
            GameplayEffectExecutor executor)
        {
            if (!targetId.IsValid) throw new ArgumentException("A valid target id is required.", nameof(targetId));
            this.targetId = targetId;
            this.executor = executor ?? throw new ArgumentNullException(nameof(executor));
        }

        public GameplayAbilitySystem(GameplayEffectExecutor executor)
        {
            this.executor = executor ?? throw new ArgumentNullException(nameof(executor));
            targetId = EntityId.Invalid;
        }

        public bool Apply(GameplayEffectSpec spec, EntityId sourceId)
        {
            if (!targetId.IsValid)
                throw new InvalidOperationException("This ability system requires a target id or an explicit context.");
            GameplayEffectContext context = new GameplayEffectContext(sourceId, targetId);
            return Apply(spec, sourceId, in context);
        }

        public bool Apply(in GameplayEffectSpec spec, EntityId sourceId, in GameplayEffectContext context)
        {
            return Apply(spec, sourceId, in context);
        }

        public bool Apply(GameplayEffectSpec spec, EntityId sourceId, in GameplayEffectContext context)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));
            if (!sourceId.IsValid) throw new ArgumentException("A valid source id is required.", nameof(sourceId));
            if (context.SourceId != sourceId)
                throw new ArgumentException("Effect context source id must match sourceId.", nameof(context));
            if (context.TargetId != targetId && targetId.IsValid)
                throw new ArgumentException("Effect context target id must match this system's target id.", nameof(context));

            if (spec.IsInstant)
            {
                ActiveGameplayEffect instant = new ActiveGameplayEffect(spec, sourceId, context.TargetId);
                instant.SetContext(context);
                if (spec.ExecuteOnApplication)
                    executor.Execute(instant, in context);
                executor.PublishCue(instant, GameplayCuePhase.Executed);
                return true;
            }

            string key = BuildKey(sourceId, spec.Id);
            if (activeEffects.TryGetValue(key, out ActiveGameplayEffect existing))
            {
                switch (spec.Stacking)
                {
                    case StackingPolicy.Replace:
                        RemoveInternal(key, existing);
                        break;
                    case StackingPolicy.AddStacks:
                        existing.AddStack();
                        existing.Refresh();
                        existing.SetContext(context);
                        executor.RemoveModifiers(existing);
                        executor.ApplyModifiers(existing);
                        if (spec.ExecuteOnApplication)
                            executor.Execute(existing, in context);
                        executor.PublishCue(existing, GameplayCuePhase.Applied);
                        return true;
                    case StackingPolicy.RefreshDuration:
                        existing.Refresh();
                        existing.SetContext(context);
                        if (spec.ExecuteOnApplication)
                            executor.Execute(existing, in context);
                        executor.PublishCue(existing, GameplayCuePhase.Applied);
                        return true;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(spec));
                }
            }

            ActiveGameplayEffect active = new ActiveGameplayEffect(spec, sourceId, context.TargetId);
            active.SetContext(context);
            executor.ApplyModifiers(active);
            activeEffects.Add(key, active);
            if (spec.ExecuteOnApplication)
                executor.Execute(active, in context);
            executor.PublishCue(active, GameplayCuePhase.Applied);
            return true;
        }

        public int RemoveBySource(EntityId sourceId)
        {
            if (!sourceId.IsValid) return 0;
            removalScratch.Clear();
            foreach (KeyValuePair<string, ActiveGameplayEffect> pair in activeEffects)
                if (pair.Value.SourceId == sourceId) removalScratch.Add(pair.Key);

            int removed = 0;
            foreach (string key in removalScratch)
            {
                if (!activeEffects.TryGetValue(key, out ActiveGameplayEffect active)) continue;
                RemoveInternal(key, active);
                removed++;
            }
            removalScratch.Clear();
            return removed;
        }

        public bool Remove(EffectId effectId, EntityId sourceId)
        {
            if (!effectId.IsValid || !sourceId.IsValid) return false;
            string key = BuildKey(sourceId, effectId);
            if (!activeEffects.TryGetValue(key, out ActiveGameplayEffect active)) return false;
            RemoveInternal(key, active);
            return true;
        }

        public void Tick(float deltaTime)
        {
            if (deltaTime < 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            if (deltaTime <= 0f || activeEffects.Count == 0) return;

            removalScratch.Clear();
            foreach (KeyValuePair<string, ActiveGameplayEffect> pair in activeEffects)
            {
                ActiveGameplayEffect active = pair.Value;
                float periodicWindow = active.Spec.Duration == DurationPolicy.Duration
                    ? Math.Min(deltaTime, active.RemainingTime)
                    : deltaTime;
                int periodExecutions = active.ConsumePeriods(periodicWindow);
                for (int index = 0; index < periodExecutions; index++)
                {
                    GameplayEffectContext context = active.Context;
                    executor.Execute(active, in context, true);
                    executor.PublishCue(active, GameplayCuePhase.Executed);
                }

                if (active.Tick(deltaTime)) removalScratch.Add(pair.Key);
            }

            foreach (string key in removalScratch)
            {
                if (!activeEffects.TryGetValue(key, out ActiveGameplayEffect active)) continue;
                RemoveInternal(key, active);
            }
            removalScratch.Clear();
        }

        public void Clear()
        {
            removalScratch.Clear();
            foreach (string key in activeEffects.Keys) removalScratch.Add(key);
            foreach (string key in removalScratch)
            {
                if (activeEffects.TryGetValue(key, out ActiveGameplayEffect active))
                    RemoveInternal(key, active);
            }
            removalScratch.Clear();
        }

        private void RemoveInternal(string key, ActiveGameplayEffect active)
        {
            executor.RemoveModifiers(active);
            executor.PublishCue(active, GameplayCuePhase.Removed);
            activeEffects.Remove(key);
        }

        private static string BuildKey(EntityId sourceId, EffectId effectId) =>
            sourceId.Value.ToString() + ":" + effectId.Value;
    }
}
