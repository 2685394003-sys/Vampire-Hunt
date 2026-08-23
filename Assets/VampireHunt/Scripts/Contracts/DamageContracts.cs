using System;
using VampireHunt.SharedKernel;

namespace VampireHunt.Contracts
{
    [Flags]
    public enum DamageTags : uint
    {
        None = 0,
        Melee = 1 << 0,
        Projectile = 1 << 1,
        SwordWave = 1 << 2,
        Critical = 1 << 3,
        Status = 1 << 4,
        Pact = 1 << 5,
        Periodic = 1 << 6
    }

    /// <summary>Pure combat input. Unity hit data stays in the adapter layer.</summary>
    public readonly struct DamageRequest
    {
        public EntityId Source { get; }
        public EntityId Target { get; }
        public uint AttackId { get; }
        public ulong Sequence { get; }
        public float BaseDamage { get; }
        public DamageTags Tags { get; }

        public DamageRequest(
            EntityId source,
            EntityId target,
            uint attackId,
            ulong sequence,
            float baseDamage,
            DamageTags tags)
        {
            Source = source;
            Target = target;
            AttackId = attackId;
            Sequence = sequence;
            BaseDamage = Math.Max(0f, baseDamage);
            Tags = tags;
        }
    }

    public readonly struct ResolvedDamage
    {
        public DamageRequest Request { get; }
        public float Amount { get; }
        public DamageTags Tags { get; }
        public bool IsCancelled { get; }

        public ResolvedDamage(DamageRequest request, float amount, DamageTags tags, bool isCancelled)
        {
            Request = request;
            Amount = Math.Max(0f, amount);
            Tags = tags;
            IsCancelled = isCancelled;
        }
    }

    /// <summary>
    /// Mutable resolution context. Blood pacts can add implementations of
    /// IDamageModifier without coupling Combat to the Progression module.
    /// </summary>
    public sealed class DamageContext
    {
        public DamageRequest Request { get; }
        public float Amount { get; set; }
        public DamageTags Tags { get; set; }
        public bool IsCancelled { get; set; }

        public DamageContext(DamageRequest request)
        {
            Request = request;
            Amount = request.BaseDamage;
            Tags = request.Tags;
        }

        public ResolvedDamage ToResult()
        {
            return new ResolvedDamage(Request, Amount, Tags, IsCancelled);
        }
    }

    public interface IDamageModifier
    {
        int Priority { get; }
        void Modify(DamageContext context);
    }

    public interface ICombatOutcomeListener
    {
        void OnDamageResolved(in ResolvedDamage damage);
    }

    public interface ICombatModifierTarget
    {
        bool RegisterOutgoingModifier(IDamageModifier modifier);
        bool UnregisterOutgoingModifier(IDamageModifier modifier);
        bool RegisterIncomingModifier(IDamageModifier modifier);
        bool UnregisterIncomingModifier(IDamageModifier modifier);
        bool RegisterOutcomeListener(ICombatOutcomeListener listener);
        bool UnregisterOutcomeListener(ICombatOutcomeListener listener);
    }

    public interface IDamageReceiver
    {
        bool TryApplyDamage(in DamageRequest request, out ResolvedDamage result);
    }

    /// <summary>Trusted client-side hit result carried to the authoritative target adapter.</summary>
    public readonly struct TrustedCombatHit
    {
        public DamageRequest Damage { get; }
        public ElementId Element { get; }
        public StatusEffectSpec[] Statuses { get; }
        public Float3 ImpactForce { get; }

        public TrustedCombatHit(
            in DamageRequest damage,
            ElementId element,
            StatusEffectSpec[] statuses,
            in Float3 impactForce)
        {
            Damage = damage;
            Element = element;
            Statuses = statuses ?? Array.Empty<StatusEffectSpec>();
            ImpactForce = impactForce;
        }
    }

    public interface ITrustedCombatHitTarget
    {
        bool SubmitTrustedHit(in TrustedCombatHit hit);
    }
}
