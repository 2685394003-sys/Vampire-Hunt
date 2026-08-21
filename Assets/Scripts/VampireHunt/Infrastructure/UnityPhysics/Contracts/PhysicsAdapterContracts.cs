using System;
using System.Collections.Generic;
using UnityEngine;
using VampireHunt.Boss.Contracts;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;
using VampireHunt.Enemies.Contracts;
using VampireHunt.Player.Contracts;
using VampireHunt.Player.Application;
using EntityId = VampireHunt.Core.EntityId;

namespace VampireHunt.Infrastructure.UnityPhysics.Contracts
{
    public interface IEntityTransformRegistry
    {
        bool TryGet(EntityId id, out Transform transform);
        void Register(EntityId id, Transform transform);
        bool Unregister(EntityId id);
    }

    public interface IMeleeQueryPort
    {
        void CollectUniqueTargets(MeleeQueryDto query, ICollection<IMeleeTargetPort> buffer);
    }

    public interface IMeleeTargetPort
    {
        EntityId Id { get; }
        bool IsAlive { get; }
        WorldPosition HitPosition { get; }
    }

    public readonly struct MeleeQueryDto
    {
        public MeleeQueryDto(EntityId sourceId, WorldPosition origin, WorldPosition aimAt, float range, float coneAngleDegrees)
        {
            SourceId = sourceId;
            Origin = origin;
            AimAt = aimAt;
            Range = MathUtil.NonNegative(range);
            ConeAngleDegrees = MathUtil.NonNegative(coneAngleDegrees);
        }

        public EntityId SourceId { get; }
        public WorldPosition Origin { get; }
        public WorldPosition AimAt { get; }
        public float Range { get; }
        public float ConeAngleDegrees { get; }
    }

    public interface IEnemyMotorPort
    {
        void Move(EntityId enemyId, EnemyMotionDto motion);
        void Knockback(EntityId enemyId, KnockbackDto impulse);
        void Stop(EntityId enemyId);
    }

    public readonly struct EnemyMotionDto
    {
        public EnemyMotionDto(EntityId enemyId, int directionX, int directionY, float speed, bool shouldMove)
        {
            EnemyId = enemyId;
            DirectionX = Math.Sign(directionX);
            DirectionY = Math.Sign(directionY);
            Speed = MathUtil.NonNegative(speed);
            ShouldMove = shouldMove;
        }

        public EntityId EnemyId { get; }
        public int DirectionX { get; }
        public int DirectionY { get; }
        public float Speed { get; }
        public bool ShouldMove { get; }
    }

    public readonly struct KnockbackDto
    {
        public KnockbackDto(WorldPosition direction, float force, float duration, bool immune = false)
        {
            Direction = direction;
            Force = MathUtil.NonNegative(force);
            Duration = MathUtil.NonNegative(duration);
            IsImmune = immune;
        }

        public WorldPosition Direction { get; }
        public float Force { get; }
        public float Duration { get; }
        public bool IsImmune { get; }
    }

    public interface IEnemyProjectilePort
    {
        void Spawn(EnemyProjectileDto request);
    }

    public readonly struct EnemyProjectileDto
    {
        public EnemyProjectileDto(EntityId sourceId, EntityId targetId, WorldPosition origin, WorldPosition target, int damage)
        {
            SourceId = sourceId;
            TargetId = targetId;
            Origin = origin;
            Target = target;
            Damage = Math.Max(0, damage);
        }

        public EntityId SourceId { get; }
        public EntityId TargetId { get; }
        public WorldPosition Origin { get; }
        public WorldPosition Target { get; }
        public int Damage { get; }
    }

    public interface IBossMotorPort
    {
        void Execute(EntityId bossId, BossMovementDto movement);
    }

    public readonly struct BossMovementDto
    {
        public BossMovementDto(EntityId bossId, WorldPosition from, WorldPosition to, float speed, float duration)
        {
            BossId = bossId;
            From = from;
            To = to;
            Speed = MathUtil.NonNegative(speed);
            Duration = MathUtil.NonNegative(duration);
        }

        public EntityId BossId { get; }
        public WorldPosition From { get; }
        public WorldPosition To { get; }
        public float Speed { get; }
        public float Duration { get; }
    }

    public enum BossAttackShape
    {
        Circle = 0,
        Line = 1,
        Cross = 2,
        Rectangle = 3,
        Radial = 4
    }

    public readonly struct BossDamageWindowDto
    {
        public BossDamageWindowDto(EntityId bossId, BossAttackShape shape, WorldPosition origin, WorldPosition target, float range, float width)
        {
            BossId = bossId;
            Shape = shape;
            Origin = origin;
            Target = target;
            Range = MathUtil.NonNegative(range);
            Width = MathUtil.NonNegative(width);
        }

        public EntityId BossId { get; }
        public BossAttackShape Shape { get; }
        public WorldPosition Origin { get; }
        public WorldPosition Target { get; }
        public float Range { get; }
        public float Width { get; }
    }

    public interface IBossAttackQueryPort
    {
        int CollectTargets(EntityId bossId, BossDamageWindowDto window, IList<ICombatTarget> buffer);
    }

    public interface IBossProjectilePort
    {
        void Spawn(BossProjectileDto request);
    }

    public readonly struct BossProjectileDto
    {
        public BossProjectileDto(EntityId bossId, BossAttackId attackId, WorldPosition origin, WorldPosition target, int count, int damage, float speed, float lifetime)
        {
            BossId = bossId;
            AttackId = attackId;
            Origin = origin;
            Target = target;
            Count = Math.Max(0, count);
            Damage = Math.Max(0, damage);
            Speed = MathUtil.NonNegative(speed);
            Lifetime = MathUtil.NonNegative(lifetime);
        }

        public EntityId BossId { get; }
        public BossAttackId AttackId { get; }
        public WorldPosition Origin { get; }
        public WorldPosition Target { get; }
        public int Count { get; }
        public int Damage { get; }
        public float Speed { get; }
        public float Lifetime { get; }
    }

    public interface IPhysicsMeleeTargetResolver
    {
        bool TryResolve(Collider collider, out IMeleeTargetPort target);
    }

    public interface IPhysicsApplicationMeleeTargetResolver
    {
        bool TryResolve(Collider collider, out IMeleeHitTarget target);
    }

    public interface IPhysicsCombatTargetResolver
    {
        bool TryResolve(Collider collider, out ICombatTarget target);
    }

    public static class MathUtil
    {
        public static float NonNegative(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) || value < 0f ? 0f : value;
        }
    }
}
