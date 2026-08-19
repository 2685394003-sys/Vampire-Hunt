using System;
using VampireHunt.Combat.Contracts;
using VampireHunt.Combat.Domain;
using VampireHunt.Core;
using VampireHunt.Stats;

namespace VampireHunt.Combat.Application
{
    /// <summary>
    /// The single domain entry point for changing health or applying knockback.
    /// It resolves intent, delegates the capability mutation, and emits the
    /// confirmed event only after the receiver returns an actual result.
    /// </summary>
    public sealed class CombatApplicationService
    {
        private readonly CombatResolver resolver;
        private readonly ICombatEntityDirectory directory;
        private readonly IGameplayEventSink eventSink;
        private readonly IGameClock clock;
        private readonly GameplayEventIdAllocator eventIds;

        public CombatApplicationService(
            CombatResolver resolver,
            ICombatEntityDirectory directory,
            IGameplayEventSink eventSink = null,
            IGameClock clock = null,
            GameplayEventIdAllocator eventIds = null)
        {
            this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            this.directory = directory ?? throw new ArgumentNullException(nameof(directory));
            this.eventSink = eventSink;
            this.clock = clock;
            this.eventIds = eventIds ?? new GameplayEventIdAllocator();
        }

        public DamageResult ApplyDamage(in DamageRequest request, IStatSnapshot sourceStats = null)
        {
            ResolvedDamage resolved = resolver.Resolve(in request, sourceStats);
            IDamageReceiver receiver = directory.TryGetDamageReceiver(request.TargetId);
            if (receiver == null)
                return DamageResult.NoDamage(request.BaseDamage, request.Hit.Position);

            DamageResult receiverResult = receiver.ApplyDamage(in resolved);
            int applied = receiverResult.AppliedDamage;
            if (applied < 0) applied = 0;
            if (applied > resolved.FinalDamage) applied = resolved.FinalDamage;

            bool wasKilled = applied > 0 && receiverResult.WasKilled;
            DamageResult result = new DamageResult(
                request.BaseDamage,
                applied,
                applied > 0 && resolved.WasCritical,
                wasKilled,
                resolved.Hit.Position);

            // A blocked, invulnerable or already-dead target is a valid combat
            // resolution but not a presentation event. Only actual health loss
            // is confirmed to clients, avoiding zero-damage combat-text noise.
            if (applied > 0)
            {
                Publish(new DamageConfirmedEvent(
                    eventIds.Next(),
                    clock == null ? 0d : clock.Now,
                    request.SourceId,
                    request.TargetId,
                    result,
                    request.Flags,
                    request.Hit));

                if (result.WasKilled)
                {
                    Publish(new EntityDiedEvent(
                        eventIds.Next(),
                        clock == null ? 0d : clock.Now,
                        request.TargetId,
                        request.SourceId,
                        request.Hit.Position));
                }
            }

            return result;
        }

        public DamageResult ApplyDamage(DamageRequest request, IStatSnapshot sourceStats = null) =>
            ApplyDamage(in request, sourceStats);

        public HealingResult ApplyHealing(in HealingRequest request)
        {
            IHealingReceiver receiver = directory.TryGetHealingReceiver(request.TargetId);
            int applied = receiver == null ? 0 : receiver.ApplyHealing(request.BaseHealing);
            if (applied < 0) applied = 0;
            if (applied > request.BaseHealing) applied = request.BaseHealing;

            HealingResult result = new HealingResult(
                request.SourceId,
                request.TargetId,
                request.BaseHealing,
                applied);
            if (receiver != null && applied > 0)
            {
                Publish(new HealingConfirmedEvent(
                    eventIds.Next(),
                    clock == null ? 0d : clock.Now,
                    request.SourceId,
                    request.TargetId,
                    result));
            }
            return result;
        }

        public HealingResult ApplyHealing(HealingRequest request) => ApplyHealing(in request);

        public KnockbackImpulse ApplyKnockback(
            in KnockbackRequest request,
            KnockbackResolver knockbackResolver,
            IStatSnapshot targetStats = null)
        {
            if (knockbackResolver == null) throw new ArgumentNullException(nameof(knockbackResolver));
            KnockbackImpulse impulse = knockbackResolver.Resolve(in request, targetStats);
            IKnockbackReceiver receiver = directory.TryGetKnockbackReceiver(request.TargetId);
            if (receiver != null) receiver.ApplyKnockback(in impulse);
            return impulse;
        }

        private void Publish(IGameplayEvent @event)
        {
            if (eventSink != null) eventSink.Publish(@event);
        }
    }
}
