using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using VampireHunt.Boss.Contracts;
using VampireHunt.Core;
using VampireHunt.Enemies.Contracts;
using VampireHunt.Player.Contracts;

namespace VampireHunt.Infrastructure.Netcode.Contracts
{
    /// <summary>Fixed-size NGO representation of PlayerStateDto.</summary>
    public struct PlayerStateWire : INetworkSerializable
    {
        public ushort ProtocolVersion;
        public ulong Id;
        public int Health;
        public int MaxHealth;
        public byte IsAlive;
        public byte IsInvincibleWindow;
        public float Stamina;
        public float MaxStamina;
        public int Scarlet;
        public int Coins;
        public int Level;
        public int Experience;
        public uint LastCommandSequence;
        public byte IsAttacking;
        public long AttackWindowEndsAtMilliseconds;
        public uint StateSequence;
        public byte BloodPactCount;
        public FixedString64Bytes BloodPact0Id;
        public FixedString64Bytes BloodPact1Id;
        public FixedString64Bytes BloodPact2Id;
        public FixedString64Bytes BloodPact3Id;
        public FixedString64Bytes BloodPact4Id;
        public FixedString64Bytes BloodPact5Id;
        public FixedString64Bytes BloodPact6Id;
        public FixedString64Bytes BloodPact7Id;
        public int BloodPact0Stacks;
        public int BloodPact1Stacks;
        public int BloodPact2Stacks;
        public int BloodPact3Stacks;
        public int BloodPact4Stacks;
        public int BloodPact5Stacks;
        public int BloodPact6Stacks;
        public int BloodPact7Stacks;

        public static bool TryFrom(PlayerStateDto dto, out PlayerStateWire wire)
        {
            wire = new PlayerStateWire
            {
                ProtocolVersion = NetworkProtocol.CurrentVersion,
                Id = dto.Id.Value,
                Health = dto.Health,
                MaxHealth = dto.MaxHealth,
                IsAlive = dto.IsAlive ? (byte)1 : (byte)0,
                IsInvincibleWindow = dto.IsInvincibleWindow ? (byte)1 : (byte)0,
                Stamina = dto.Stamina,
                MaxStamina = dto.MaxStamina,
                Scarlet = dto.Scarlet,
                Coins = dto.Coins,
                Level = dto.Level,
                Experience = dto.Experience,
                LastCommandSequence = dto.LastCommandSequence,
                IsAttacking = dto.IsAttacking ? (byte)1 : (byte)0,
                AttackWindowEndsAtMilliseconds = QuantizeMilliseconds(dto.AttackWindowEndsAt),
                StateSequence = dto.StateSequence
            };

            if (dto.BloodPacts == null || dto.BloodPacts.Count > NetworkProtocol.MaxPactCount)
            {
                wire = default;
                return false;
            }

            wire.BloodPactCount = (byte)dto.BloodPacts.Count;
            for (int i = 0; i < dto.BloodPacts.Count; i++)
            {
                BloodPactStack pact = dto.BloodPacts[i];
                if (!TrySetPact(ref wire, i, pact))
                {
                    wire = default;
                    return false;
                }
            }

            if (!dto.Id.IsValid || !IsFinite(dto.Stamina) || !IsFinite(dto.MaxStamina) ||
                !IsFinite(dto.AttackWindowEndsAt))
            {
                wire = default;
                return false;
            }

            return true;
        }

        public bool TryToDto(out PlayerStateDto dto)
        {
            dto = default;
            if (ProtocolVersion != NetworkProtocol.CurrentVersion ||
                !EntityId.TryCreate(Id, out EntityId entityId) ||
                BloodPactCount > NetworkProtocol.MaxPactCount ||
                !IsFinite(Stamina) || !IsFinite(MaxStamina))
                return false;

            List<BloodPactStack> pacts = new(BloodPactCount);
            for (int i = 0; i < BloodPactCount; i++)
            {
                if (!TryGetPact(i, out BloodPactStack pact)) return false;
                pacts.Add(pact);
            }

            PlayerSnapshot snapshot = new(
                entityId,
                Health,
                MaxHealth,
                IsAlive != 0,
                IsInvincibleWindow != 0,
                Stamina,
                MaxStamina,
                Scarlet,
                Coins,
                Level,
                Experience,
                LastCommandSequence,
                IsAttacking != 0,
                AttackWindowEndsAtMilliseconds / 1000d,
                pacts);
            dto = new PlayerStateDto(snapshot, StateSequence);
            return true;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref ProtocolVersion);
            serializer.SerializeValue(ref Id);
            serializer.SerializeValue(ref Health);
            serializer.SerializeValue(ref MaxHealth);
            serializer.SerializeValue(ref IsAlive);
            serializer.SerializeValue(ref IsInvincibleWindow);
            serializer.SerializeValue(ref Stamina);
            serializer.SerializeValue(ref MaxStamina);
            serializer.SerializeValue(ref Scarlet);
            serializer.SerializeValue(ref Coins);
            serializer.SerializeValue(ref Level);
            serializer.SerializeValue(ref Experience);
            serializer.SerializeValue(ref LastCommandSequence);
            serializer.SerializeValue(ref IsAttacking);
            serializer.SerializeValue(ref AttackWindowEndsAtMilliseconds);
            serializer.SerializeValue(ref StateSequence);
            serializer.SerializeValue(ref BloodPactCount);
            serializer.SerializeValue(ref BloodPact0Id);
            serializer.SerializeValue(ref BloodPact1Id);
            serializer.SerializeValue(ref BloodPact2Id);
            serializer.SerializeValue(ref BloodPact3Id);
            serializer.SerializeValue(ref BloodPact4Id);
            serializer.SerializeValue(ref BloodPact5Id);
            serializer.SerializeValue(ref BloodPact6Id);
            serializer.SerializeValue(ref BloodPact7Id);
            serializer.SerializeValue(ref BloodPact0Stacks);
            serializer.SerializeValue(ref BloodPact1Stacks);
            serializer.SerializeValue(ref BloodPact2Stacks);
            serializer.SerializeValue(ref BloodPact3Stacks);
            serializer.SerializeValue(ref BloodPact4Stacks);
            serializer.SerializeValue(ref BloodPact5Stacks);
            serializer.SerializeValue(ref BloodPact6Stacks);
            serializer.SerializeValue(ref BloodPact7Stacks);
        }

        private bool TryGetPact(int index, out BloodPactStack pact)
        {
            FixedString64Bytes id;
            int stacks;
            switch (index)
            {
                case 0: id = BloodPact0Id; stacks = BloodPact0Stacks; break;
                case 1: id = BloodPact1Id; stacks = BloodPact1Stacks; break;
                case 2: id = BloodPact2Id; stacks = BloodPact2Stacks; break;
                case 3: id = BloodPact3Id; stacks = BloodPact3Stacks; break;
                case 4: id = BloodPact4Id; stacks = BloodPact4Stacks; break;
                case 5: id = BloodPact5Id; stacks = BloodPact5Stacks; break;
                case 6: id = BloodPact6Id; stacks = BloodPact6Stacks; break;
                case 7: id = BloodPact7Id; stacks = BloodPact7Stacks; break;
                default: pact = default; return false;
            }

            if (string.IsNullOrWhiteSpace(id.ToString()))
            {
                pact = default;
                return false;
            }

            pact = new BloodPactStack(new BloodPactId(id.ToString()), stacks);
            return true;
        }

        private static bool TrySetPact(ref PlayerStateWire wire, int index, BloodPactStack pact)
        {
            if (!pact.Id.IsValid) return false;
            FixedString64Bytes id;
            try { id = new FixedString64Bytes(pact.Id.Value); }
            catch (ArgumentException) { return false; }

            switch (index)
            {
                case 0: wire.BloodPact0Id = id; wire.BloodPact0Stacks = pact.Stacks; return true;
                case 1: wire.BloodPact1Id = id; wire.BloodPact1Stacks = pact.Stacks; return true;
                case 2: wire.BloodPact2Id = id; wire.BloodPact2Stacks = pact.Stacks; return true;
                case 3: wire.BloodPact3Id = id; wire.BloodPact3Stacks = pact.Stacks; return true;
                case 4: wire.BloodPact4Id = id; wire.BloodPact4Stacks = pact.Stacks; return true;
                case 5: wire.BloodPact5Id = id; wire.BloodPact5Stacks = pact.Stacks; return true;
                case 6: wire.BloodPact6Id = id; wire.BloodPact6Stacks = pact.Stacks; return true;
                case 7: wire.BloodPact7Id = id; wire.BloodPact7Stacks = pact.Stacks; return true;
                default: return false;
            }
        }

        private static long QuantizeMilliseconds(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) return 0L;
            double scaled = value * 1000d;
            if (scaled >= long.MaxValue) return long.MaxValue;
            if (scaled <= long.MinValue) return long.MinValue;
            return (long)Math.Round(scaled, MidpointRounding.AwayFromZero);
        }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool IsFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);
    }

    /// <summary>Fixed-shape NGO representation of EnemyStateDto.</summary>
    public struct EnemyStateWire : INetworkSerializable
    {
        public ushort ProtocolVersion;
        public ulong Id;
        public int Health;
        public int MaxHealth;
        public byte IsAlive;
        public EnemyState State;
        public int PositionX;
        public int PositionY;
        public int PositionZ;
        public ulong TargetId;
        public long LastAttackAtMilliseconds;
        public uint Sequence;
        public byte Dirty;
        public EnemyReplicationTier Tier;
        public int PositionScale;

        public static bool TryFrom(EnemyStateDto dto, out EnemyStateWire wire)
        {
            wire = default;
            if (!dto.Id.IsValid ||
                !Enum.IsDefined(typeof(EnemyState), dto.State) ||
                !Enum.IsDefined(typeof(EnemyReplicationTier), dto.Tier) ||
                dto.PositionScale <= 0)
                return false;

            wire = From(dto);
            return true;
        }

        public static EnemyStateWire From(EnemyStateDto dto)
        {
            return new EnemyStateWire
            {
                ProtocolVersion = NetworkProtocol.CurrentVersion,
                Id = dto.Id.Value,
                Health = dto.Health,
                MaxHealth = dto.MaxHealth,
                IsAlive = dto.IsAlive ? (byte)1 : (byte)0,
                State = dto.State,
                PositionX = dto.PositionX,
                PositionY = dto.PositionY,
                PositionZ = dto.PositionZ,
                TargetId = dto.TargetId.Value,
                LastAttackAtMilliseconds = dto.LastAttackAtMilliseconds,
                Sequence = dto.Sequence,
                Dirty = dto.Dirty ? (byte)1 : (byte)0,
                Tier = dto.Tier,
                PositionScale = dto.PositionScale
            };
        }

        public bool TryToDto(out EnemyStateDto dto)
        {
            dto = default;
            EntityId targetId = default;
            bool targetIsValid = TargetId == 0UL || EntityId.TryCreate(TargetId, out targetId);
            if (ProtocolVersion != NetworkProtocol.CurrentVersion ||
                !EntityId.TryCreate(Id, out EntityId id) ||
                !targetIsValid ||
                !Enum.IsDefined(typeof(EnemyState), State) ||
                !Enum.IsDefined(typeof(EnemyReplicationTier), Tier) ||
                PositionScale <= 0)
                return false;

            dto = new EnemyStateDto(
                id,
                Health,
                MaxHealth,
                IsAlive != 0,
                State,
                PositionX,
                PositionY,
                PositionZ,
                targetId,
                LastAttackAtMilliseconds,
                Sequence,
                Dirty != 0,
                Tier,
                PositionScale);
            return true;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref ProtocolVersion);
            serializer.SerializeValue(ref Id);
            serializer.SerializeValue(ref Health);
            serializer.SerializeValue(ref MaxHealth);
            serializer.SerializeValue(ref IsAlive);
            serializer.SerializeValue(ref State);
            serializer.SerializeValue(ref PositionX);
            serializer.SerializeValue(ref PositionY);
            serializer.SerializeValue(ref PositionZ);
            serializer.SerializeValue(ref TargetId);
            serializer.SerializeValue(ref LastAttackAtMilliseconds);
            serializer.SerializeValue(ref Sequence);
            serializer.SerializeValue(ref Dirty);
            serializer.SerializeValue(ref Tier);
            serializer.SerializeValue(ref PositionScale);
        }
    }

    /// <summary>Fixed-shape NGO representation of BossStateDto.</summary>
    public struct BossStateWire : INetworkSerializable
    {
        public ushort ProtocolVersion;
        public ulong Id;
        public int Health;
        public int MaxHealth;
        public BossPhase Phase;
        public byte IsInvulnerable;
        public BossAttackId CurrentAttack;
        public EncounterMode Mode;
        public StaggerState Stagger;
        public float ContractSeconds;
        public uint Sequence;
        public byte Dirty;

        public static bool TryFrom(BossStateDto dto, out BossStateWire wire)
        {
            wire = default;
            if (!dto.Id.IsValid ||
                !Enum.IsDefined(typeof(BossPhase), dto.Phase) ||
                !Enum.IsDefined(typeof(BossAttackId), dto.CurrentAttack) ||
                !Enum.IsDefined(typeof(EncounterMode), dto.Mode) ||
                !Enum.IsDefined(typeof(StaggerState), dto.Stagger) ||
                float.IsNaN(dto.ContractSeconds) || float.IsInfinity(dto.ContractSeconds))
                return false;

            wire = From(dto);
            return true;
        }

        public static BossStateWire From(BossStateDto dto)
        {
            return new BossStateWire
            {
                ProtocolVersion = NetworkProtocol.CurrentVersion,
                Id = dto.Id.Value,
                Health = dto.Health,
                MaxHealth = dto.MaxHealth,
                Phase = dto.Phase,
                IsInvulnerable = dto.IsInvulnerable ? (byte)1 : (byte)0,
                CurrentAttack = dto.CurrentAttack,
                Mode = dto.Mode,
                Stagger = dto.Stagger,
                ContractSeconds = dto.ContractSeconds,
                Sequence = dto.Sequence,
                Dirty = dto.Dirty ? (byte)1 : (byte)0
            };
        }

        public bool TryToDto(out BossStateDto dto)
        {
            dto = default;
            if (ProtocolVersion != NetworkProtocol.CurrentVersion ||
                !EntityId.TryCreate(Id, out EntityId id) ||
                !Enum.IsDefined(typeof(BossPhase), Phase) ||
                !Enum.IsDefined(typeof(BossAttackId), CurrentAttack) ||
                !Enum.IsDefined(typeof(EncounterMode), Mode) ||
                !Enum.IsDefined(typeof(StaggerState), Stagger) ||
                float.IsNaN(ContractSeconds) || float.IsInfinity(ContractSeconds))
                return false;

            BossSnapshot snapshot = new(
                id,
                Health,
                MaxHealth,
                Phase,
                IsInvulnerable != 0,
                CurrentAttack,
                Mode,
                Stagger,
                ContractSeconds);
            dto = new BossStateDto(snapshot, Sequence, Dirty != 0);
            return true;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref ProtocolVersion);
            serializer.SerializeValue(ref Id);
            serializer.SerializeValue(ref Health);
            serializer.SerializeValue(ref MaxHealth);
            serializer.SerializeValue(ref Phase);
            serializer.SerializeValue(ref IsInvulnerable);
            serializer.SerializeValue(ref CurrentAttack);
            serializer.SerializeValue(ref Mode);
            serializer.SerializeValue(ref Stagger);
            serializer.SerializeValue(ref ContractSeconds);
            serializer.SerializeValue(ref Sequence);
            serializer.SerializeValue(ref Dirty);
        }
    }
}
