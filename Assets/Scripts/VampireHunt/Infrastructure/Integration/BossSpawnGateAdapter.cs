using System;
using VampireHunt.Boss.Contracts;
using VampireHunt.Spawning.Contracts;

namespace VampireHunt.Infrastructure.Integration
{
    /// <summary>
    /// Converts the authoritative Boss encounter query into the spawning gate
    /// contract. No Boss read model or presentation state is consulted.
    /// </summary>
    public sealed class BossSpawnGateAdapter : ISpawnGate
    {
        private readonly IBossEncounterQuery encounterQuery;
        private readonly bool blockWhileEncounterActive;

        public BossSpawnGateAdapter(
            IBossEncounterQuery encounterQuery,
            bool blockWhileEncounterActive = true)
        {
            this.encounterQuery = encounterQuery ?? throw new ArgumentNullException(nameof(encounterQuery));
            this.blockWhileEncounterActive = blockWhileEncounterActive;
        }

        public IBossEncounterQuery EncounterQuery => encounterQuery;
        public bool BlockWhileEncounterActive => blockWhileEncounterActive;

        public bool CanSpawn(SpawnContext context)
        {
            if (context == null) return false;
            return !blockWhileEncounterActive || !encounterQuery.IsEncounterActive;
        }
    }
}
