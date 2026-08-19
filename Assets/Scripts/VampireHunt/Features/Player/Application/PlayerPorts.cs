using System.Collections.Generic;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;
using VampireHunt.Player.Contracts;
using VampireHunt.Player.Domain;

namespace VampireHunt.Player.Application
{
    public interface IPlayerRepository
    {
        PlayerAggregate Get(EntityId playerId);
        bool TryGet(EntityId playerId, out PlayerAggregate player);
        void GetAllAlive(ICollection<PlayerAggregate> buffer);
    }

    public interface IPlayerOwnership
    {
        bool IsOwner(EntityId playerId, ulong senderId);
    }

    public interface IMovementCorrector
    {
        void ForcePose(EntityId playerId, MovementPose pose);
    }

    public interface IMovementState
    {
        bool TryGetLastAcceptedPose(EntityId playerId, out MovementPose pose);
        void CommitAcceptedPose(EntityId playerId, MovementPose pose);
    }

    public interface IMovementWorldQuery
    {
        bool IsInsideBounds(WorldPosition position);
        bool IsPathClear(WorldPosition from, WorldPosition to);
    }

    public interface IMovementClock
    {
        double Now { get; }
    }

    public readonly struct MovementValidationOptions
    {
        public float MaximumSpeed { get; }
        public float DashSpeedMultiplier { get; }
        public double MaximumReportAge { get; }
        public double MaximumFutureSkew { get; }

        public MovementValidationOptions(
            float maximumSpeed,
            float dashSpeedMultiplier = 1f,
            double maximumReportAge = 0.25d,
            double maximumFutureSkew = 0.1d)
        {
            MaximumSpeed = maximumSpeed < 0f ? 0f : maximumSpeed;
            DashSpeedMultiplier = dashSpeedMultiplier < 1f ? 1f : dashSpeedMultiplier;
            MaximumReportAge = maximumReportAge < 0d ? 0d : maximumReportAge;
            MaximumFutureSkew = maximumFutureSkew < 0d ? 0d : maximumFutureSkew;
        }
    }

    public interface IMeleeHitTarget
    {
        EntityId Id { get; }
        bool IsAlive { get; }
        WorldPosition HitPosition { get; }
    }

    public readonly struct MeleeHitQuery
    {
        public EntityId SourceId { get; }
        public WorldPosition Origin { get; }
        public WorldPosition AimAt { get; }
        public float Range { get; }
        public float ConeAngleDegrees { get; }

        public MeleeHitQuery(
            EntityId sourceId,
            WorldPosition origin,
            WorldPosition aimAt,
            float range,
            float coneAngleDegrees)
        {
            SourceId = sourceId;
            Origin = origin;
            AimAt = aimAt;
            Range = range < 0f ? 0f : range;
            ConeAngleDegrees = coneAngleDegrees < 0f ? 0f : coneAngleDegrees;
        }
    }

    public interface IMeleeHitQuery
    {
        void CollectUniqueTargets(MeleeHitQuery query, ICollection<IMeleeHitTarget> buffer);
    }

    public interface IPlayerPositionQuery
    {
        bool TryGetPosition(EntityId playerId, out WorldPosition position);
    }

    public readonly struct PlayerAttackRequest
    {
        public EntityId SourceId { get; }
        public EntityId TargetId { get; }
        public int BaseDamage { get; }
        public WorldPosition AimAt { get; }
        public WorldPosition HitPosition { get; }

        public PlayerAttackRequest(
            EntityId sourceId,
            EntityId targetId,
            int baseDamage,
            WorldPosition aimAt,
            WorldPosition hitPosition)
        {
            SourceId = sourceId;
            TargetId = targetId;
            BaseDamage = baseDamage < 0 ? 0 : baseDamage;
            AimAt = aimAt;
            HitPosition = hitPosition;
        }
    }

    /// <summary>
    /// Integration seam for CombatApplicationService. Implementations resolve one
    /// target at a time, so critical and actual damage stay target-local.
    /// </summary>
    public interface IPlayerDamageResolver
    {
        DamageResult Apply(PlayerAttackRequest request);
    }

    public interface IPlayerDeathSink
    {
        void Publish(PlayerDeathEvent deathEvent);
    }

    public interface IBloodPactCatalog
    {
        int Count { get; }
        bool TryGet(BloodPactId id, out BloodPactOption option);
        void CopyOptions(ICollection<BloodPactOption> buffer);
    }

    public interface IPlayerRandom
    {
        int NextInt(int minimumInclusive, int maximumExclusive);
    }
}
