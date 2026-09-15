using System;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;

namespace VampireHunt.Infrastructure.Netcode
{
    public struct StatusEffectNetworkSpec : INetworkSerializable, IEquatable<StatusEffectNetworkSpec>
    {
        public uint StatusId;
        public int Stacks;
        public float Duration;
        public float Magnitude;
        public byte Element;

        public StatusEffectNetworkSpec(in StatusEffectSpec spec)
        {
            StatusId = spec.StatusId;
            Stacks = spec.Stacks;
            Duration = spec.Duration;
            Magnitude = spec.Magnitude;
            Element = (byte)spec.Element;
        }

        public StatusEffectSpec ToDomain() =>
            new StatusEffectSpec(StatusId, Stacks, Duration, Magnitude, (ElementId)Element);

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref StatusId);
            serializer.SerializeValue(ref Stacks);
            serializer.SerializeValue(ref Duration);
            serializer.SerializeValue(ref Magnitude);
            serializer.SerializeValue(ref Element);
        }

        public bool Equals(StatusEffectNetworkSpec other) =>
            StatusId == other.StatusId && Stacks == other.Stacks && Duration.Equals(other.Duration) &&
            Magnitude.Equals(other.Magnitude) && Element == other.Element;
    }

    public struct StatusEffectNetworkBundle : INetworkSerializable, IEquatable<StatusEffectNetworkBundle>
    {
        public byte Count;
        public StatusEffectNetworkSpec Effect0;
        public StatusEffectNetworkSpec Effect1;
        public StatusEffectNetworkSpec Effect2;
        public StatusEffectNetworkSpec Effect3;

        public static StatusEffectNetworkBundle FromPlan(AbilityCastPlan plan)
        {
            var result = new StatusEffectNetworkBundle
            {
                Count = (byte)Math.Min(4, plan?.OnHitStatuses.Count ?? 0)
            };
            for (int i = 0; i < result.Count; i++) result.Set(i, new StatusEffectNetworkSpec(plan.OnHitStatuses[i]));
            return result;
        }

        public StatusEffectNetworkSpec Get(int index)
        {
            switch (index)
            {
                case 0: return Effect0;
                case 1: return Effect1;
                case 2: return Effect2;
                case 3: return Effect3;
                default: return default;
            }
        }

        private void Set(int index, StatusEffectNetworkSpec value)
        {
            switch (index)
            {
                case 0: Effect0 = value; break;
                case 1: Effect1 = value; break;
                case 2: Effect2 = value; break;
                case 3: Effect3 = value; break;
            }
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Count);
            serializer.SerializeValue(ref Effect0);
            serializer.SerializeValue(ref Effect1);
            serializer.SerializeValue(ref Effect2);
            serializer.SerializeValue(ref Effect3);
        }

        public bool Equals(StatusEffectNetworkBundle other) =>
            Count == other.Count && Effect0.Equals(other.Effect0) && Effect1.Equals(other.Effect1) &&
            Effect2.Equals(other.Effect2) && Effect3.Equals(other.Effect3);
    }

    public struct AbilityCastNetworkMessage : INetworkSerializable, IEquatable<AbilityCastNetworkMessage>
    {
        public uint AbilityId;
        public ulong Sequence;
        public Vector3 Origin;
        public Vector3 Direction;
        public float Damage;
        public float TravelDistance;
        public float ProjectileSpeed;
        public float Knockback;
        public float ProjectileSize;
        public int ProjectileCount;
        public int PierceCount;
        public float SpreadAngle;
        public uint Tags;
        public byte Element;
        public StatusEffectNetworkBundle OnHitStatuses;

        public static AbilityCastNetworkMessage FromPlan(AbilityCastPlan plan)
        {
            return new AbilityCastNetworkMessage
            {
                AbilityId = plan.AbilityId,
                Sequence = plan.Sequence,
                Origin = new Vector3(plan.Origin.X, plan.Origin.Y, plan.Origin.Z),
                Direction = new Vector3(plan.Direction.X, plan.Direction.Y, plan.Direction.Z),
                Damage = plan.Damage,
                TravelDistance = plan.TravelDistance,
                ProjectileSpeed = plan.ProjectileSpeed,
                Knockback = plan.Knockback,
                ProjectileSize = plan.ProjectileSize,
                ProjectileCount = plan.ProjectileCount,
                PierceCount = plan.PierceCount,
                SpreadAngle = plan.SpreadAngle,
                Tags = (uint)plan.Tags,
                Element = (byte)plan.Element,
                OnHitStatuses = StatusEffectNetworkBundle.FromPlan(plan)
            };
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref AbilityId);
            serializer.SerializeValue(ref Sequence);
            serializer.SerializeValue(ref Origin);
            serializer.SerializeValue(ref Direction);
            serializer.SerializeValue(ref Damage);
            serializer.SerializeValue(ref TravelDistance);
            serializer.SerializeValue(ref ProjectileSpeed);
            serializer.SerializeValue(ref Knockback);
            serializer.SerializeValue(ref ProjectileSize);
            serializer.SerializeValue(ref ProjectileCount);
            serializer.SerializeValue(ref PierceCount);
            serializer.SerializeValue(ref SpreadAngle);
            serializer.SerializeValue(ref Tags);
            serializer.SerializeValue(ref Element);
            serializer.SerializeValue(ref OnHitStatuses);
        }

        public bool Equals(AbilityCastNetworkMessage other) =>
            AbilityId == other.AbilityId && Sequence == other.Sequence && Origin.Equals(other.Origin) &&
            Direction.Equals(other.Direction) && Damage.Equals(other.Damage) &&
            TravelDistance.Equals(other.TravelDistance) && ProjectileSpeed.Equals(other.ProjectileSpeed) &&
            Knockback.Equals(other.Knockback) && ProjectileSize.Equals(other.ProjectileSize) &&
            ProjectileCount == other.ProjectileCount && PierceCount == other.PierceCount &&
            SpreadAngle.Equals(other.SpreadAngle) && Tags == other.Tags && Element == other.Element &&
            OnHitStatuses.Equals(other.OnHitStatuses);
    }

    public struct TrustedCombatHitNetworkMessage : INetworkSerializable, IEquatable<TrustedCombatHitNetworkMessage>
    {
        public uint AttackId;
        public ulong Sequence;
        public float Damage;
        public uint Tags;
        public byte Element;
        public Vector3 ImpactForce;
        public StatusEffectNetworkBundle Statuses;

        public static TrustedCombatHitNetworkMessage FromDomain(in TrustedCombatHit hit)
        {
            var bundle = new StatusEffectNetworkBundle
            {
                Count = (byte)Math.Min(4, hit.Statuses?.Length ?? 0)
            };
            // Reuse the bundle's network representation through a small plan.
            var plan = new AbilityCastPlan();
            for (int i = 0; i < bundle.Count; i++) plan.OnHitStatuses.Add(hit.Statuses[i]);
            bundle = StatusEffectNetworkBundle.FromPlan(plan);
            return new TrustedCombatHitNetworkMessage
            {
                AttackId = hit.Damage.AttackId,
                Sequence = hit.Damage.Sequence,
                Damage = hit.Damage.BaseDamage,
                Tags = (uint)hit.Damage.Tags,
                Element = (byte)hit.Element,
                ImpactForce = new Vector3(hit.ImpactForce.X, hit.ImpactForce.Y, hit.ImpactForce.Z),
                Statuses = bundle
            };
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref AttackId);
            serializer.SerializeValue(ref Sequence);
            serializer.SerializeValue(ref Damage);
            serializer.SerializeValue(ref Tags);
            serializer.SerializeValue(ref Element);
            serializer.SerializeValue(ref ImpactForce);
            serializer.SerializeValue(ref Statuses);
        }

        public bool Equals(TrustedCombatHitNetworkMessage other) =>
            AttackId == other.AttackId && Sequence == other.Sequence && Damage.Equals(other.Damage) &&
            Tags == other.Tags && Element == other.Element && ImpactForce.Equals(other.ImpactForce) &&
            Statuses.Equals(other.Statuses);
    }
}
