using System;
using System.Threading;
using VampireHunt.Player.Contracts;

namespace VampireHunt.Presentation.Player
{
    /// <summary>
    /// Atomically swaps an immutable player snapshot. Presenters never retain
    /// a writable domain object and can safely render while replication applies
    /// the next snapshot.
    /// </summary>
    public sealed class PlayerReadModelProjector : IPlayerReadModel, IPlayerStateSnapshotSink
    {
        private PlayerReadModelState current;
        private long revision;

        public PlayerReadModelProjector()
        {
            current = PlayerReadModelState.Empty;
        }

        public bool HasSnapshot => Volatile.Read(ref current).HasSnapshot;
        public long Revision => Interlocked.Read(ref revision);
        public PlayerSnapshot Snapshot => Volatile.Read(ref current).Snapshot;

        public int Health => Volatile.Read(ref current).Health;
        public int MaxHealth => Volatile.Read(ref current).MaxHealth;
        public float Stamina => Volatile.Read(ref current).Stamina;
        public float MaxStamina => Volatile.Read(ref current).MaxStamina;
        public int Scarlet => Volatile.Read(ref current).Scarlet;
        public int Coins => Volatile.Read(ref current).Coins;
        public int Level => Volatile.Read(ref current).Level;
        public int Experience => Volatile.Read(ref current).Experience;
        public bool IsAlive => Volatile.Read(ref current).IsAlive;

        public void Apply(PlayerSnapshot snapshot)
        {
            PlayerReadModelState next = new PlayerReadModelState(snapshot);
            Volatile.Write(ref current, next);
            Interlocked.Increment(ref revision);
        }

        private sealed class PlayerReadModelState : IPlayerReadModel
        {
            private readonly PlayerSnapshot snapshot;

            public PlayerReadModelState(PlayerSnapshot snapshot)
            {
                this.snapshot = new PlayerSnapshot(
                    snapshot.Id,
                    snapshot.CurrentHealth,
                    snapshot.MaxHealth,
                    snapshot.IsAlive,
                    snapshot.IsInvincibleWindow,
                    snapshot.Stamina,
                    snapshot.MaxStamina,
                    snapshot.Scarlet,
                    snapshot.Coins,
                    snapshot.Level,
                    snapshot.Experience,
                    snapshot.LastCommandSequence,
                    snapshot.IsAttacking,
                    snapshot.AttackWindowEndsAt,
                    snapshot.BloodPacts);
                HasSnapshot = snapshot.Id.IsValid;
            }

            public static PlayerReadModelState Empty => new PlayerReadModelState(default(PlayerSnapshot));
            public bool HasSnapshot { get; }
            public PlayerSnapshot Snapshot => snapshot;
            public int Health => snapshot.Health;
            public int MaxHealth => snapshot.MaxHealth;
            public float Stamina => snapshot.Stamina;
            public float MaxStamina => snapshot.MaxStamina;
            public int Scarlet => snapshot.Scarlet;
            public int Coins => snapshot.Coins;
            public int Level => snapshot.Level;
            public int Experience => snapshot.Experience;
            public bool IsAlive => snapshot.IsAlive;
        }
    }
}
