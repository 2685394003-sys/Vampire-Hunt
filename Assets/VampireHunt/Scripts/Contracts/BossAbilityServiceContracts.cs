using System;
using VampireHunt.SharedKernel;

namespace VampireHunt.Contracts
{
    /// <summary>Read-only player data returned to Boss decision and ability code.</summary>
    public readonly struct BossPlayerTarget
    {
        public EntityId EntityId { get; }
        public Float3 Position { get; }
        /// <summary>玩家普通（走路）移动速度，米/秒。Boss 用它决定移动速度，好让玩家疾跑/冲刺时能追上。</summary>
        public float NormalMoveSpeed { get; }

        public BossPlayerTarget(EntityId entityId, in Float3 position, float normalMoveSpeed)
        {
            EntityId = entityId;
            Position = position;
            NormalMoveSpeed = normalMoveSpeed;
        }
    }

    /// <summary>Server-side player lookup. Network client discovery stays behind its adapter.</summary>
    public interface IPlayerTargetQuery
    {
        int QueryAlivePlayers(BossPlayerTarget[] results);
        bool TryGetNearest(in Float3 origin, float maxDistance, out BossPlayerTarget target);
        bool TryResolve(EntityId entityId, out BossPlayerTarget target);
    }

    public enum BossHitShape : byte
    {
        Sphere = 0,
        Box = 1
    }

    /// <summary>
    /// Unity-free description of an ability damage volume. Sphere covers radial attacks;
    /// Box covers sweeps, lanes, grids and laser segments.
    /// </summary>
    public readonly struct BossHitQueryRequest
    {
        public BossHitShape Shape { get; }
        public Float3 Center { get; }
        public Float3 Forward { get; }
        public Float3 HalfExtents { get; }
        public float Radius { get; }

        private BossHitQueryRequest(
            BossHitShape shape,
            in Float3 center,
            in Float3 forward,
            in Float3 halfExtents,
            float radius)
        {
            Shape = shape;
            Center = center;
            Forward = forward.Normalized();
            HalfExtents = halfExtents;
            Radius = Math.Max(0f, radius);
        }

        public static BossHitQueryRequest Sphere(in Float3 center, float radius) =>
            new BossHitQueryRequest(BossHitShape.Sphere, center, Float3.Zero, Float3.Zero, radius);

        public static BossHitQueryRequest Box(
            in Float3 center,
            in Float3 forward,
            in Float3 halfExtents) =>
            new BossHitQueryRequest(BossHitShape.Box, center, forward, halfExtents, 0f);
    }

    /// <summary>Returns unique, alive player entities touching an authored ability volume.</summary>
    public interface IBossHitQuery
    {
        int Query(in BossHitQueryRequest request, BossPlayerTarget[] results);
    }

    public readonly struct BossDamageApplication
    {
        public DamageRequest Damage { get; }
        public Float3 ImpactForce { get; }

        public BossDamageApplication(in DamageRequest damage, in Float3 impactForce)
        {
            Damage = damage;
            ImpactForce = impactForce;
        }
    }

    /// <summary>Boss-facing adapter over authoritative player damage and knockback ports.</summary>
    public interface IBossDamageService
    {
        bool TryApply(in DamageRequest request, out ResolvedDamage result);
        bool TryApply(in BossDamageApplication application, out ResolvedDamage result);
    }

    public readonly struct BossProjectileSpawnRequest
    {
        public uint ProjectileId { get; }
        public EntityId Source { get; }
        public ulong Sequence { get; }
        public Float3 Origin { get; }
        public Float3 Direction { get; }
        public float Damage { get; }
        public float Speed { get; }
        public float Scale { get; }
        public DamageTags Tags { get; }

        public BossProjectileSpawnRequest(
            uint projectileId,
            EntityId source,
            ulong sequence,
            in Float3 origin,
            in Float3 direction,
            float damage,
            float speed,
            float scale,
            DamageTags tags)
        {
            ProjectileId = projectileId;
            Source = source;
            Sequence = sequence;
            Origin = origin;
            Direction = direction.Normalized();
            Damage = Math.Max(0f, damage);
            Speed = Math.Max(0.1f, speed);
            Scale = Math.Max(0.01f, scale);
            Tags = tags;
        }
    }

    /// <summary>Spawns one server-owned network projectile from a prefab catalog entry.</summary>
    public interface IBossProjectileSpawner
    {
        bool TrySpawn(in BossProjectileSpawnRequest request, out ulong networkObjectId);
    }

    /// <summary>
    /// Optional component on a projectile prefab. It receives the pure Boss payload
    /// before the NetworkObject is spawned, so projectile hit logic can stay modular.
    /// </summary>
    public interface IBossProjectilePayloadReceiver
    {
        void ConfigureBossProjectile(in BossProjectileSpawnRequest request);
    }

    public enum BossAreaTelegraphShape : byte
    {
        Sphere = 0,
        Box = 1
    }

    /// <summary>
    /// Immutable, server-authored snapshot of world-space area warnings for one cast.
    /// Centers are captured once and deliberately never follow their original targets.
    /// </summary>
    public readonly struct BossAreaTelegraphRequest
    {
        public uint AbilityId { get; }
        public ulong CastSequence { get; }
        public double StartServerTime { get; }
        public BossAreaTelegraphShape Shape { get; }
        public float Radius { get; }
        public Float3 Forward { get; }
        public Float3 Size { get; }
        public Float3[] Centers { get; }

        public BossAreaTelegraphRequest(
            uint abilityId,
            ulong castSequence,
            double startServerTime,
            float radius,
            Float3[] centers)
        {
            AbilityId = abilityId;
            CastSequence = castSequence;
            StartServerTime = startServerTime;
            Shape = BossAreaTelegraphShape.Sphere;
            Radius = Math.Max(0f, radius);
            Forward = Float3.Zero;
            Size = Float3.Zero;
            Centers = centers ?? Array.Empty<Float3>();
        }

        private BossAreaTelegraphRequest(
            uint abilityId,
            ulong castSequence,
            double startServerTime,
            BossAreaTelegraphShape shape,
            float radius,
            in Float3 forward,
            in Float3 size,
            Float3[] centers)
        {
            AbilityId = abilityId;
            CastSequence = castSequence;
            StartServerTime = startServerTime;
            Shape = shape;
            Radius = Math.Max(0f, radius);
            Forward = forward.Normalized();
            Size = new Float3(Math.Max(.01f, size.X), Math.Max(.01f, size.Y), Math.Max(.01f, size.Z));
            Centers = centers ?? Array.Empty<Float3>();
        }

        public static BossAreaTelegraphRequest Box(
            uint abilityId,
            ulong castSequence,
            double startServerTime,
            in Float3 center,
            in Float3 forward,
            in Float3 size) =>
            new BossAreaTelegraphRequest(
                abilityId,
                castSequence,
                startServerTime,
                BossAreaTelegraphShape.Box,
                0f,
                forward,
                size,
                new[] { center });
    }

    /// <summary>
    /// Server-to-client presentation port for abilities that create several fixed world areas.
    /// Damage remains in ability logic; this service only replicates presentation snapshots.
    /// </summary>
    public interface IBossAreaTelegraphService
    {
        bool TryPublish(in BossAreaTelegraphRequest request);
        bool TryCancel(uint abilityId, ulong castSequence);
    }

    public enum BossSweepDirection : byte
    {
        LeftToRight = 0,
        RightToLeft = 1
    }

    /// <summary>One server-authored sweep pass. Its position and direction never follow a player.</summary>
    public readonly struct BossSweepTelegraphPass
    {
        public uint PassIndex { get; }
        public double StartServerTime { get; }
        public Float3 Center { get; }
        public Float3 Forward { get; }
        public Float3 Size { get; }
        public BossSweepDirection Direction { get; }

        public BossSweepTelegraphPass(
            uint passIndex,
            double startServerTime,
            in Float3 center,
            in Float3 forward,
            in Float3 size,
            BossSweepDirection direction)
        {
            PassIndex = passIndex;
            StartServerTime = startServerTime;
            Center = center;
            Forward = forward.Normalized();
            Size = new Float3(
                Math.Max(.01f, size.X),
                Math.Max(.01f, size.Y),
                Math.Max(.01f, size.Z));
            Direction = direction;
        }
    }

    public readonly struct BossSweepTelegraphRequest
    {
        public uint AbilityId { get; }
        public ulong CastSequence { get; }
        public BossSweepTelegraphPass[] Passes { get; }

        public BossSweepTelegraphRequest(
            uint abilityId,
            ulong castSequence,
            BossSweepTelegraphPass[] passes)
        {
            AbilityId = abilityId;
            CastSequence = castSequence;
            Passes = passes ?? Array.Empty<BossSweepTelegraphPass>();
        }
    }

    /// <summary>Network presentation port dedicated to multi-pass hand sweeps.</summary>
    public interface IBossSweepTelegraphService
    {
        bool TryPublish(in BossSweepTelegraphRequest request);
        bool TryCancelPass(uint abilityId, ulong castSequence, uint passIndex);
        bool TryCancel(uint abilityId, ulong castSequence);
    }

    /// <summary>
    /// Immutable start snapshot for a tracking laser. The server owns targeting and damage;
    /// clients only use this data to render the same locked target and beam dimensions.
    /// </summary>
    public readonly struct BossTrackingLaserPresentationRequest
    {
        public uint AbilityId { get; }
        public ulong CastSequence { get; }
        public double StartServerTime { get; }
        public EntityId TargetEntityId { get; }
        public Float3 InitialDirection { get; }
        public Float3 Size { get; }
        public float RotationSpeed { get; }

        public BossTrackingLaserPresentationRequest(
            uint abilityId,
            ulong castSequence,
            double startServerTime,
            EntityId targetEntityId,
            in Float3 initialDirection,
            in Float3 size,
            float rotationSpeed)
        {
            AbilityId = abilityId;
            CastSequence = castSequence;
            StartServerTime = startServerTime;
            TargetEntityId = targetEntityId;
            InitialDirection = initialDirection.Normalized();
            Size = new Float3(
                Math.Max(.01f, size.X),
                Math.Max(.01f, size.Y),
                Math.Max(.01f, size.Z));
            RotationSpeed = Math.Max(0f, rotationSpeed);
        }
    }

    /// <summary>Server-to-client presentation port for one locked, tracking laser cast.</summary>
    public interface IBossTrackingLaserPresentationService
    {
        bool TryPublish(in BossTrackingLaserPresentationRequest request);
        bool TryCancel(uint abilityId, ulong castSequence);
    }

    /// <summary>Server-only adapter that applies an authored planar facing to the Boss body.</summary>
    public interface IBossFacingService
    {
        Float3 CurrentFacing { get; }
        bool TrySetFacing(in Float3 forward);
    }

    /// <summary>Boss-facing adapter over the player's authoritative status host.</summary>
    public interface IBossStatusEffectService
    {
        bool TryApply(in StatusApplicationRequest request);
        bool HasStatus(EntityId target, uint statusId);
        bool TryRemove(EntityId target, uint statusId);
    }

    /// <summary>Server-only run clock mutations used by encounter abilities such as Frenzy.</summary>
    public interface IRunClockModifier
    {
        double CurrentDrainRate { get; }
        bool TryExtend(double seconds);
        bool TrySetDrainRate(double drainRate);
    }

    /// <summary>
    /// Read model for ability conditions. Health/body damage systems own mutations;
    /// individual skills are intentionally not allowed to rewrite these values.
    /// </summary>
    public interface IBossBodyState
    {
        EntityId EntityId { get; }
        float NormalizedHealth { get; }
        bool IsAlive { get; }
        bool IsStaggered { get; }
        bool IsLeftHandFunctional { get; }
        bool IsRightHandFunctional { get; }
    }

    /// <summary>Server-side write port for Boss vitals and body-part systems, not exposed to skills.</summary>
    public interface IBossBodyStateControl
    {
        bool TrySetNormalizedHealth(float value);
        bool TrySetStaggered(bool value);
        bool TrySetHandState(bool leftFunctional, bool rightFunctional);
    }
}
