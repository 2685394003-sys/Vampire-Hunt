using System;
using System.Collections.Generic;
using UnityEngine;
using VampireHunt.Core;
using VampireHunt.Boss.Application;
using VampireHunt.Boss.Domain;
using EntityId = VampireHunt.Core.EntityId;
using VampireHunt.Infrastructure.UnityPhysics.Contracts;

namespace VampireHunt.Infrastructure.UnityPhysics
{
    public sealed class BossMotorAdapter : IBossMotorPort, IBossMotor
    {
        private readonly IEntityTransformRegistry transforms;
        private readonly Func<float> deltaTime;
        private readonly Dictionary<EntityId, ActiveMove> activeMoves = new();

        public BossMotorAdapter(IEntityTransformRegistry transforms, Func<float> deltaTime = null)
        {
            this.transforms = transforms ?? throw new ArgumentNullException(nameof(transforms));
            this.deltaTime = deltaTime;
        }

        public void Execute(EntityId bossId, BossMovementDto movement)
        {
            if (!transforms.TryGet(bossId, out Transform transform) || transform == null) return;
            Vector3 from = ToVector3(movement.From);
            Vector3 to = ToVector3(movement.To);
            transform.position = from;
            activeMoves[bossId] = new ActiveMove(to, movement.Speed, movement.Duration);
            if (movement.Duration <= 0f) transform.position = to;
        }

        public void Execute(EntityId bossId, in MovementPlan plan)
        {
            Execute(bossId, new BossMovementDto(
                bossId, plan.From, plan.To, plan.Speed, plan.Duration));
        }

        public void Tick()
        {
            float dt = deltaTime?.Invoke() ?? Time.deltaTime;
            if (float.IsNaN(dt) || float.IsInfinity(dt) || dt < 0f) dt = 0f;
            List<EntityId> completed = null;
            List<KeyValuePair<EntityId, ActiveMove>> updates = null;
            foreach (KeyValuePair<EntityId, ActiveMove> pair in activeMoves)
            {
                if (!transforms.TryGet(pair.Key, out Transform transform) || transform == null)
                {
                    (completed ??= new List<EntityId>()).Add(pair.Key);
                    continue;
                }
                ActiveMove move = pair.Value;
                Vector3 before = transform.position;
                transform.position = move.Speed <= 0f
                    ? move.Target
                    : Vector3.MoveTowards(before, move.Target, move.Speed * dt);
                move.Elapsed += dt;
                if (move.Elapsed >= move.Duration || transform.position == move.Target)
                    (completed ??= new List<EntityId>()).Add(pair.Key);
                else (updates ??= new List<KeyValuePair<EntityId, ActiveMove>>()).Add(
                    new KeyValuePair<EntityId, ActiveMove>(pair.Key, move));
            }
            if (updates != null)
                for (int i = 0; i < updates.Count; i++) activeMoves[updates[i].Key] = updates[i].Value;
            if (completed != null)
                for (int i = 0; i < completed.Count; i++) activeMoves.Remove(completed[i]);
        }

        private static Vector3 ToVector3(WorldPosition value) => new(value.X, value.Y, value.Z);

        private struct ActiveMove
        {
            public ActiveMove(Vector3 target, float speed, float duration)
            {
                Target = target;
                Speed = Mathf.Max(0f, speed);
                Duration = Mathf.Max(0f, duration);
                Elapsed = 0f;
            }

            public Vector3 Target;
            public float Speed;
            public float Duration;
            public float Elapsed;
        }
    }
}
