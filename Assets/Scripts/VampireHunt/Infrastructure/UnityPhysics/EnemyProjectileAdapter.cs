using System;
using System.Collections.Generic;
using VampireHunt.Enemies.Application;
using VampireHunt.Infrastructure.UnityPhysics.Contracts;

namespace VampireHunt.Infrastructure.UnityPhysics
{
    public sealed class EnemyProjectileAdapter : IEnemyProjectilePort, IEnemyProjectileSpawner
    {
        private readonly IEnemyProjectilePort sink;
        private readonly Queue<EnemyProjectileDto> pending = new();

        public EnemyProjectileAdapter(IEnemyProjectilePort sink = null)
        {
            if (ReferenceEquals(sink, this)) throw new ArgumentException("An adapter cannot sink to itself.", nameof(sink));
            this.sink = sink;
        }

        public int PendingCount => pending.Count;

        public void Spawn(EnemyProjectileDto request)
        {
            if (sink != null) sink.Spawn(request);
            else pending.Enqueue(request);
        }

        public void Spawn(EnemyProjectileRequest request)
        {
            Spawn(new EnemyProjectileDto(
                request.SourceId,
                request.TargetId,
                request.HitPosition,
                request.HitPosition,
                request.Attack.BaseDamage));
        }

        public bool TryDequeue(out EnemyProjectileDto request)
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
