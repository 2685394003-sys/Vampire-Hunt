using System;
using System.Collections.Generic;
using System.Globalization;
using VampireHunt.Core;

namespace VampireHunt.Combat.Contracts
{
    [Flags]
    public enum DamageFlags
    {
        None = 0,
        NoCritical = 1 << 0,
        Periodic = 1 << 1
    }

    /// <summary>A stable, presentation-neutral damage category.</summary>
    public readonly struct DamageTag : IEquatable<DamageTag>
    {
        public string Value { get; }
        public bool IsValid => !string.IsNullOrEmpty(Value);
        public static DamageTag None => default;

        public DamageTag(string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            value = value.Trim();
            if (value.Length == 0)
            {
                Value = string.Empty;
                return;
            }

            Value = value;
        }

        public bool Equals(DamageTag other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is DamageTag other && Equals(other);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(DamageTag left, DamageTag right) => left.Equals(right);
        public static bool operator !=(DamageTag left, DamageTag right) => !left.Equals(right);
    }

    public readonly struct HitContext : IEquatable<HitContext>
    {
        public WorldPosition Position { get; }
        public DamageTag DamageTag { get; }

        public HitContext(WorldPosition position, DamageTag damageTag = default)
        {
            Position = position;
            DamageTag = damageTag;
        }

        public static HitContext None => new HitContext(WorldPosition.Origin, DamageTag.None);
        public bool Equals(HitContext other) => Position == other.Position && DamageTag == other.DamageTag;
        public override bool Equals(object obj) => obj is HitContext other && Equals(other);
        public override int GetHashCode()
        {
            unchecked { return (Position.GetHashCode() * 397) ^ DamageTag.GetHashCode(); }
        }
        public static bool operator ==(HitContext left, HitContext right) => left.Equals(right);
        public static bool operator !=(HitContext left, HitContext right) => !left.Equals(right);
    }

    public readonly struct DamageRequest : IEquatable<DamageRequest>
    {
        public EntityId SourceId { get; }
        public EntityId TargetId { get; }
        public int BaseDamage { get; }
        public DamageFlags Flags { get; }
        public HitContext Hit { get; }

        public DamageRequest(
            EntityId sourceId,
            EntityId targetId,
            int baseDamage,
            DamageFlags flags = DamageFlags.None,
            HitContext hit = default)
        {
            ValidateIds(sourceId, targetId);
            if (baseDamage < 0) throw new ArgumentOutOfRangeException(nameof(baseDamage));
            ValidateFlags(flags);
            SourceId = sourceId;
            TargetId = targetId;
            BaseDamage = baseDamage;
            Flags = flags;
            Hit = hit;
        }

        public bool Equals(DamageRequest other) =>
            SourceId == other.SourceId && TargetId == other.TargetId &&
            BaseDamage == other.BaseDamage && Flags == other.Flags && Hit == other.Hit;
        public override bool Equals(object obj) => obj is DamageRequest other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = SourceId.GetHashCode();
                hash = (hash * 397) ^ TargetId.GetHashCode();
                hash = (hash * 397) ^ BaseDamage;
                hash = (hash * 397) ^ (int)Flags;
                return (hash * 397) ^ Hit.GetHashCode();
            }
        }
        public static bool operator ==(DamageRequest left, DamageRequest right) => left.Equals(right);
        public static bool operator !=(DamageRequest left, DamageRequest right) => !left.Equals(right);

        public static void ValidateFlags(DamageFlags flags)
        {
            const DamageFlags supported = DamageFlags.NoCritical | DamageFlags.Periodic;
            if ((flags & ~supported) != 0)
                throw new ArgumentOutOfRangeException(nameof(flags), "Unknown damage flags.");
        }

        public static void ValidateIds(EntityId sourceId, EntityId targetId)
        {
            if (!sourceId.IsValid) throw new ArgumentException("A valid source id is required.", nameof(sourceId));
            if (!targetId.IsValid) throw new ArgumentException("A valid target id is required.", nameof(targetId));
        }
    }

    public readonly struct HealingRequest : IEquatable<HealingRequest>
    {
        public EntityId SourceId { get; }
        public EntityId TargetId { get; }
        public int BaseHealing { get; }

        public HealingRequest(EntityId sourceId, EntityId targetId, int baseHealing)
        {
            DamageRequest.ValidateIds(sourceId, targetId);
            if (baseHealing < 0) throw new ArgumentOutOfRangeException(nameof(baseHealing));
            SourceId = sourceId;
            TargetId = targetId;
            BaseHealing = baseHealing;
        }

        public bool Equals(HealingRequest other) =>
            SourceId == other.SourceId && TargetId == other.TargetId && BaseHealing == other.BaseHealing;
        public override bool Equals(object obj) => obj is HealingRequest other && Equals(other);
        public override int GetHashCode()
        {
            unchecked { return ((SourceId.GetHashCode() * 397) ^ TargetId.GetHashCode()) * 397 ^ BaseHealing; }
        }
        public static bool operator ==(HealingRequest left, HealingRequest right) => left.Equals(right);
        public static bool operator !=(HealingRequest left, HealingRequest right) => !left.Equals(right);
    }

    public readonly struct ResolvedDamage : IEquatable<ResolvedDamage>
    {
        public EntityId SourceId { get; }
        public EntityId TargetId { get; }
        public int FinalDamage { get; }
        public bool WasCritical { get; }
        public HitContext Hit { get; }

        public ResolvedDamage(
            EntityId sourceId,
            EntityId targetId,
            int finalDamage,
            bool wasCritical,
            HitContext hit = default)
        {
            DamageRequest.ValidateIds(sourceId, targetId);
            if (finalDamage < 0) throw new ArgumentOutOfRangeException(nameof(finalDamage));
            SourceId = sourceId;
            TargetId = targetId;
            FinalDamage = finalDamage;
            WasCritical = wasCritical;
            Hit = hit;
        }

        public bool Equals(ResolvedDamage other) =>
            SourceId == other.SourceId && TargetId == other.TargetId &&
            FinalDamage == other.FinalDamage && WasCritical == other.WasCritical && Hit == other.Hit;
        public override bool Equals(object obj) => obj is ResolvedDamage other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = SourceId.GetHashCode();
                hash = (hash * 397) ^ TargetId.GetHashCode();
                hash = (hash * 397) ^ FinalDamage;
                hash = (hash * 397) ^ WasCritical.GetHashCode();
                return (hash * 397) ^ Hit.GetHashCode();
            }
        }
        public static bool operator ==(ResolvedDamage left, ResolvedDamage right) => left.Equals(right);
        public static bool operator !=(ResolvedDamage left, ResolvedDamage right) => !left.Equals(right);
    }

    public readonly struct DamageResult : IEquatable<DamageResult>
    {
        public int RequestedDamage { get; }
        public int AppliedDamage { get; }
        public bool WasCritical { get; }
        public bool WasKilled { get; }
        public WorldPosition HitPosition { get; }

        public DamageResult(
            int requestedDamage,
            int appliedDamage,
            bool wasCritical,
            bool wasKilled,
            WorldPosition hitPosition)
        {
            if (requestedDamage < 0) throw new ArgumentOutOfRangeException(nameof(requestedDamage));
            if (appliedDamage < 0) throw new ArgumentOutOfRangeException(nameof(appliedDamage));
            RequestedDamage = requestedDamage;
            AppliedDamage = appliedDamage;
            WasCritical = wasCritical;
            WasKilled = wasKilled;
            HitPosition = hitPosition;
        }

        public static DamageResult NoDamage(int requestedDamage, WorldPosition hitPosition = default) =>
            new DamageResult(requestedDamage, 0, false, false, hitPosition);

        public bool Equals(DamageResult other) =>
            RequestedDamage == other.RequestedDamage && AppliedDamage == other.AppliedDamage &&
            WasCritical == other.WasCritical && WasKilled == other.WasKilled &&
            HitPosition == other.HitPosition;
        public override bool Equals(object obj) => obj is DamageResult other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = RequestedDamage;
                hash = (hash * 397) ^ AppliedDamage;
                hash = (hash * 397) ^ WasCritical.GetHashCode();
                hash = (hash * 397) ^ WasKilled.GetHashCode();
                return (hash * 397) ^ HitPosition.GetHashCode();
            }
        }
        public static bool operator ==(DamageResult left, DamageResult right) => left.Equals(right);
        public static bool operator !=(DamageResult left, DamageResult right) => !left.Equals(right);
    }

    public readonly struct HealingResult : IEquatable<HealingResult>
    {
        public EntityId SourceId { get; }
        public EntityId TargetId { get; }
        public int RequestedHealing { get; }
        public int AppliedHealing { get; }

        public HealingResult(EntityId sourceId, EntityId targetId, int requestedHealing, int appliedHealing)
        {
            DamageRequest.ValidateIds(sourceId, targetId);
            if (requestedHealing < 0) throw new ArgumentOutOfRangeException(nameof(requestedHealing));
            if (appliedHealing < 0) throw new ArgumentOutOfRangeException(nameof(appliedHealing));
            SourceId = sourceId;
            TargetId = targetId;
            RequestedHealing = requestedHealing;
            AppliedHealing = appliedHealing;
        }

        public bool Equals(HealingResult other) =>
            SourceId == other.SourceId && TargetId == other.TargetId &&
            RequestedHealing == other.RequestedHealing && AppliedHealing == other.AppliedHealing;
        public override bool Equals(object obj) => obj is HealingResult other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = SourceId.GetHashCode();
                hash = (hash * 397) ^ TargetId.GetHashCode();
                return (hash * 397) ^ RequestedHealing * 397 ^ AppliedHealing;
            }
        }
        public static bool operator ==(HealingResult left, HealingResult right) => left.Equals(right);
        public static bool operator !=(HealingResult left, HealingResult right) => !left.Equals(right);
    }

    public interface IDamageReceiver
    {
        DamageResult ApplyDamage(in ResolvedDamage damage);
    }

    public interface IHealingReceiver
    {
        int ApplyHealing(int amount);
    }

    public interface IKnockbackReceiver
    {
        void ApplyKnockback(in KnockbackImpulse impulse);
    }

    public interface ICombatTarget
    {
        EntityId Id { get; }
        bool IsAlive { get; }
        WorldPosition Position { get; }
    }

    public readonly struct TargetQuery
    {
        public WorldPosition Origin { get; }
        public float Radius { get; }
        public EntityId ExcludedId { get; }
        public bool RequireAlive { get; }

        public TargetQuery(WorldPosition origin, float radius, EntityId excludedId = default, bool requireAlive = true)
        {
            if (radius < 0f || float.IsNaN(radius) || float.IsInfinity(radius))
                throw new ArgumentOutOfRangeException(nameof(radius));
            Origin = origin;
            Radius = radius;
            ExcludedId = excludedId;
            RequireAlive = requireAlive;
        }
    }

    public interface ICombatTargetQuery
    {
        ICombatTarget FindClosest(in TargetQuery query);
        int CollectInArea(in TargetQuery query, IList<ICombatTarget> buffer);
    }

    public interface ICombatEntityDirectory
    {
        IDamageReceiver TryGetDamageReceiver(EntityId id);
        IHealingReceiver TryGetHealingReceiver(EntityId id);
        IKnockbackReceiver TryGetKnockbackReceiver(EntityId id);
    }

    public readonly struct KnockbackRequest : IEquatable<KnockbackRequest>
    {
        public EntityId SourceId { get; }
        public EntityId TargetId { get; }
        public WorldPosition Direction { get; }
        public float Force { get; }
        public float Duration { get; }

        public KnockbackRequest(
            EntityId sourceId,
            EntityId targetId,
            WorldPosition direction,
            float force,
            float duration)
        {
            DamageRequest.ValidateIds(sourceId, targetId);
            ValidateNonNegativeFinite(force, nameof(force));
            ValidateNonNegativeFinite(duration, nameof(duration));
            SourceId = sourceId;
            TargetId = targetId;
            Direction = direction;
            Force = force;
            Duration = duration;
        }

        public bool Equals(KnockbackRequest other) =>
            SourceId == other.SourceId && TargetId == other.TargetId &&
            Direction == other.Direction && Force.Equals(other.Force) && Duration.Equals(other.Duration);
        public override bool Equals(object obj) => obj is KnockbackRequest other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = SourceId.GetHashCode();
                hash = (hash * 397) ^ TargetId.GetHashCode();
                hash = (hash * 397) ^ Direction.GetHashCode();
                hash = (hash * 397) ^ Force.GetHashCode();
                return (hash * 397) ^ Duration.GetHashCode();
            }
        }
        public static bool operator ==(KnockbackRequest left, KnockbackRequest right) => left.Equals(right);
        public static bool operator !=(KnockbackRequest left, KnockbackRequest right) => !left.Equals(right);

        private static void ValidateNonNegativeFinite(float value, string name)
        {
            if (value < 0f || float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(name);
        }
    }

    public readonly struct KnockbackImpulse : IEquatable<KnockbackImpulse>
    {
        public EntityId SourceId { get; }
        public EntityId TargetId { get; }
        public WorldPosition Direction { get; }
        public float Force { get; }
        public float Duration { get; }
        public bool IsImmune { get; }

        public KnockbackImpulse(
            EntityId sourceId,
            EntityId targetId,
            WorldPosition direction,
            float force,
            float duration,
            bool isImmune)
        {
            DamageRequest.ValidateIds(sourceId, targetId);
            if (force < 0f || float.IsNaN(force) || float.IsInfinity(force))
                throw new ArgumentOutOfRangeException(nameof(force));
            if (duration < 0f || float.IsNaN(duration) || float.IsInfinity(duration))
                throw new ArgumentOutOfRangeException(nameof(duration));
            SourceId = sourceId;
            TargetId = targetId;
            Direction = direction;
            Force = force;
            Duration = duration;
            IsImmune = isImmune;
        }

        public bool Equals(KnockbackImpulse other) =>
            SourceId == other.SourceId && TargetId == other.TargetId && Direction == other.Direction &&
            Force.Equals(other.Force) && Duration.Equals(other.Duration) && IsImmune == other.IsImmune;
        public override bool Equals(object obj) => obj is KnockbackImpulse other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = SourceId.GetHashCode();
                hash = (hash * 397) ^ TargetId.GetHashCode();
                hash = (hash * 397) ^ Direction.GetHashCode();
                hash = (hash * 397) ^ Force.GetHashCode();
                hash = (hash * 397) ^ Duration.GetHashCode();
                return (hash * 397) ^ IsImmune.GetHashCode();
            }
        }
        public static bool operator ==(KnockbackImpulse left, KnockbackImpulse right) => left.Equals(right);
        public static bool operator !=(KnockbackImpulse left, KnockbackImpulse right) => !left.Equals(right);
    }

    public sealed class DamageConfirmedEvent : GameplayEventBase
    {
        public DamageResult Result { get; }
        public EntityId SourceId { get; }
        public EntityId TargetId { get; }
        public DamageFlags Flags { get; }
        public HitContext Hit { get; }

        public DamageConfirmedEvent(
            ulong eventId,
            double occurredAt,
            EntityId sourceId,
            EntityId targetId,
            DamageResult result,
            DamageFlags flags = DamageFlags.None,
            HitContext hit = default)
            : base(eventId, occurredAt)
        {
            DamageRequest.ValidateIds(sourceId, targetId);
            DamageRequest.ValidateFlags(flags);
            Result = result;
            SourceId = sourceId;
            TargetId = targetId;
            Flags = flags;
            Hit = hit;
        }

        public DamageConfirmedEvent(EntityId sourceId, EntityId targetId, DamageResult result)
            : this(0UL, 0d, sourceId, targetId, result) { }
    }

    public sealed class HealingConfirmedEvent : GameplayEventBase
    {
        public int AppliedHealing { get; }
        public EntityId SourceId { get; }
        public EntityId TargetId { get; }
        public HealingResult Result { get; }

        public HealingConfirmedEvent(
            ulong eventId,
            double occurredAt,
            EntityId sourceId,
            EntityId targetId,
            HealingResult result)
            : base(eventId, occurredAt)
        {
            DamageRequest.ValidateIds(sourceId, targetId);
            AppliedHealing = result.AppliedHealing;
            SourceId = sourceId;
            TargetId = targetId;
            Result = result;
        }

        public HealingConfirmedEvent(EntityId sourceId, EntityId targetId, int appliedHealing)
            : this(0UL, 0d, sourceId, targetId,
                new HealingResult(sourceId, targetId, appliedHealing, appliedHealing)) { }
    }

    public sealed class EntityDiedEvent : GameplayEventBase
    {
        public EntityId EntityId { get; }
        public EntityId KillerId { get; }
        public WorldPosition Position { get; }

        public EntityDiedEvent(
            ulong eventId,
            double occurredAt,
            EntityId entityId,
            EntityId killerId,
            WorldPosition position = default)
            : base(eventId, occurredAt)
        {
            if (!entityId.IsValid) throw new ArgumentException("A valid entity id is required.", nameof(entityId));
            if (!killerId.IsValid) throw new ArgumentException("A valid killer id is required.", nameof(killerId));
            EntityId = entityId;
            KillerId = killerId;
            Position = position;
        }

        public EntityDiedEvent(EntityId entityId, EntityId killerId)
            : this(0UL, 0d, entityId, killerId) { }
    }
}
