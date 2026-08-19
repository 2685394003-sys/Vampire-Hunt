using System;
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
                        if (attributeTarget == null)
                            throw new InvalidOperationException("An attribute execution requires IAttributeModifierTarget.");
                        attributeTarget.AddModifier(execution.Modifier.Bind(context.SourceId));
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

            int applied = 0;
            foreach (GameplayModifierSpec modifier in effect.Spec.Modifiers)
            {
                StatModifier bound = modifier.Bind(effect.SourceId);
                attributeTarget.AddModifier(bound);
                applied++;
            }
            return applied;
        }

        internal int RemoveModifiers(ActiveGameplayEffect effect)
        {
            if (effect == null || effect.Spec.Modifiers.Count == 0 || attributeTarget == null) return 0;
            return attributeTarget.RemoveModifiers(effect.SourceId);
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
    }

}
