using System;
using Unity.Netcode;
using VampireHunt.Core;
using VampireHunt.Player.Contracts;

namespace VampireHunt.Infrastructure.Netcode.Contracts
{
    /// <summary>
    /// Fixed-shape, versioned owner-pose payload. Sender identity is never
    /// serialized; NGO's RpcParams remains the only source of sender identity.
    /// </summary>
    public struct MovementPoseWire : INetworkSerializable
    {
        public ushort ProtocolVersion;
        public ulong PlayerId;
        public float PositionX;
        public float PositionY;
        public float PositionZ;
        public float FacingX;
        public float FacingY;
        public float FacingZ;
        public long ReportedAtMilliseconds;
        public uint Sequence;
        public byte IsDashing;

        public static bool TryFrom(EntityId playerId, MovementPose pose, out MovementPoseWire wire)
        {
            wire = default;
            if (!playerId.IsValid || !pose.IsFinite ||
                !TryQuantizeMilliseconds(pose.ReportedAt, out long reportedAtMilliseconds))
                return false;

            wire = new MovementPoseWire
            {
                ProtocolVersion = NetworkProtocol.CurrentVersion,
                PlayerId = playerId.Value,
                PositionX = pose.Position.X,
                PositionY = pose.Position.Y,
                PositionZ = pose.Position.Z,
                FacingX = pose.Facing.X,
                FacingY = pose.Facing.Y,
                FacingZ = pose.Facing.Z,
                ReportedAtMilliseconds = reportedAtMilliseconds,
                Sequence = pose.Sequence,
                IsDashing = pose.IsDashing ? (byte)1 : (byte)0
            };
            return true;
        }

        public bool TryToPose(out EntityId playerId, out MovementPose pose)
        {
            playerId = default;
            pose = default;
            if (ProtocolVersion != NetworkProtocol.CurrentVersion ||
                !EntityId.TryCreate(PlayerId, out playerId) ||
                !IsFinite(PositionX) || !IsFinite(PositionY) || !IsFinite(PositionZ) ||
                !IsFinite(FacingX) || !IsFinite(FacingY) || !IsFinite(FacingZ))
                return false;

            pose = new MovementPose(
                new WorldPosition(PositionX, PositionY, PositionZ),
                new MoveVector(FacingX, FacingY, FacingZ),
                ReportedAtMilliseconds / 1000d,
                Sequence,
                IsDashing != 0);
            return pose.IsFinite;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref ProtocolVersion);
            serializer.SerializeValue(ref PlayerId);
            serializer.SerializeValue(ref PositionX);
            serializer.SerializeValue(ref PositionY);
            serializer.SerializeValue(ref PositionZ);
            serializer.SerializeValue(ref FacingX);
            serializer.SerializeValue(ref FacingY);
            serializer.SerializeValue(ref FacingZ);
            serializer.SerializeValue(ref ReportedAtMilliseconds);
            serializer.SerializeValue(ref Sequence);
            serializer.SerializeValue(ref IsDashing);
        }

        private static bool TryQuantizeMilliseconds(double seconds, out long milliseconds)
        {
            milliseconds = 0L;
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) return false;
            double scaled = seconds * 1000d;
            if (scaled < long.MinValue || scaled > long.MaxValue) return false;
            milliseconds = (long)Math.Round(scaled);
            return true;
        }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
