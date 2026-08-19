using System;
using System.Collections.Generic;
using UnityEngine;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;
using VampireHunt.Boss.Application;
using VampireHunt.Boss.Domain;
using EntityId = VampireHunt.Core.EntityId;
using VampireHunt.Infrastructure.UnityPhysics.Contracts;

namespace VampireHunt.Infrastructure.UnityPhysics
{
    public sealed class BossAttackWorldQueryAdapter : IBossAttackQueryPort, IAttackWorldQuery
    {
        private readonly IPhysicsCombatTargetResolver resolver;
        private readonly Collider[] colliders;
        private readonly HashSet<EntityId> seen = new();
        private readonly int layerMask;
        private readonly QueryTriggerInteraction triggerInteraction;

        public BossAttackWorldQueryAdapter(
            IPhysicsCombatTargetResolver resolver,
            int layerMask = Physics.DefaultRaycastLayers,
            int maxColliders = 128,
            QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore)
        {
            this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            if (maxColliders < 1) throw new ArgumentOutOfRangeException(nameof(maxColliders));
            colliders = new Collider[maxColliders];
            this.layerMask = layerMask;
            this.triggerInteraction = triggerInteraction;
        }

        public int CollectTargets(EntityId bossId, BossDamageWindowDto window, IList<ICombatTarget> buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            buffer.Clear();
            seen.Clear();
            if (!bossId.IsValid || window.Range <= 0f) return 0;

            Vector3 origin = ToVector3(window.Origin);
            int count = Physics.OverlapSphereNonAlloc(origin, window.Range, colliders, layerMask, triggerInteraction);
            Vector3 axis = ToVector3(window.Target) - origin;
            if (axis.sqrMagnitude <= 0.000001f) axis = Vector3.forward;
            axis.Normalize();
            for (int i = 0; i < count; i++)
            {
                if (!resolver.TryResolve(colliders[i], out ICombatTarget target) || target == null ||
                    !target.IsAlive || !target.Id.IsValid || target.Id == bossId || !seen.Add(target.Id))
                    continue;
                if (IsInside(window, origin, axis, ToVector3(target.Position))) buffer.Add(target);
            }
            return buffer.Count;
        }

        public int CollectTargets(EntityId bossId, in DamageWindow window, IList<ICombatTarget> buffer)
        {
            BossAttackShape shape = window.Shape switch
            {
                TelegraphShape.Line => BossAttackShape.Line,
                TelegraphShape.Cross => BossAttackShape.Cross,
                TelegraphShape.Rectangle => BossAttackShape.Rectangle,
                TelegraphShape.Radial => BossAttackShape.Radial,
                _ => BossAttackShape.Circle
            };
            return CollectTargets(bossId, new BossDamageWindowDto(
                bossId, shape, window.Origin, window.Target, window.Range, window.Width), buffer);
        }

        private static bool IsInside(BossDamageWindowDto window, Vector3 origin, Vector3 axis, Vector3 point)
        {
            Vector3 delta = point - origin;
            float distance = delta.magnitude;
            if (distance > window.Range) return false;
            if (window.Shape == BossAttackShape.Circle || window.Shape == BossAttackShape.Radial) return true;

            float along = Vector3.Dot(delta, axis);
            if (along < 0f || along > window.Range) return false;
            float halfWidth = window.Width * 0.5f;
            float perpendicular = (delta - axis * along).magnitude;
            if (window.Shape == BossAttackShape.Cross)
            {
                // Cross attacks accept either the forward strip or its
                // perpendicular strip, with the same authored width.
                Vector3 side = Vector3.Cross(Vector3.up, axis);
                if (side.sqrMagnitude <= 0.000001f) side = Vector3.right;
                side.Normalize();
                float sideAlong = Vector3.Dot(delta, side);
                float sidePerpendicular = (delta - side * sideAlong).magnitude;
                return perpendicular <= halfWidth || (Mathf.Abs(sideAlong) <= window.Range && sidePerpendicular <= halfWidth);
            }
            return perpendicular <= halfWidth;
        }

        private static Vector3 ToVector3(WorldPosition value) => new(value.X, value.Y, value.Z);
    }
}
