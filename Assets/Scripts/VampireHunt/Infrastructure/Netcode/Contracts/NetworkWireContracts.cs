using System;
using Unity.Collections;
using Unity.Netcode;
using VampireHunt.Abilities.Contracts;
using VampireHunt.Boss.Contracts;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;
using VampireHunt.Enemies.Contracts;
using VampireHunt.Player.Contracts;

namespace VampireHunt.Infrastructure.Netcode.Contracts
{
    /// <summary>
    /// Versioned, fixed-shape values that cross the NGO boundary.  Domain
    /// objects are never passed to an RPC; the conversion code below is the
    /// single compatibility boundary for the wire protocol.
    /// </summary>
    public static class NetworkProtocol
    {
        public const ushort CurrentVersion = 1;
        public const int PositionScale = 100;
        public const int MaxPactCount = 8;
    }

    /// <summary>Payload discriminator for the command protocol.</summary>
    public enum NetworkCommandKind : byte
    {
        Dash = 0,
        Attack = 1,
        SelectBloodPact = 2
    }

    /// <summary>
    /// NGO-serializable command payload.  Sender identity is intentionally
    /// absent: the server must take it from RpcParams.Receive.SenderClientId.
    /// </summary>
    public struct NetworkCommandWire : INetworkSerializable
    {
        public ushort ProtocolVersion;
        public NetworkCommandKind Kind;
        public ulong PlayerId;
        public float ValueX;
        public float ValueY;
        public float ValueZ;
        public FixedString64Bytes Selection;
        public uint OfferVersion;
        public uint Sequence;

        public static NetworkCommandWire From(DashCommand command)
        {
            return new NetworkCommandWire
            {
                ProtocolVersion = NetworkProtocol.CurrentVersion,
                Kind = NetworkCommandKind.Dash,
                PlayerId = command.PlayerId.Value,
                ValueX = command.Direction.X,
                ValueY = command.Direction.Y,
                ValueZ = command.Direction.Z,
                Sequence = command.Sequence
            };
        }

        public static NetworkCommandWire From(AttackCommand command)
        {
            return new NetworkCommandWire
            {
                ProtocolVersion = NetworkProtocol.CurrentVersion,
                Kind = NetworkCommandKind.Attack,
                PlayerId = command.PlayerId.Value,
                ValueX = command.AimAt.X,
                ValueY = command.AimAt.Y,
                ValueZ = command.AimAt.Z,
                Sequence = command.Sequence
            };
        }

        public static NetworkCommandWire From(SelectBloodPactCommand command)
        {
            return new NetworkCommandWire
            {
                ProtocolVersion = NetworkProtocol.CurrentVersion,
                Kind = NetworkCommandKind.SelectBloodPact,
                PlayerId = command.PlayerId.Value,
                Selection = new FixedString64Bytes(command.Selection.Value ?? string.Empty),
                OfferVersion = command.OfferVersion,
                Sequence = command.Sequence
            };
        }

        public bool IsVersionSupported => ProtocolVersion == NetworkProtocol.CurrentVersion;

        public bool TryToDash(out DashCommand command)
        {
            command = default;
            if (!IsVersionSupported || Kind != NetworkCommandKind.Dash ||
                !VampireHunt.Core.EntityId.TryCreate(PlayerId, out EntityId playerId) ||
                !IsFinite(ValueX) || !IsFinite(ValueY) || !IsFinite(ValueZ))
                return false;

            command = new DashCommand(playerId, new MoveVector(ValueX, ValueY, ValueZ), Sequence);
            return true;
        }

        public bool TryToAttack(out AttackCommand command)
        {
            command = default;
            if (!IsVersionSupported || Kind != NetworkCommandKind.Attack ||
                !VampireHunt.Core.EntityId.TryCreate(PlayerId, out EntityId playerId) ||
                !IsFinite(ValueX) || !IsFinite(ValueY) || !IsFinite(ValueZ))
                return false;

            command = new AttackCommand(playerId, new WorldPosition(ValueX, ValueY, ValueZ), Sequence);
            return true;
        }

        public bool TryToSelectBloodPact(out SelectBloodPactCommand command)
        {
            command = default;
            string selection = Selection.ToString();
            if (!IsVersionSupported || Kind != NetworkCommandKind.SelectBloodPact ||
                !VampireHunt.Core.EntityId.TryCreate(PlayerId, out EntityId playerId) ||
                string.IsNullOrWhiteSpace(selection))
                return false;

            command = new SelectBloodPactCommand(
                playerId,
                new BloodPactId(selection),
                OfferVersion,
                Sequence);
            return true;
        }

        public bool TryGetPlayerId(out EntityId playerId) => VampireHunt.Core.EntityId.TryCreate(PlayerId, out playerId);

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref ProtocolVersion);
            serializer.SerializeValue(ref Kind);
            serializer.SerializeValue(ref PlayerId);
            serializer.SerializeValue(ref ValueX);
            serializer.SerializeValue(ref ValueY);
            serializer.SerializeValue(ref ValueZ);
            serializer.SerializeValue(ref Selection);
            serializer.SerializeValue(ref OfferVersion);
            serializer.SerializeValue(ref Sequence);
        }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>
    /// Explicit command envelope used after the server has bound senderId to
    /// the NGO receive context.  It contains no object payload.
    /// </summary>
    public readonly struct NetworkCommandEnvelope
    {
        private readonly NetworkCommandWire wire;

        private NetworkCommandEnvelope(ulong senderId, NetworkCommandWire wire)
        {
            SenderId = senderId;
            this.wire = wire;
        }

        public ulong SenderId { get; }
        public ushort ProtocolVersion => wire.ProtocolVersion;
        public NetworkCommandKind Kind => wire.Kind;
        public uint Sequence => wire.Sequence;
        public NetworkCommandWire Wire => wire;
        public bool IsVersionSupported => wire.IsVersionSupported;

        // Compatibility accessors remain domain projections, not serialized
        // fields.  Invalid/foreign discriminators fail closed instead of
        // casting an arbitrary object payload.
        public DashCommand Dash
        {
            get
            {
                if (!TryGetDash(out DashCommand command)) throw new InvalidOperationException("The envelope is not a Dash command.");
                return command;
            }
        }

        public AttackCommand Attack
        {
            get
            {
                if (!TryGetAttack(out AttackCommand command)) throw new InvalidOperationException("The envelope is not an Attack command.");
                return command;
            }
        }

        public SelectBloodPactCommand SelectBloodPact
        {
            get
            {
                if (!TryGetSelectBloodPact(out SelectBloodPactCommand command)) throw new InvalidOperationException("The envelope is not a Blood Pact command.");
                return command;
            }
        }

        public static NetworkCommandEnvelope From(ulong senderId, DashCommand command) =>
            new NetworkCommandEnvelope(senderId, NetworkCommandWire.From(command));

        public static NetworkCommandEnvelope From(ulong senderId, AttackCommand command) =>
            new NetworkCommandEnvelope(senderId, NetworkCommandWire.From(command));

        public static NetworkCommandEnvelope From(ulong senderId, SelectBloodPactCommand command) =>
            new NetworkCommandEnvelope(senderId, NetworkCommandWire.From(command));

        public static NetworkCommandEnvelope FromWire(ulong senderId, NetworkCommandWire wire) =>
            new NetworkCommandEnvelope(senderId, wire);

        public bool TryGetDash(out DashCommand command) => wire.TryToDash(out command);
        public bool TryGetAttack(out AttackCommand command) => wire.TryToAttack(out command);
        public bool TryGetSelectBloodPact(out SelectBloodPactCommand command) => wire.TryToSelectBloodPact(out command);
    }

    /// <summary>Discriminator for the fixed-shape gameplay event wire.</summary>
    public enum GameplayEventKind : byte
    {
        Unknown = 0,
        DamageConfirmed = 1,
        HealingConfirmed = 2,
        EntityDied = 3,
        GameplayCue = 4,
        EnemyDeath = 5,
        PlayerDeath = 6,
        BossPhaseChanged = 7,
        BossAttackTelegraph = 8,
        BossAttackCue = 9,
        BossDefeated = 10,
        OpaqueMetadata = 255
    }

    /// <summary>
    /// Fixed-shape event payload.  The fields are intentionally a superset so
    /// every supported event has a deterministic, bounded representation.
    /// Unsupported event types become metadata-only and never execute a
    /// feature-specific rule on the receiving side.
    /// </summary>
    public struct GameplayEventWire : INetworkSerializable
    {
        public ushort ProtocolVersion;
        public GameplayEventKind Kind;
        public ulong EventId;
        public long OccurredAtMilliseconds;
        public ulong SourceId;
        public ulong TargetId;
        public ulong EntityId;
        public ulong KillerId;
        public ulong BossId;
        public ulong PlayerId;
        public ulong EnemyId;
        public int PositionX;
        public int PositionY;
        public int PositionZ;
        public int PositionX2;
        public int PositionY2;
        public int PositionZ2;
        public int Requested;
        public int Applied;
        public int Secondary;
        public float Duration;
        public byte Flags;
        public byte Phase;
        public byte WasCritical;
        public byte WasKilled;
        public byte ExtraA;
        public byte ExtraB;
        public FixedString64Bytes Tag;

        public bool IsVersionSupported => ProtocolVersion == NetworkProtocol.CurrentVersion;
        public bool IsFeatureEvent => Kind != GameplayEventKind.Unknown && Kind != GameplayEventKind.OpaqueMetadata;

        public static bool TryFrom(IGameplayEvent @event, out GameplayEventWire wire)
        {
            try
            {
                return TryFromCore(@event, out wire);
            }
            catch (ArgumentException)
            {
                wire = default;
                return false;
            }
            catch (OverflowException)
            {
                wire = default;
                return false;
            }
        }

        private static bool TryFromCore(IGameplayEvent @event, out GameplayEventWire wire)
        {
            wire = default;
            if (@event == null) return false;
            wire.ProtocolVersion = NetworkProtocol.CurrentVersion;
            wire.EventId = @event.EventId;
            wire.OccurredAtMilliseconds = QuantizeMilliseconds(@event.OccurredAt);

            if (@event is DamageConfirmedEvent damage)
            {
                wire.Kind = GameplayEventKind.DamageConfirmed;
                wire.SourceId = damage.SourceId.Value;
                wire.TargetId = damage.TargetId.Value;
                wire.Requested = damage.Result.RequestedDamage;
                wire.Applied = damage.Result.AppliedDamage;
                wire.WasCritical = damage.Result.WasCritical ? (byte)1 : (byte)0;
                wire.WasKilled = damage.Result.WasKilled ? (byte)1 : (byte)0;
                wire.Flags = (byte)damage.Flags;
                SetPosition(ref wire, damage.Hit.Position);
                wire.Tag = ToFixedString(damage.Hit.DamageTag.Value);
                return true;
            }

            if (@event is HealingConfirmedEvent healing)
            {
                wire.Kind = GameplayEventKind.HealingConfirmed;
                wire.SourceId = healing.SourceId.Value;
                wire.TargetId = healing.TargetId.Value;
                wire.Requested = healing.Result.RequestedHealing;
                wire.Applied = healing.Result.AppliedHealing;
                return true;
            }

            if (@event is EntityDiedEvent died)
            {
                wire.Kind = GameplayEventKind.EntityDied;
                wire.EntityId = died.EntityId.Value;
                wire.KillerId = died.KillerId.Value;
                SetPosition(ref wire, died.Position);
                return true;
            }

            if (@event is GameplayCueEvent cue)
            {
                wire.Kind = GameplayEventKind.GameplayCue;
                wire.TargetId = cue.TargetId.Value;
                wire.Phase = (byte)cue.Phase;
                wire.Tag = ToFixedString(cue.Cue.Value);
                SetPosition(ref wire, cue.Position);
                return true;
            }

            if (@event is EnemyDeathEvent enemyDeath)
            {
                wire.Kind = GameplayEventKind.EnemyDeath;
                wire.EnemyId = enemyDeath.EnemyId.Value;
                wire.KillerId = enemyDeath.KillerId.Value;
                SetPosition(ref wire, enemyDeath.Position);
                return true;
            }

            if (@event is PlayerDeathEvent playerDeath)
            {
                wire.Kind = GameplayEventKind.PlayerDeath;
                wire.PlayerId = playerDeath.PlayerId.Value;
                wire.KillerId = playerDeath.KillerId.IsValid ? playerDeath.KillerId.Value : 0UL;
                return true;
            }

            if (@event is BossPhaseChangedEvent phaseChanged)
            {
                wire.Kind = GameplayEventKind.BossPhaseChanged;
                wire.BossId = phaseChanged.BossId.Value;
                wire.ExtraA = (byte)phaseChanged.Previous;
                wire.ExtraB = (byte)phaseChanged.Current;
                return true;
            }

            if (@event is BossAttackTelegraphEvent telegraph)
            {
                wire.Kind = GameplayEventKind.BossAttackTelegraph;
                wire.BossId = telegraph.BossId.Value;
                wire.ExtraA = (byte)telegraph.AttackId;
                wire.Duration = telegraph.Duration;
                wire.Tag = ToFixedString(telegraph.Cue.Value);
                SetPosition(ref wire, telegraph.Origin);
                SetPosition2(ref wire, telegraph.Target);
                return true;
            }

            if (@event is BossAttackCueEvent attackCue)
            {
                wire.Kind = GameplayEventKind.BossAttackCue;
                wire.BossId = attackCue.BossId.Value;
                wire.ExtraA = (byte)attackCue.AttackId;
                wire.ExtraB = (byte)attackCue.Phase;
                wire.Tag = ToFixedString(attackCue.Cue.Value);
                return true;
            }

            if (@event is BossDefeatedEvent bossDefeated)
            {
                wire.Kind = GameplayEventKind.BossDefeated;
                wire.BossId = bossDefeated.BossId.Value;
                wire.KillerId = bossDefeated.KillerId.Value;
                return true;
            }

            // Preserve only event metadata for local compatibility.  A
            // receiver can display/log this opaque event, but no feature
            // handler can mistake it for a known gameplay event.
            wire.Kind = GameplayEventKind.OpaqueMetadata;
            return true;
        }

        public bool TryToDomain(out IGameplayEvent @event)
        {
            @event = null;
            if (!IsVersionSupported || !IsValidTimestamp()) return false;

            try
            {
                switch (Kind)
                {
                    case GameplayEventKind.DamageConfirmed:
                        if (!TryIds(SourceId, TargetId, out EntityId damageSource, out EntityId damageTarget)) return false;
                        @event = new DamageConfirmedEvent(
                            EventId,
                            OccurredAtMilliseconds / 1000d,
                            damageSource,
                            damageTarget,
                            new DamageResult(
                                Math.Max(0, Requested),
                                Math.Max(0, Applied),
                                WasCritical != 0,
                                WasKilled != 0,
                                ReadPosition()),
                            (DamageFlags)Flags,
                            new HitContext(ReadPosition(), new DamageTag(Tag.ToString())));
                        return true;

                    case GameplayEventKind.HealingConfirmed:
                        if (!TryIds(SourceId, TargetId, out EntityId healingSource, out EntityId healingTarget)) return false;
                        @event = new HealingConfirmedEvent(
                            EventId,
                            OccurredAtMilliseconds / 1000d,
                            healingSource,
                            healingTarget,
                            new HealingResult(
                                healingSource,
                                healingTarget,
                                Math.Max(0, Requested),
                                Math.Max(0, Applied)));
                        return true;

                    case GameplayEventKind.EntityDied:
                        if (!TryIds(EntityId, KillerId, out EntityId deadEntity, out EntityId deadKiller)) return false;
                        @event = new EntityDiedEvent(
                            EventId,
                            OccurredAtMilliseconds / 1000d,
                            deadEntity,
                            deadKiller,
                            ReadPosition());
                        return true;

                    case GameplayEventKind.GameplayCue:
                        if (!VampireHunt.Core.EntityId.TryCreate(TargetId, out EntityId cueTarget)) return false;
                        string cueValue = Tag.ToString();
                        if (string.IsNullOrWhiteSpace(cueValue) ||
                            !Enum.IsDefined(typeof(GameplayCuePhase), (int)Phase)) return false;
                        @event = new GameplayCueEvent(
                            EventId,
                            OccurredAtMilliseconds / 1000d,
                            new CueId(cueValue),
                            cueTarget,
                            ReadPosition(),
                            (GameplayCuePhase)Phase);
                        return true;

                    case GameplayEventKind.EnemyDeath:
                        if (!TryIds(EnemyId, KillerId, out EntityId enemyId, out EntityId enemyKiller)) return false;
                        @event = new EnemyDeathEvent(
                            EventId,
                            OccurredAtMilliseconds / 1000d,
                            enemyId,
                            enemyKiller,
                            ReadPosition());
                        return true;

                    case GameplayEventKind.PlayerDeath:
                        if (!VampireHunt.Core.EntityId.TryCreate(PlayerId, out EntityId playerId)) return false;
                        VampireHunt.Core.EntityId.TryCreate(KillerId, out EntityId playerKiller);
                        @event = new PlayerDeathEvent(
                            EventId,
                            OccurredAtMilliseconds / 1000d,
                            playerId,
                            playerKiller);
                        return true;

                    case GameplayEventKind.BossPhaseChanged:
                        if (!VampireHunt.Core.EntityId.TryCreate(BossId, out EntityId phaseBoss) ||
                            !Enum.IsDefined(typeof(BossPhase), (int)ExtraA) ||
                            !Enum.IsDefined(typeof(BossPhase), (int)ExtraB)) return false;
                        @event = new BossPhaseChangedEvent(
                            EventId,
                            OccurredAtMilliseconds / 1000d,
                            phaseBoss,
                            (BossPhase)ExtraA,
                            (BossPhase)ExtraB);
                        return true;

                    case GameplayEventKind.BossAttackTelegraph:
                        if (!VampireHunt.Core.EntityId.TryCreate(BossId, out EntityId telegraphBoss) ||
                            !Enum.IsDefined(typeof(BossAttackId), (int)ExtraA) ||
                            (BossAttackId)ExtraA == BossAttackId.None ||
                            string.IsNullOrWhiteSpace(Tag.ToString())) return false;
                        @event = new BossAttackTelegraphEvent(
                            EventId,
                            OccurredAtMilliseconds / 1000d,
                            telegraphBoss,
                            (BossAttackId)ExtraA,
                            ReadPosition(),
                            ReadPosition2(),
                            Math.Max(0f, Duration),
                            new PresentationCueId(Tag.ToString()));
                        return true;

                    case GameplayEventKind.BossAttackCue:
                        if (!VampireHunt.Core.EntityId.TryCreate(BossId, out EntityId attackBoss) ||
                            !Enum.IsDefined(typeof(BossAttackId), (int)ExtraA) ||
                            (BossAttackId)ExtraA == BossAttackId.None ||
                            !Enum.IsDefined(typeof(BossAttackCuePhase), (int)ExtraB) ||
                            string.IsNullOrWhiteSpace(Tag.ToString())) return false;
                        @event = new BossAttackCueEvent(
                            EventId,
                            OccurredAtMilliseconds / 1000d,
                            attackBoss,
                            (BossAttackId)ExtraA,
                            (BossAttackCuePhase)ExtraB,
                            new PresentationCueId(Tag.ToString()));
                        return true;

                    case GameplayEventKind.BossDefeated:
                        if (!TryIds(BossId, KillerId, out EntityId defeatedBoss, out EntityId defeatedKiller)) return false;
                        @event = new BossDefeatedEvent(
                            EventId,
                            OccurredAtMilliseconds / 1000d,
                            defeatedBoss,
                            defeatedKiller);
                        return true;

                    case GameplayEventKind.OpaqueMetadata:
                        @event = new OpaqueGameplayEvent(EventId, OccurredAtMilliseconds / 1000d);
                        return true;

                    default:
                        return false;
                }
            }
            catch (ArgumentException)
            {
                @event = null;
                return false;
            }
            catch (OverflowException)
            {
                @event = null;
                return false;
            }
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref ProtocolVersion);
            serializer.SerializeValue(ref Kind);
            serializer.SerializeValue(ref EventId);
            serializer.SerializeValue(ref OccurredAtMilliseconds);
            serializer.SerializeValue(ref SourceId);
            serializer.SerializeValue(ref TargetId);
            serializer.SerializeValue(ref EntityId);
            serializer.SerializeValue(ref KillerId);
            serializer.SerializeValue(ref BossId);
            serializer.SerializeValue(ref PlayerId);
            serializer.SerializeValue(ref EnemyId);
            serializer.SerializeValue(ref PositionX);
            serializer.SerializeValue(ref PositionY);
            serializer.SerializeValue(ref PositionZ);
            serializer.SerializeValue(ref PositionX2);
            serializer.SerializeValue(ref PositionY2);
            serializer.SerializeValue(ref PositionZ2);
            serializer.SerializeValue(ref Requested);
            serializer.SerializeValue(ref Applied);
            serializer.SerializeValue(ref Secondary);
            serializer.SerializeValue(ref Duration);
            serializer.SerializeValue(ref Flags);
            serializer.SerializeValue(ref Phase);
            serializer.SerializeValue(ref WasCritical);
            serializer.SerializeValue(ref WasKilled);
            serializer.SerializeValue(ref ExtraA);
            serializer.SerializeValue(ref ExtraB);
            serializer.SerializeValue(ref Tag);
        }

        private bool IsValidTimestamp() => true;

        private WorldPosition ReadPosition() => new(
            PositionX / (float)NetworkProtocol.PositionScale,
            PositionY / (float)NetworkProtocol.PositionScale,
            PositionZ / (float)NetworkProtocol.PositionScale);

        private WorldPosition ReadPosition2() => new(
            PositionX2 / (float)NetworkProtocol.PositionScale,
            PositionY2 / (float)NetworkProtocol.PositionScale,
            PositionZ2 / (float)NetworkProtocol.PositionScale);

        private static bool TryIds(ulong first, ulong second, out EntityId firstId, out EntityId secondId)
        {
            if (!VampireHunt.Core.EntityId.TryCreate(first, out firstId))
            {
                secondId = default;
                return false;
            }

            return VampireHunt.Core.EntityId.TryCreate(second, out secondId);
        }

        private static void SetPosition(ref GameplayEventWire wire, WorldPosition position)
        {
            wire.PositionX = Quantize(position.X);
            wire.PositionY = Quantize(position.Y);
            wire.PositionZ = Quantize(position.Z);
        }

        private static void SetPosition2(ref GameplayEventWire wire, WorldPosition position)
        {
            wire.PositionX2 = Quantize(position.X);
            wire.PositionY2 = Quantize(position.Y);
            wire.PositionZ2 = Quantize(position.Z);
        }

        private static int Quantize(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return 0;
            double scaled = value * NetworkProtocol.PositionScale;
            if (scaled >= int.MaxValue) return int.MaxValue;
            if (scaled <= int.MinValue) return int.MinValue;
            return (int)Math.Round(scaled, MidpointRounding.AwayFromZero);
        }

        private static long QuantizeMilliseconds(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0L;
            double scaled = value * 1000d;
            if (scaled >= long.MaxValue) return long.MaxValue;
            if (scaled <= long.MinValue) return long.MinValue;
            return (long)Math.Round(scaled, MidpointRounding.AwayFromZero);
        }

        private static FixedString64Bytes ToFixedString(string value) =>
            new FixedString64Bytes(value ?? string.Empty);
    }

    /// <summary>Metadata-only fallback for unsupported local event classes.</summary>
    public sealed class OpaqueGameplayEvent : GameplayEventBase
    {
        public OpaqueGameplayEvent(ulong eventId, double occurredAt)
            : base(eventId, occurredAt)
        {
        }
    }
}
