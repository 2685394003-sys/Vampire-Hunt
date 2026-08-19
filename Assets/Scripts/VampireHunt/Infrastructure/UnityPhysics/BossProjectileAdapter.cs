using System;
using System.Collections.Generic;
using VampireHunt.Boss.Application;
using VampireHunt.Boss.Domain;
using VampireHunt.Boss.Contracts;
using VampireHunt.Core;
using EntityId = VampireHunt.Core.EntityId;
using VampireHunt.Infrastructure.UnityPhysics.Contracts;

namespace VampireHunt.Infrastructure.UnityPhysics
{
    public sealed class BossProjectileAdapter : IBossProjectilePort, IProjectileSpawner
    {
        private readonly IBossProjectilePort sink;
        private readonly Queue<BossProjectileDto> pending = new();

        public BossProjectileAdapter(IBossProjectilePort sink = null)
        {
            if (ReferenceEquals(sink, this)) throw new ArgumentException("An adapter cannot sink to itself.", nameof(sink));
            this.sink = sink;
        }

        public int PendingCount => pending.Count;

        public void Spawn(BossProjectileDto request)
        {
            if (sink != null) sink.Spawn(request);
            else pending.Enqueue(request);
        }

        public void Spawn(EntityId bossId, BossAttackId attackId, in ProjectileRequest request)
        {
            Spawn(new BossProjectileDto(
                bossId, attackId, request.Origin, request.Target, request.Count,
                request.Damage, request.Speed, request.Lifetime));
        }

        public bool TryDequeue(out BossProjectileDto request)
        {
            if (pending.Count == 0)
            {
                request = default;
                return false;
            }
            request = pending.Dequeue();
            return true;
        }
    }
}
