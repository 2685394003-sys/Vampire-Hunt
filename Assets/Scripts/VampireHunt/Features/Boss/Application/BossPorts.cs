using System.Collections.Generic;
using VampireHunt.Boss.Contracts;
using VampireHunt.Boss.Domain;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;

namespace VampireHunt.Boss.Application
{
    internal interface IBossRepository
    {
        BossAggregate Get(EntityId bossId);
        bool TryGet(EntityId bossId, out BossAggregate aggregate);
        IEnumerable<BossAggregate> GetAll();
    }

    public interface IBossMotor
    {
        void Execute(EntityId bossId, in MovementPlan plan);
    }

    public interface IAttackWorldQuery
    {
        int CollectTargets(EntityId bossId, in DamageWindow window, IList<ICombatTarget> buffer);
    }

    public interface IProjectileSpawner
    {
        void Spawn(EntityId bossId, BossAttackId attackId, in ProjectileRequest request);
    }

    public interface IBossWorldState
    {
        bool TryGetAttackContext(
            EntityId bossId,
            BossPhase phase,
            double now,
            out BossAttackContext context);
    }

    public interface IBossEncounterConsequences
    {
        void OnBossDefeated(EntityId bossId, EntityId killerId);
    }

    internal sealed class InMemoryBossRepository : IBossRepository
    {
        private readonly Dictionary<EntityId, BossAggregate> bosses = new();

        public void Add(BossAggregate aggregate)
        {
            if (aggregate == null) throw new System.ArgumentNullException(nameof(aggregate));
            if (!bosses.TryAdd(aggregate.Id, aggregate))
                throw new System.InvalidOperationException($"Boss '{aggregate.Id}' is already registered.");
        }

        public BossAggregate Get(EntityId bossId) => bosses.TryGetValue(bossId, out BossAggregate boss)
            ? boss
            : throw new KeyNotFoundException($"Boss '{bossId}' was not found.");

        public bool TryGet(EntityId bossId, out BossAggregate aggregate) => bosses.TryGetValue(bossId, out aggregate);
        public IEnumerable<BossAggregate> GetAll() => bosses.Values;
    }
}
