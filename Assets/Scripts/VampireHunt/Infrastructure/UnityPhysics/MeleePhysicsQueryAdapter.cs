using System;
using System.Collections.Generic;
using UnityEngine;
using VampireHunt.Core;
using VampireHunt.Infrastructure.UnityPhysics.Contracts;
using VampireHunt.Player.Application;
using EntityId = VampireHunt.Core.EntityId;

namespace VampireHunt.Infrastructure.UnityPhysics
{
    /// <summary>
    /// Physics-backed melee query with a stable per-query EntityId de-duplication
    /// set. Collider count and layer mask are bounded to keep attacks predictable.
    /// </summary>
    public sealed class MeleePhysicsQueryAdapter : IMeleeQueryPort, IMeleeHitQuery
    {
        private readonly IPhysicsMeleeTargetResolver resolver;
        private readonly IPhysicsApplicationMeleeTargetResolver applicationResolver;
        private readonly Collider[] colliders;
        private readonly int layerMask;
        private readonly QueryTriggerInteraction triggerInteraction;
        private readonly HashSet<EntityId> seen = new();

        public MeleePhysicsQueryAdapter(
            IPhysicsMeleeTargetResolver resolver,
            int layerMask = Physics.DefaultRaycastLayers,
            int maxColliders = 64,
            QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore,
            IPhysicsApplicationMeleeTargetResolver applicationResolver = null)
        {
            this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            this.applicationResolver = applicationResolver ?? resolver as IPhysicsApplicationMeleeTargetResolver;
            if (maxColliders < 1) throw new ArgumentOutOfRangeException(nameof(maxColliders));
            colliders = new Collider[maxColliders];
            this.layerMask = layerMask;
            this.triggerInteraction = triggerInteraction;
        }

        public void CollectUniqueTargets(MeleeHitQuery query, ICollection<IMeleeHitTarget> buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            buffer.Clear();
            seen.Clear();
            if (applicationResolver == null || !query.SourceId.IsValid || query.Range <= 0f) return;

            Vector3 origin = ToVector3(query.Origin);
            int count = Physics.OverlapSphereNonAlloc(origin, query.Range, colliders, layerMask, triggerInteraction);
            Vector3 aim = ToVector3(query.AimAt) - origin;
            if (aim.sqrMagnitude <= 0.000001f) aim = Vector3.forward;
            aim.Normalize();
            float halfAngle = Mathf.Clamp(query.ConeAngleDegrees, 0f, 360f) * 0.5f;
            float minimumDot = Mathf.Cos(halfAngle * Mathf.Deg2Rad);
            float rangeSquared = query.Range * query.Range;
            for (int i = 0; i < count; i++)
            {
                if (!applicationResolver.TryResolve(colliders[i], out IMeleeHitTarget target) || target == null ||
                    !target.IsAlive || !target.Id.IsValid || target.Id == query.SourceId || !seen.Add(target.Id))
                    continue;
                Vector3 delta = ToVector3(target.HitPosition) - origin;
                if (delta.sqrMagnitude > rangeSquared) continue;
                if (delta.sqrMagnitude > 0.000001f && Vector3.Dot(aim, delta.normalized) < minimumDot) continue;
                buffer.Add(target);
            }
        }

        public void CollectUniqueTargets(MeleeQueryDto query, ICollection<IMeleeTargetPort> buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            buffer.Clear();
            seen.Clear();
            if (!query.SourceId.IsValid || query.Range <= 0f) return;

            Vector3 origin = ToVector3(query.Origin);
            int count = Physics.OverlapSphereNonAlloc(origin, query.Range, colliders, layerMask, triggerInteraction);
            Vector3 aim = ToVector3(query.AimAt) - origin;
            if (aim.sqrMagnitude <= 0.000001f) aim = Vector3.forward;
            aim.Normalize();
            float halfAngle = Mathf.Clamp(query.ConeAngleDegrees, 0f, 360f) * 0.5f;
            float minimumDot = Mathf.Cos(halfAngle * Mathf.Deg2Rad);
            float rangeSquared = query.Range * query.Range;

            for (int i = 0; i < count; i++)
            {
                Collider collider = colliders[i];
                if (!resolver.TryResolve(collider, out IMeleeTargetPort target) || target == null ||
                    !target.IsAlive || !target.Id.IsValid || target.Id == query.SourceId || !seen.Add(target.Id))
                    continue;

                Vector3 delta = ToVector3(target.HitPosition) - origin;
                if (delta.sqrMagnitude > rangeSquared) continue;
                if (delta.sqrMagnitude > 0.000001f && Vector3.Dot(aim, delta.normalized) < minimumDot) continue;
                buffer.Add(target);
            }
        }

        private static Vector3 ToVector3(WorldPosition position) =>
            new(position.X, position.Y, position.Z);
    }
}
