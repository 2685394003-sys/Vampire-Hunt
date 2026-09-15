using System;
using System.Collections.Generic;
using VampireHunt.Contracts;
using VampireHunt.SharedKernel;

namespace VampireHunt.Boss.Abilities.Logic
{
    /// <summary>Reusable server-only plumbing for authored Boss ability scripts.</summary>
    public abstract class BossGameplayAbilityLogic : IBossAbilityLogicRuntime,
        IBossAbilityServiceConsumer, IBossAbilityTuningConsumer
    {
        private readonly BossPlayerTarget[] m_Hits = new BossPlayerTarget[64];
        private ulong m_HitOrdinal;

        protected BossAbilityServices Services { get; private set; } = BossAbilityServices.Empty;
        protected BossAbilityTuning Tuning { get; private set; } = new BossAbilityTuning();
        protected BossAbilityCastContext Context { get; private set; }
        protected BossAbilityCastPhase Phase { get; private set; }

        public void BindServices(BossAbilityServices services) =>
            Services = services ?? BossAbilityServices.Empty;

        public void BindTuning(BossAbilityTuning tuning) =>
            Tuning = (tuning ?? new BossAbilityTuning()).CloneValidated();

        public virtual void OnCastStarted(in BossAbilityCastContext context)
        {
            Context = context;
            // Tuning is a factory-created, cast-local clone. Scaling it here never mutates
            // the ScriptableObject and gives every damage path the same authoritative value.
            Tuning.Damage *= context.Modifiers.DamageMultiplier;
            Phase = BossAbilityCastPhase.Telegraph;
            m_HitOrdinal = 0;
        }

        public virtual void OnPhaseEntered(BossAbilityCastPhase phase, double serverTime)
        {
            Phase = phase;
            if (phase == BossAbilityCastPhase.Resolve) Resolve(serverTime);
        }

        public virtual void Tick(double serverTime) { }
        public virtual void Cancel(double serverTime) { }
        public virtual void Dispose() { }
        protected virtual void Resolve(double serverTime) { }

        protected int QuerySphere(in Float3 center, float radius) =>
            Services.HitQuery?.Query(BossHitQueryRequest.Sphere(center, radius), m_Hits) ?? 0;

        protected int QueryBox(in Float3 center, in Float3 forward, in Float3 halfExtents) =>
            Services.HitQuery?.Query(BossHitQueryRequest.Box(center, forward, halfExtents), m_Hits) ?? 0;

        protected void DamageHits(int count, in Float3 forceDirection)
        {
            EntityId source = Services.BossBodyState?.EntityId ?? EntityId.None;
            Float3 impulse = forceDirection.Normalized() * Tuning.Knockback;
            for (int i = 0; i < count && i < m_Hits.Length; i++)
            {
                var request = new DamageRequest(
                    source,
                    m_Hits[i].EntityId,
                    Context.AbilityId,
                    (Context.CastSequence << 16) | ++m_HitOrdinal,
                    Tuning.Damage,
                    DamageTags.Melee);
                Services.DamageService?.TryApply(new BossDamageApplication(request, impulse), out _);
            }
        }

        protected void DamageHitsOnce(int count, ISet<ulong> damagedTargets, in Float3 forceDirection)
        {
            if (damagedTargets == null) return;
            EntityId source = Services.BossBodyState?.EntityId ?? EntityId.None;
            Float3 impulse = forceDirection.Normalized() * Tuning.Knockback;
            for (int i = 0; i < count && i < m_Hits.Length; i++)
            {
                BossPlayerTarget target = m_Hits[i];
                if (!damagedTargets.Add(target.EntityId.Value)) continue;
                var request = new DamageRequest(source, target.EntityId, Context.AbilityId,
                    (Context.CastSequence << 16) | ++m_HitOrdinal, Tuning.Damage, DamageTags.Melee);
                Services.DamageService?.TryApply(new BossDamageApplication(request, impulse), out _);
            }
        }

        /// <summary>
        /// Applies damage to every queried target on its own cooldown. This is intended for
        /// persistent volumes such as a laser: entering later still hits immediately, while
        /// remaining inside cannot hit faster than the authored interval.
        /// </summary>
        protected void DamageHitsWithPerTargetCooldown(
            int count,
            IDictionary<ulong, double> nextAllowedTimes,
            double serverTime,
            float interval,
            in Float3 forceDirection)
        {
            if (nextAllowedTimes == null) return;
            EntityId source = Services.BossBodyState?.EntityId ?? EntityId.None;
            Float3 impulse = forceDirection.Normalized() * Tuning.Knockback;
            double cooldown = Math.Max(.01d, interval);
            for (int i = 0; i < count && i < m_Hits.Length; i++)
            {
                BossPlayerTarget target = m_Hits[i];
                ulong targetId = target.EntityId.Value;
                if (nextAllowedTimes.TryGetValue(targetId, out double nextAllowed) &&
                    serverTime + .000001d < nextAllowed) continue;

                nextAllowedTimes[targetId] = serverTime + cooldown;
                var request = new DamageRequest(source, target.EntityId, Context.AbilityId,
                    (Context.CastSequence << 16) | ++m_HitOrdinal, Tuning.Damage, DamageTags.Melee);
                Services.DamageService?.TryApply(new BossDamageApplication(request, impulse), out _);
            }
        }

        protected void DamageHitsRadially(int count, in Float3 center)
        {
            EntityId source = Services.BossBodyState?.EntityId ?? EntityId.None;
            for (int i = 0; i < count && i < m_Hits.Length; i++)
            {
                Float3 direction = Direction(center, m_Hits[i].Position);
                var request = new DamageRequest(source, m_Hits[i].EntityId, Context.AbilityId,
                    (Context.CastSequence << 16) | ++m_HitOrdinal, Tuning.Damage, DamageTags.Projectile);
                Services.DamageService?.TryApply(
                    new BossDamageApplication(request, direction * Tuning.Knockback), out _);
            }
        }

        protected static Float3 Add(in Float3 left, in Float3 right) => left + right;
        protected static Float3 Scale(in Float3 value, float scalar) => value * scalar;
        protected static Float3 Direction(in Float3 from, in Float3 to) =>
            new Float3(to.X - from.X, to.Y - from.Y, to.Z - from.Z).Normalized();

        protected static Float3 RotateY(in Float3 direction, float degrees)
        {
            double radians = degrees * Math.PI / 180d;
            float sin = (float)Math.Sin(radians);
            float cos = (float)Math.Cos(radians);
            return new Float3(
                direction.X * cos + direction.Z * sin,
                direction.Y,
                -direction.X * sin + direction.Z * cos).Normalized();
        }
    }
}
