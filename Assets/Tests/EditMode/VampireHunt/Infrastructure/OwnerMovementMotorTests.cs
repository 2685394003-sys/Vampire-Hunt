using NUnit.Framework;
using UnityEngine;
using VampireHunt.Core;
using VampireHunt.Infrastructure.Input;
using VampireHunt.Infrastructure.Input.Contracts;
using VampireHunt.Player.Contracts;
using EntityId = VampireHunt.Core.EntityId;

namespace VampireHunt.Tests.Infrastructure
{
    public sealed class OwnerMovementMotorTests
    {
        [Test]
        public void Drive_ChangesPlanarVelocityAndPreservesGravityVelocity()
        {
            GameObject player = new("OwnerMovementMotorTest");
            try
            {
                Rigidbody body = player.AddComponent<Rigidbody>();
                body.useGravity = true;
                body.linearVelocity = new Vector3(2f, -7f, 3f);
                RecordingPoseTransport poses = new();
                OwnerMovementMotor motor = new(
                    player.transform,
                    new EntityId(1UL),
                    poses,
                    moveSpeed: 5f,
                    body: body);

                motor.Drive(new MoveVector(1f, 0f), 0.02f);

                Assert.That(body.linearVelocity.x, Is.EqualTo(5f).Within(0.0001f));
                Assert.That(body.linearVelocity.y, Is.EqualTo(-7f).Within(0.0001f),
                    "The FixedUpdate motor must leave gravity-owned vertical velocity untouched.");
                Assert.That(body.linearVelocity.z, Is.EqualTo(0f).Within(0.0001f));
                Assert.That(poses.Submissions, Is.EqualTo(1));

                motor.Drive(default, 0.02f);

                Assert.That(body.linearVelocity.x, Is.EqualTo(0f).Within(0.0001f));
                Assert.That(body.linearVelocity.y, Is.EqualTo(-7f).Within(0.0001f));
                Assert.That(body.linearVelocity.z, Is.EqualTo(0f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(player);
            }
        }

        private sealed class RecordingPoseTransport : IPlayerPoseTransport
        {
            public int Submissions { get; private set; }

            public void SubmitPose(EntityId playerId, MovementPose pose) => Submissions++;
        }
    }
}
