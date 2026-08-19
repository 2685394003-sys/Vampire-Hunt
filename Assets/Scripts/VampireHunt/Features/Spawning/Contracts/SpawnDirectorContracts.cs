namespace VampireHunt.Spawning.Contracts
{
    /// <summary>Binding port used by the legacy Unity director shell.</summary>
    public interface IEnemySpawnDirectorBinding
    {
        IEnemySpawnDirector SpawnDirector { get; }
        bool TryBind(IEnemySpawnDirector director);
    }
}
