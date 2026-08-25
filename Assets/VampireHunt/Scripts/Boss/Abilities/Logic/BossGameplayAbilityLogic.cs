using System;
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
