using System;
using System.Collections.Generic;
using VampireHunt.Boss.Contracts;
using VampireHunt.Core;
using VampireHunt.Core.Contracts;
using VampireHunt.Enemies.Contracts;
using VampireHunt.Infrastructure.Netcode.Contracts;
using VampireHunt.Player.Contracts;

namespace VampireHunt.Infrastructure.Netcode
{
    public sealed class PlayerStateReplicator
    {
        private readonly Dictionary<EntityId, uint> receivedSequences = new();

        public PlayerStateDto Capture(PlayerSnapshot snapshot) => new(snapshot);

        public bool ApplyNetworkState(
            PlayerStateDto dto,
            IStateSnapshotSink<PlayerSnapshot> sink)
        {
            if (sink == null || !dto.Id.IsValid || !AcceptSequence(dto.Id, dto.LastCommandSequence))
                return false;
            sink.Apply(dto.ToSnapshot());
            return true;
        }

        public void Reset(EntityId id) => receivedSequences.Remove(id);

        private bool AcceptSequence(EntityId id, uint candidate)
        {
            if (!receivedSequences.TryGetValue(id, out uint previous))
            {
                receivedSequences[id] = candidate;
                return true;
            }

            if (candidate == previous) return false;
            if (!NetworkSequence.IsNewer(candidate, previous)) return false;
            receivedSequences[id] = candidate;
            return true;
        }
    }

    public sealed class EnemyStateReplicator
    {
        private readonly EnemyReplicationPolicy policy;
        private readonly int positionScale;
        private readonly Dictionary<EntityId, CapturedEnemy> captured = new();
        private readonly Dictionary<EntityId, uint> receivedSequences = new();
        private long tick;

        public EnemyStateReplicator(
            EnemyReplicationPolicy policy = default,
            int positionScale = EnemyStateDto.DefaultPositionScale)
        {
            this.policy = policy.Equals(default(EnemyReplicationPolicy))
                ? new EnemyReplicationPolicy()
                : policy;
            this.positionScale = positionScale <= 0 ? EnemyStateDto.DefaultPositionScale : positionScale;
        }

        public long Tick => tick;

        public bool Capture(
            EnemySnapshot snapshot,
            float distanceToNearestPlayer,
            double now,
            out EnemyStateDto dto)
        {
            EnemyReplicationTier tier = policy.GetTier(distanceToNearestPlayer);
            bool changed = !captured.TryGetValue(snapshot.Id, out CapturedEnemy previous) ||
                !previous.Matches(snapshot);
            bool periodic = policy.ShouldSend(tier, tick);
            bool dirty = changed || periodic;

            uint sequence = previous.Sequence;
            if (changed) sequence = Next(sequence);
            dto = EnemyStateDto.From(snapshot, sequence, dirty, tier, positionScale);
            captured[snapshot.Id] = new CapturedEnemy(snapshot, sequence, now);
            tick = tick == long.MaxValue ? 0L : tick + 1L;
            return dirty;
        }

        public bool ApplyNetworkState(
            EnemyStateDto dto,
            IStateSnapshotSink<EnemySnapshot> sink)
        {
            if (sink == null || !dto.Id.IsValid || !dto.Dirty || !AcceptSequence(dto.Id, dto.Sequence))
                return false;
            sink.Apply(dto.ToSnapshot());
            return true;
        }

        public void Reset(EntityId id)
        {
            captured.Remove(id);
            receivedSequences.Remove(id);
        }

        private bool AcceptSequence(EntityId id, uint candidate)
        {
            if (!receivedSequences.TryGetValue(id, out uint previous))
            {
                receivedSequences[id] = candidate;
                return true;
            }
            if (candidate == previous || !NetworkSequence.IsNewer(candidate, previous)) return false;
            receivedSequences[id] = candidate;
            return true;
        }

        private static uint Next(uint value) => value == uint.MaxValue ? 1U : value + 1U;

        private readonly struct CapturedEnemy
        {
            private readonly EnemySnapshot snapshot;
            public CapturedEnemy(EnemySnapshot snapshot, uint sequence, double capturedAt)
            {
                this.snapshot = snapshot;
                Sequence = sequence;
                CapturedAt = capturedAt;
            }

            public uint Sequence { get; }
            public double CapturedAt { get; }
            public bool Matches(EnemySnapshot other)
            {
                return snapshot.Id == other.Id && snapshot.Health == other.Health &&
                    snapshot.MaxHealth == other.MaxHealth && snapshot.IsAlive == other.IsAlive &&
                    snapshot.State == other.State && snapshot.Position == other.Position &&
                    snapshot.TargetId == other.TargetId && snapshot.LastAttackAt.Equals(other.LastAttackAt);
            }
        }
    }

    public sealed class BossStateReplicator
    {
        private readonly Dictionary<EntityId, uint> sendSequences = new();
        private readonly Dictionary<EntityId, BossSnapshot> previous = new();
        private readonly Dictionary<EntityId, uint> receivedSequences = new();

        public BossStateDto Capture(BossSnapshot snapshot)
        {
            bool changed = !previous.TryGetValue(snapshot.Id, out BossSnapshot old) || !Equals(old, snapshot);
            uint sequence = sendSequences.TryGetValue(snapshot.Id, out uint current) ? current : 0U;
            if (changed) sequence = sequence == uint.MaxValue ? 1U : sequence + 1U;
            previous[snapshot.Id] = snapshot;
            sendSequences[snapshot.Id] = sequence;
            return new BossStateDto(snapshot, sequence, changed);
        }

        public bool ApplyNetworkState(
            BossStateDto dto,
            IStateSnapshotSink<BossSnapshot> sink)
        {
            if (sink == null || !dto.Id.IsValid || !dto.Dirty || !AcceptSequence(dto.Id, dto.Sequence))
                return false;
            sink.Apply(dto.ToSnapshot());
            return true;
        }

        public void Reset(EntityId id)
        {
            previous.Remove(id);
            sendSequences.Remove(id);
            receivedSequences.Remove(id);
        }

        private bool AcceptSequence(EntityId id, uint candidate)
        {
            if (!receivedSequences.TryGetValue(id, out uint previousSequence))
            {
                receivedSequences[id] = candidate;
                return true;
            }
            if (candidate == previousSequence || !NetworkSequence.IsNewer(candidate, previousSequence)) return false;
            receivedSequences[id] = candidate;
            return true;
        }
    }
}
