using System;
using System.Collections.Generic;
using VampireHunt.Core;

namespace VampireHunt.Spawning.Contracts
{
    public readonly struct SpawnBudget
    {
        public SpawnBudget(int requested, int available, int granted)
        {
            Requested = Math.Max(0, requested);
            Available = Math.Max(0, available);
            Granted = Math.Min(Math.Max(0, granted), Math.Min(Requested, Available));
        }

        public int Requested { get; }
        public int Available { get; }
        public int Granted { get; }

        public SpawnBudget WithCapacity(int available) =>
            new SpawnBudget(Requested, available, Math.Min(Granted, available));

        public SpawnBudget WithGranted(int granted) =>
            new SpawnBudget(Requested, Available, granted);
    }

    public sealed class SpawnTickResult
    {
        private readonly EntityId[] _spawned;
        private readonly IReadOnlyList<EntityId> _spawnedView;

        public SpawnTickResult(SpawnBudget budget, bool gateOpen, IReadOnlyList<EntityId> spawned)
        {
            Budget = budget;
            GateOpen = gateOpen;
            _spawned = Copy(spawned);
            _spawnedView = Array.AsReadOnly(_spawned);
        }

        public SpawnBudget Budget { get; }
        public bool GateOpen { get; }
        public IReadOnlyList<EntityId> Spawned => _spawnedView;
        public int SpawnedCount => _spawned.Length;

        public static SpawnTickResult Empty(bool gateOpen = true) =>
            new SpawnTickResult(new SpawnBudget(0, 0, 0), gateOpen, Array.Empty<EntityId>());

        private static EntityId[] Copy(IReadOnlyList<EntityId> source)
        {
            if (source == null || source.Count == 0) return Array.Empty<EntityId>();
            EntityId[] copy = new EntityId[source.Count];
            for (int i = 0; i < source.Count; i++) copy[i] = source[i];
            return copy;
        }
    }
}
