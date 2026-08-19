using System;
using System.Collections.Generic;
using VampireHunt.Core;
using VampireHunt.Spawning.Contracts;

namespace VampireHunt.Spawning.Domain
{
    /// <summary>
    /// The single authoritative spawn entry point. Pressure, capacity,
    /// sampling, gate checks and pool/network instantiation are deliberately
    /// separate ports so none can silently create a second director.
    /// </summary>
    public sealed class EnemySpawnDirector : IEnemySpawnDirector
    {
        private readonly EnemySpawnSpec spec;
        private readonly SpawnPressurePolicy pressure;
        private readonly SpawnCandidateSampler sampler;
        private readonly IEnemySpawner spawner;
        private readonly ISpawnGate gate;
        private double timeSinceBudget;
        private ulong sequence;
        private bool awaitingFirstSpawn;

        public EnemySpawnDirector(
            EnemySpawnSpec spec,
            SpawnPressurePolicy pressure,
            SpawnCandidateSampler sampler,
            IEnemySpawner spawner,
            ISpawnGate gate)
        {
            this.spec = spec ?? throw new ArgumentNullException(nameof(spec));
            this.pressure = pressure ?? throw new ArgumentNullException(nameof(pressure));
            this.sampler = sampler ?? throw new ArgumentNullException(nameof(sampler));
            this.spawner = spawner ?? throw new ArgumentNullException(nameof(spawner));
            this.gate = gate ?? throw new ArgumentNullException(nameof(gate));
            if (pressure == null) throw new ArgumentNullException(nameof(pressure));
            ResetForRun();
        }

        public SpawnBudget LastBudget { get; private set; }
        public ulong LastSequence => sequence == 0UL ? 0UL : sequence - 1UL;

        public SpawnTickResult Tick(SpawnContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (!gate.CanSpawn(context))
            {
                return SpawnTickResult.Empty(false);
            }

            int intervals;
            if (awaitingFirstSpawn)
            {
                if (context.ElapsedSeconds < spec.FirstSpawnDelay)
                    return SpawnTickResult.Empty(true);
                awaitingFirstSpawn = false;
                timeSinceBudget = 0d;
                intervals = 1;
            }
            else
            {
                timeSinceBudget += context.DeltaTime;
                intervals = CalculateIntervals();
            }
            if (intervals == 0)
            {
                return SpawnTickResult.Empty(true);
            }

            SpawnBudget requestedBudget = pressure.CalculateBudget(context.ElapsedSeconds, context.Phase);
            int requested = requestedBudget.Requested;
            if (intervals > 1 && requested > 0)
            {
                long multiplied = (long)requested * intervals;
                requested = multiplied >= int.MaxValue ? int.MaxValue : (int)multiplied;
            }
            requested = Math.Min(requested, spec.MaxSpawnsPerTick);

            int activeCount = Math.Max(0, spawner.ActiveCount);
            int available = Math.Max(0, spec.MaxAlive - activeCount);
            SpawnBudget budget = new SpawnBudget(requested, available, 0);
            LastBudget = budget;
            if (budget.Requested == 0 || budget.Available == 0)
            {
                return new SpawnTickResult(budget, true, Array.Empty<EntityId>());
            }

            List<EntityId> spawned = new List<EntityId>(Math.Min(budget.Requested, budget.Available));
            int attempts = Math.Min(budget.Requested, budget.Available);
            for (int i = 0; i < attempts; i++)
            {
                if (!sampler.TrySample(context, out SpawnCandidate candidate)) continue;
                SpawnRequest request = new SpawnRequest(spec, candidate, context.Phase, NextSequence());
                EntityId id = spawner.Spawn(request);
                if (id.IsValid) spawned.Add(id);
            }

            budget = budget.WithGranted(spawned.Count);
            LastBudget = budget;
            return new SpawnTickResult(budget, true, spawned);
        }

        public SpawnTickResult Tick(in SpawnContext context) => Tick(context);

        public void ResetForRun()
        {
            timeSinceBudget = 0d;
            sequence = 0UL;
            awaitingFirstSpawn = true;
            LastBudget = new SpawnBudget(0, 0, 0);
        }

        private int CalculateIntervals()
        {
            if (spec.SpawnInterval <= 0f)
            {
                timeSinceBudget = 0d;
                return 1;
            }

            if (timeSinceBudget < spec.SpawnInterval) return 0;
            int intervals = timeSinceBudget >= int.MaxValue * (double)spec.SpawnInterval
                ? int.MaxValue
                : (int)Math.Floor(timeSinceBudget / spec.SpawnInterval);
            timeSinceBudget -= intervals * (double)spec.SpawnInterval;
            return Math.Max(1, intervals);
        }

        private ulong NextSequence()
        {
            if (sequence == ulong.MaxValue) throw new InvalidOperationException("Spawn request sequence exhausted.");
            return sequence++;
        }
    }
}
