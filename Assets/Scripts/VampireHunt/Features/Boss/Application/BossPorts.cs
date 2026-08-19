using System;
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

    /// <summary>
    /// Scene/composition dependencies required by the Boss application. The
    /// values are capability ports only; no Unity component or Boss aggregate
    /// crosses this seam. <see cref="RegisterRuntime"/> lets a scene adapter
    /// register the authoritative Boss receiver in the shared Combat
    /// directory after Bootstrap has created the runtime.
    /// </summary>
    public readonly struct BossRuntimeDependencies
    {
        public EntityId BossId { get; }
        public ICombatEntityDirectory CombatEntities { get; }
        public IBossMotor Motor { get; }
        public IAttackWorldQuery AttackWorldQuery { get; }
        public IProjectileSpawner ProjectileSpawner { get; }
        public IBossWorldState WorldState { get; }
        public Action<IBossRuntimePort> RegisterRuntime { get; }

        public bool IsValid => BossId.IsValid && CombatEntities != null && Motor != null &&
            AttackWorldQuery != null && ProjectileSpawner != null && WorldState != null;

        public BossRuntimeDependencies(
            EntityId bossId,
            ICombatEntityDirectory combatEntities,
            IBossMotor motor,
            IAttackWorldQuery attackWorldQuery,
            IProjectileSpawner projectileSpawner,
            IBossWorldState worldState,
            Action<IBossRuntimePort> registerRuntime = null)
        {
            if (!bossId.IsValid) throw new ArgumentException("A valid Boss id is required.", nameof(bossId));
            BossId = bossId;
            CombatEntities = combatEntities ?? throw new ArgumentNullException(nameof(combatEntities));
            Motor = motor ?? throw new ArgumentNullException(nameof(motor));
            AttackWorldQuery = attackWorldQuery ?? throw new ArgumentNullException(nameof(attackWorldQuery));
            ProjectileSpawner = projectileSpawner ?? throw new ArgumentNullException(nameof(projectileSpawner));
            WorldState = worldState ?? throw new ArgumentNullException(nameof(worldState));
            RegisterRuntime = registerRuntime;
        }

        public void Register(IBossRuntimePort runtime) => RegisterRuntime?.Invoke(runtime);
    }

    /// <summary>
    /// Optional Unity/composition seam. Bootstrap asks a scene adapter for
    /// ports, creates the authoritative runtime, then binds it separately via
    /// <see cref="IBossRuntimeBinding"/>. The legacy Controller fallback is
    /// intentionally outside this seam and is only a migration bridge.
    /// </summary>
    public interface IBossRuntimeDependencyProvider
    {
        bool TryCreateRuntimeDependencies(out BossRuntimeDependencies dependencies);
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
