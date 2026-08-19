using VampireHunt.Core;

namespace VampireHunt.Spawning.Contracts
{
    public readonly struct SpawnRequest
    {
        public SpawnRequest(EnemySpawnSpec spec, WorldPosition position, int phase = 0, ulong sequence = 0UL)
            : this(spec, new SpawnCandidate(position, position, true), phase, sequence)
        {
        }

        public SpawnRequest(
            EnemySpawnSpec spec,
            SpawnCandidate candidate,
            int phase,
            ulong sequence)
        {
            Spec = spec;
            Candidate = candidate;
            Phase = phase;
            Sequence = sequence;
        }

        public EnemySpawnSpec Spec { get; }
        public SpawnCandidate Candidate { get; }
        public WorldPosition Position => Candidate.Position;
        public int Phase { get; }
        public ulong Sequence { get; }
    }
}
