using System.Threading;
using VampireHunt.Boss.Contracts;

namespace VampireHunt.Presentation.Boss
{
    public sealed class BossReadModelProjector : IBossReadModel, IBossStateSnapshotSink
    {
        private BossReadModelState current;
        private long revision;

        public BossReadModelProjector()
        {
            current = BossReadModelState.Empty;
        }

        public bool HasSnapshot => Volatile.Read(ref current).HasSnapshot;
        public long Revision => Interlocked.Read(ref revision);
        public BossSnapshot Snapshot => Volatile.Read(ref current).Snapshot;

        public int Health => Volatile.Read(ref current).Health;
        public int MaxHealth => Volatile.Read(ref current).MaxHealth;
        public BossPhase Phase => Volatile.Read(ref current).Phase;
        public bool IsInvulnerable => Volatile.Read(ref current).IsInvulnerable;
        public BossAttackId CurrentAttack => Volatile.Read(ref current).CurrentAttack;
        public EncounterMode Mode => Volatile.Read(ref current).Mode;
        public StaggerState Stagger => Volatile.Read(ref current).Stagger;
        public float ContractSeconds => Volatile.Read(ref current).ContractSeconds;

        public void Apply(BossSnapshot snapshot)
        {
            Volatile.Write(ref current, new BossReadModelState(snapshot));
            Interlocked.Increment(ref revision);
        }

        private sealed class BossReadModelState : IBossReadModel
        {
            private readonly BossSnapshot snapshot;

            public BossReadModelState(BossSnapshot snapshot)
            {
                this.snapshot = snapshot;
                HasSnapshot = snapshot.Id.IsValid;
            }

            public static BossReadModelState Empty => new BossReadModelState(default(BossSnapshot));
            public bool HasSnapshot { get; }
            public BossSnapshot Snapshot => snapshot;
            public int Health => snapshot.Health;
            public int MaxHealth => snapshot.MaxHealth;
            public BossPhase Phase => snapshot.Phase;
            public bool IsInvulnerable => snapshot.IsInvulnerable;
            public BossAttackId CurrentAttack => snapshot.CurrentAttack;
            public EncounterMode Mode => snapshot.Mode;
            public StaggerState Stagger => snapshot.Stagger;
            public float ContractSeconds => snapshot.ContractSeconds;
        }
    }
}
