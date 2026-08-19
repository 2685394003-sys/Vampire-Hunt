using System;
using System.Collections.Generic;
using VampireHunt.Abilities.Contracts;
using VampireHunt.Combat.Application;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;
using VampireHunt.Stats;

namespace VampireHunt.Abilities.Domain
{
    /// <summary>
    /// Executes only the capabilities represented by an effect. Damage and
    /// healing are delegated to CombatApplicationService; attributes are sent
    /// to the narrow modifier target interface.
    /// </summary>
    public sealed class GameplayEffectExecutor
    {
        private readonly CombatApplicationService combat;
        private readonly IAttributeModifierTarget attributeTarget;
        private readonly IPreciseAttributeModifierTarget preciseAttributeTarget;
        private readonly IGameplayEventSink eventSink;
        private readonly IGameClock clock;
        private readonly GameplayEventIdAllocator eventIds;

        public GameplayEffectExecutor(
            CombatApplicationService combatApplicationService,
            IAttributeModifierTarget attributeModifierTarget = null,
            IGameplayEventSink eventSink = null,
            IGameClock clock = null,
            GameplayEventIdAllocator eventIds = null)
        {
            combat = combatApplicationService ?? throw new ArgumentNullException(nameof(combatApplicationService));
            attributeTarget = attributeModifierTarget;
            preciseAttributeTarget = attributeModifierTarget as IPreciseAttributeModifierTarget;
            this.eventSink = eventSink;
            this.clock = clock;
            this.eventIds = eventIds ?? new GameplayEventIdAllocator();
        }

        public GameplayEffectExecutionResult Execute(
            ActiveGameplayEffect effect,
            in GameplayEffectContext context)
        {
            if (effect == null) throw new ArgumentNullException(nameof(effect));
            return Execute(effect, in context, false);
        }

        internal GameplayEffectExecutionResult Execute(
            ActiveGameplayEffect effect,
            in GameplayEffectContext context,
            bool periodic)
        {
            if (effect == null) throw new ArgumentNullException(nameof(effect));
            ValidateContext(context);
            bool hasAttributeExecution = HasAttributeExecution(effect.Spec.Executions);
            if (hasAttributeExecution)
            {
                EnsurePreciseAttributeTarget();
                // Attribute executions represent the effect's current
                // contribution. Re-application, refresh and periodic ticks
                // replace that contribution instead of accumulating orphaned
                // registrations under the same source id.
                RemoveOwnedModifiers(effect, true);
            }

            int damages = 0;
            int healings = 0;
            int modifierApplications = 0;
            foreach (GameplayEffectExecution execution in effect.Spec.Executions)
            {
                int amount = ToNonNegativeInt(execution.Magnitude * effect.StackCount);
                switch (execution.Type)
                {
                    case GameplayExecutionType.Damage:
                    {
                        DamageFlags flags = execution.DamageFlags;
                        if (periodic) flags |= DamageFlags.NoCritical | DamageFlags.Periodic;
                        DamageRequest request = new DamageRequest(
                            context.SourceId,
                            context.TargetId,
                            amount,
                            flags,
                            context.Hit);
                        combat.ApplyDamage(in request, context.SourceStats);
                        damages++;
                        break;
                    }
                    case GameplayExecutionType.Healing:
                    {
                        HealingRequest request = new HealingRequest(
                            context.SourceId,
                            context.TargetId,
                            amount);
                        combat.ApplyHealing(in request);
                        healings++;
                        break;
                    }
                    case GameplayExecutionType.Attribute:
                        RegisterModifier(
                            effect,
                            execution.Modifier.Bind(context.SourceId),
                            true);
                        modifierApplications++;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(execution.Type));
                }
            }
            return new GameplayEffectExecutionResult(damages, healings, modifierApplications);
        }

        internal int ApplyModifiers(ActiveGameplayEffect effect)
        {
            if (effect == null) throw new ArgumentNullException(nameof(effect));
            if (effect.Spec.Modifiers.Count == 0) return 0;
            if (attributeTarget == null)
                throw new InvalidOperationException("An effect with modifiers requires IAttributeModifierTarget.");
            EnsurePreciseAttributeTarget();

            int applied = 0;
            foreach (GameplayModifierSpec modifier in effect.Spec.Modifiers)
            {
                StatModifier bound = modifier.Bind(effect.SourceId);
                RegisterModifier(effect, bound, false);
                applied++;
            }
            return applied;
        }

        internal int RemoveModifiers(ActiveGameplayEffect effect)
        {
            if (effect == null || attributeTarget == null) return 0;
            if (effect.OwnedModifierCount == 0) return 0;
            EnsurePreciseAttributeTarget();
            return RemoveOwnedModifiers(effect, false);
        }

        internal int RemoveBaseModifiers(ActiveGameplayEffect effect)
        {
            if (effect == null || attributeTarget == null) return 0;
            if (effect.OwnedModifierCount == effect.OwnedExecutionModifierCount) return 0;
            EnsurePreciseAttributeTarget();
            return RemoveOwnedModifiers(effect, false, includeExecution: false);
        }

        internal void PublishCue(ActiveGameplayEffect effect, GameplayCuePhase phase)
        {
            if (effect == null || !effect.Spec.Cue.IsValid || eventSink == null) return;
            GameplayEffectContext context = effect.Context;
            GameplayCueEvent cue = new GameplayCueEvent(
                eventIds.Next(),
                clock == null ? 0d : clock.Now,
                effect.Spec.Cue,
                effect.TargetId,
                context.Hit.Position,
                phase);
            eventSink.Publish(cue);
        }

        private static void ValidateContext(GameplayEffectContext context)
        {
            if (!context.SourceId.IsValid) throw new ArgumentException("A valid source id is required in effect context.");
            if (!context.TargetId.IsValid) throw new ArgumentException("A valid target id is required in effect context.");
        }

        private static int ToNonNegativeInt(float value)
        {
            if (value <= 0f) return 0;
            if (float.IsNaN(value) || float.IsInfinity(value) || value >= int.MaxValue) return int.MaxValue;
            return (int)Math.Round(value, MidpointRounding.AwayFromZero);
        }

        private void RegisterModifier(
            ActiveGameplayEffect effect,
            StatModifier modifier,
            bool execution)
        {
            EnsurePreciseAttributeTarget();
            StatModifierHandle handle = preciseAttributeTarget.AddModifierWithHandle(modifier);
            if (!handle.IsValid)
                throw new InvalidOperationException("The attribute target returned an invalid modifier handle.");
            effect.RegisterModifier(handle, execution);
        }

        private int RemoveOwnedModifiers(
            ActiveGameplayEffect effect,
            bool executionOnly,
            bool includeExecution = true)
        {
            StatModifierHandle[] handles = effect.CopyOwnedModifierHandles(executionOnly);
            StatModifierHandle[] executionHandles = includeExecution
                ? null
                : effect.CopyOwnedModifierHandles(true);
            int removed = 0;
            foreach (StatModifierHandle handle in handles)
            {
                if (!includeExecution && Contains(executionHandles, handle))
                    continue;

                if (preciseAttributeTarget.RemoveModifier(handle)) removed++;
                effect.ForgetModifier(handle);
            }
            return removed;
        }

        private static bool Contains(StatModifierHandle[] handles, StatModifierHandle value)
        {
            if (handles == null) return false;
            for (int index = 0; index < handles.Length; index++)
                if (handles[index] == value) return true;
            return false;
        }

        private void EnsurePreciseAttributeTarget()
        {
            if (attributeTarget == null)
                throw new InvalidOperationException("An attribute execution requires IAttributeModifierTarget.");
            if (preciseAttributeTarget == null)
                throw new InvalidOperationException(
                    "Gameplay effects require IPreciseAttributeModifierTarget for exact modifier ownership.");
        }

        private static bool HasAttributeExecution(IReadOnlyList<GameplayEffectExecution> executions)
        {
            foreach (GameplayEffectExecution execution in executions)
                if (execution.Type == GameplayExecutionType.Attribute) return true;
            return false;
        }
    }

}
