using VampireHunt.Core;

namespace VampireHunt.Spawning.Contracts
{
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
