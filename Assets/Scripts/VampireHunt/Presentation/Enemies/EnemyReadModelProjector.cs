using System.Threading;
using VampireHunt.Enemies.Contracts;

namespace VampireHunt.Presentation.Enemies
{
    public sealed class EnemyReadModelProjector : IEnemyReadModel, IEnemyStateSnapshotSink
    {
        private EnemyReadModelState current;
        private long revision;

        public EnemyReadModelProjector()
        {
            current = EnemyReadModelState.Empty;
        }

        public bool HasSnapshot => Volatile.Read(ref current).HasSnapshot;
        public long Revision => Interlocked.Read(ref revision);
        public EnemySnapshot Snapshot => Volatile.Read(ref current).Snapshot;

        public int Health => Volatile.Read(ref current).Health;
        public bool IsAlive => Volatile.Read(ref current).IsAlive;
        public EnemyState State => Volatile.Read(ref current).State;
        public VampireHunt.Core.WorldPosition Position => Volatile.Read(ref current).Position;

        public void Apply(EnemySnapshot snapshot)
        {
            Volatile.Write(ref current, new EnemyReadModelState(snapshot));
            Interlocked.Increment(ref revision);
        }

        private sealed class EnemyReadModelState : IEnemyReadModel
        {
            private readonly EnemySnapshot snapshot;

            public EnemyReadModelState(EnemySnapshot snapshot)
            {
                this.snapshot = snapshot;
                HasSnapshot = snapshot.Id.IsValid;
            }

            public static EnemyReadModelState Empty => new EnemyReadModelState(default(EnemySnapshot));
            public bool HasSnapshot { get; }
            public EnemySnapshot Snapshot => snapshot;
            public int Health => snapshot.Health;
            public bool IsAlive => snapshot.IsAlive;
            public EnemyState State => snapshot.State;
            public VampireHunt.Core.WorldPosition Position => snapshot.Position;
        }
    }
}
