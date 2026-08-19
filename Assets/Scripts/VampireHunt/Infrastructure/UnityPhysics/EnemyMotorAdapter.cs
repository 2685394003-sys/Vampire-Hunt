using System;
using UnityEngine;
using VampireHunt.Core;
using VampireHunt.Combat.Contracts;
using VampireHunt.Enemies.Application;
using VampireHunt.Enemies.Contracts;
using VampireHunt.Infrastructure.UnityPhysics.Contracts;
using EntityId = VampireHunt.Core.EntityId;

namespace VampireHunt.Infrastructure.UnityPhysics
{
    public sealed class EnemyMotorAdapter : IEnemyMotorPort, IEnemyMotor
    {
        private readonly IEntityTransformRegistry transforms;
        private readonly float defaultSpeed;
        private readonly Func<float> deltaTime;

        public EnemyMotorAdapter(
            IEntityTransformRegistry transforms,
            float defaultSpeed = 3f,
            Func<float> deltaTime = null)
        {
            this.transforms = transforms ?? throw new ArgumentNullException(nameof(transforms));
            this.defaultSpeed = Math.Max(0f, defaultSpeed);
            this.deltaTime = deltaTime;
        }

        public void Move(EntityId enemyId, EnemyMotionDto motion)
        {
            if (!transforms.TryGet(enemyId, out Transform transform) || transform == null || !motion.ShouldMove)
                return;
            Vector3 direction = new(motion.DirectionX, 0f, motion.DirectionY);
            if (direction.sqrMagnitude <= 0.000001f) return;
            direction.Normalize();
            float dt = SafeDeltaTime();
            float speed = motion.Speed > 0f ? motion.Speed : defaultSpeed;
            transform.position += direction * speed * dt;
            transform.forward = direction;
        }

        public void Move(EntityId enemyId, EnemyIntent intent)
        {
            Move(enemyId, new EnemyMotionDto(
                intent.EnemyId,
                intent.Direction.X,
                intent.Direction.Y,
                defaultSpeed,
                intent.ShouldMove));
        }

        public void Knockback(EntityId enemyId, KnockbackDto impulse)
        {
            if (impulse.IsImmune || !transforms.TryGet(enemyId, out Transform transform) || transform == null)
                return;
            Vector3 direction = ToVector3(impulse.Direction);
            if (direction.sqrMagnitude <= 0.000001f) return;
            float duration = impulse.Duration <= 0f ? 1f : impulse.Duration;
            float displacement = impulse.Force * Math.Min(SafeDeltaTime(), duration);
            transform.position += direction.normalized * displacement;
        }

        public void Knockback(EntityId enemyId, in KnockbackImpulse impulse)
        {
            Knockback(enemyId, new KnockbackDto(
                impulse.Direction, impulse.Force, impulse.Duration, impulse.IsImmune));
        }

        public void Stop(EntityId enemyId)
        {
            // Transform-backed motors have no retained velocity. A Rigidbody
            // presenter may layer its own velocity reset without changing the
            // simulation port.
        }

        private float SafeDeltaTime()
        {
            float value = deltaTime?.Invoke() ?? Time.deltaTime;
            return float.IsNaN(value) || float.IsInfinity(value) || value < 0f ? 0f : value;
        }

        private static Vector3 ToVector3(WorldPosition value) => new(value.X, value.Y, value.Z);
    }
}
