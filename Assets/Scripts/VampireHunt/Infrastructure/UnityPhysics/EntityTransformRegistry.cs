using System;
using System.Collections.Generic;
using UnityEngine;
using VampireHunt.Core;
using VampireHunt.Combat.Contracts;
using VampireHunt.Infrastructure.UnityPhysics.Contracts;
using VampireHunt.Player.Application;
using EntityId = VampireHunt.Core.EntityId;

namespace VampireHunt.Infrastructure.UnityPhysics
{
    public sealed class EntityTransformRegistry : IEntityTransformRegistry
    {
        private readonly Dictionary<EntityId, Transform> transforms = new();

        public int Count => transforms.Count;

        public bool TryGet(EntityId id, out Transform transform)
        {
            return transforms.TryGetValue(id, out transform) && transform != null;
        }

        public void Register(EntityId id, Transform transform)
        {
            if (!id.IsValid) throw new ArgumentException("A valid entity id is required.", nameof(id));
            if (transform == null) throw new ArgumentNullException(nameof(transform));
            transforms[id] = transform;
        }

        public bool Unregister(EntityId id) => transforms.Remove(id);
    }

    public sealed class PhysicsTargetRegistry : IPhysicsMeleeTargetResolver, IPhysicsApplicationMeleeTargetResolver, IPhysicsCombatTargetResolver
    {
        private readonly Dictionary<Collider, IMeleeTargetPort> melee = new();
        private readonly Dictionary<Collider, IMeleeHitTarget> applicationMelee = new();
        private readonly Dictionary<Collider, ICombatTarget> combat = new();

        public void Register(Collider collider, IMeleeTargetPort target)
        {
            if (collider == null) throw new ArgumentNullException(nameof(collider));
            if (target == null) throw new ArgumentNullException(nameof(target));
            melee[collider] = target;
        }

        public void Register(Collider collider, ICombatTarget target)
        {
            if (collider == null) throw new ArgumentNullException(nameof(collider));
            if (target == null) throw new ArgumentNullException(nameof(target));
            combat[collider] = target;
        }

        public void Register(Collider collider, IMeleeHitTarget target)
        {
            if (collider == null) throw new ArgumentNullException(nameof(collider));
            if (target == null) throw new ArgumentNullException(nameof(target));
            applicationMelee[collider] = target;
        }

        public bool TryResolve(Collider collider, out IMeleeTargetPort target)
        {
            if (collider != null && melee.TryGetValue(collider, out target)) return true;
            target = null;
            return false;
        }

        public bool TryResolve(Collider collider, out IMeleeHitTarget target)
        {
            if (collider != null && applicationMelee.TryGetValue(collider, out target)) return true;
            target = null;
            return false;
        }

        public bool TryResolve(Collider collider, out ICombatTarget target)
        {
            if (collider != null && combat.TryGetValue(collider, out target)) return true;
            target = null;
            return false;
        }

        public bool Unregister(Collider collider)
        {
            bool removed = melee.Remove(collider);
            removed |= applicationMelee.Remove(collider);
            removed |= combat.Remove(collider);
            return removed;
        }

        public void Clear()
        {
            melee.Clear();
            applicationMelee.Clear();
            combat.Clear();
        }
    }
}
