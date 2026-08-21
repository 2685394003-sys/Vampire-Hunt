using NUnit.Framework;
using VampireHunt.Core;
using VampireHunt.Infrastructure.Netcode.Contracts;
using VampireHunt.Player.Contracts;

namespace VampireHunt.Tests.Netcode
{
    public sealed class PlayerPoseTransportContractTests
    {
        [Test]
        public void MovementPoseWire_RoundTripsVersionedPose()
        {
            EntityId playerId = new(42UL);
            MovementPose source = new(
                new WorldPosition(1.25f, 0.5f, -3.75f),
                new MoveVector(0f, 0f, 1f),
                reportedAt: 12.3456d,
                sequence: 17U,
                isDashing: true);

            Assert.That(MovementPoseWire.TryFrom(playerId, source, out MovementPoseWire wire), Is.True);
            Assert.That(wire.ProtocolVersion, Is.EqualTo(NetworkProtocol.CurrentVersion));
            Assert.That(wire.TryToPose(out EntityId decodedId, out MovementPose decoded), Is.True);
            Assert.That(decodedId, Is.EqualTo(playerId));
            Assert.That(decoded.Position, Is.EqualTo(source.Position));
            Assert.That(decoded.Facing, Is.EqualTo(source.Facing));
            Assert.That(decoded.ReportedAt, Is.EqualTo(12.346d).Within(0.000001d));
            Assert.That(decoded.Sequence, Is.EqualTo(source.Sequence));
            Assert.That(decoded.IsDashing, Is.True);
        }

        [Test]
        public void MovementPoseWire_RejectsUnsupportedVersionAndNonFinitePose()
        {
            MovementPose source = new(
                WorldPosition.Origin,
                new MoveVector(1f, 0f),
                reportedAt: 1d,
                sequence: 1U);
            Assert.That(MovementPoseWire.TryFrom(new EntityId(7UL), source, out MovementPoseWire wire), Is.True);

            wire.ProtocolVersion++;
            Assert.That(wire.TryToPose(out _, out _), Is.False);
            Assert.That(MovementPoseWire.TryFrom(
                new EntityId(7UL),
                new MovementPose(WorldPosition.Origin, new MoveVector(float.NaN, 0f), 1d, 2U),
                out _), Is.False);
        }
    }
}
