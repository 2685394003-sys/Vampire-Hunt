using VampireHunt.Core;

namespace VampireHunt.Spawning.Contracts
{
    /// <summary>
    /// Minimal public facade for the one authoritative spawn director. Keeping
    /// this port with the existing tracked spawn contracts also makes it
    /// available to source-only composition builds before Unity imports a new
    /// contract file.
    /// </summary>
    public interface IEnemySpawnDirector
    {
        SpawnTickResult Tick(SpawnContext context);
        void ResetForRun();
    }

    public interface ISpawnLocationQuery
    {
        bool IsValid(WorldPosition position);
        WorldPosition SampleAround(WorldPosition target);
    }

    public interface IEnemySpawner
    {
        EntityId Spawn(SpawnRequest request);
        int ActiveCount { get; }
    }

    public interface ISpawnGate
    {
        bool CanSpawn(SpawnContext context);
    }
}
