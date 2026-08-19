using System;
using UnityEngine;
using VampireHunt.Core;
using VampireHunt.Infrastructure.Input.Contracts;
using VampireHunt.Player.Contracts;
using EntityId = VampireHunt.Core.EntityId;

namespace VampireHunt.Infrastructure.Input
{
    /// <summary>
    /// Owner-authoritative local movement. It emits pose samples; movement is
    /// not represented as a server command (ADR 0001).
    /// </summary>
    public sealed class OwnerMovementMotor
    {
        private readonly Transform transform;
        private readonly EntityId playerId;
        private readonly IPlayerPoseTransport poseTransport;
        private readonly Func<double> clock;
        private readonly float moveSpeed;
        private readonly float dashSpeedMultiplier;
        private uint poseSequence;

        public OwnerMovementMotor(
            Transform transform,
            EntityId playerId,
            IPlayerPoseTransport poseTransport,
            float moveSpeed = 5f,
            float dashSpeedMultiplier = 2f,
            Func<double> clock = null)
        {
            this.transform = transform ?? throw new ArgumentNullException(nameof(transform));
            if (!playerId.IsValid) throw new ArgumentException("A valid player id is required.", nameof(playerId));
            this.playerId = playerId;
            this.poseTransport = poseTransport;
            this.clock = clock;
            this.moveSpeed = Math.Max(0f, moveSpeed);
            this.dashSpeedMultiplier = Math.Max(1f, dashSpeedMultiplier);
        }

        public WorldPosition Position => ToWorldPosition(transform.position);
        public uint LastPoseSequence => poseSequence;

        public void Drive(MoveVector moveInput)
        {
            MoveVector direction = moveInput.IsFinite ? moveInput.ClampMagnitude(1f) : default;
            Vector3 delta = new(direction.X, direction.Y, direction.Z);
            float dt = SafeDeltaTime();
            if (delta.sqrMagnitude > 0.000001f)
            {
                delta.Normalize();
                transform.position += delta * moveSpeed * dt;
                transform.forward = new Vector3(delta.x, 0f, delta.z).sqrMagnitude > 0.000001f
                    ? new Vector3(delta.x, 0f, delta.z).normalized
                    : transform.forward;
            }
            SubmitPose(direction, false);
        }

        public void Dash(MoveVector direction)
        {
            MoveVector safe = direction.IsFinite ? direction.ClampMagnitude(1f) : default;
            Vector3 delta = new(safe.X, safe.Y, safe.Z);
            float dt = SafeDeltaTime();
            if (delta.sqrMagnitude > 0.000001f)
            {
                delta.Normalize();
                transform.position += delta * moveSpeed * dashSpeedMultiplier * dt;
                transform.forward = new Vector3(delta.x, 0f, delta.z).sqrMagnitude > 0.000001f
                    ? new Vector3(delta.x, 0f, delta.z).normalized
                    : transform.forward;
            }
            SubmitPose(safe, true);
        }

        private void SubmitPose(MoveVector facing, bool dashing)
        {
            poseSequence = poseSequence == uint.MaxValue ? 1U : poseSequence + 1U;
            poseTransport?.SubmitPose(playerId, new MovementPose(
                Position, facing, clock?.Invoke() ?? Time.timeAsDouble, poseSequence, dashing));
        }

        private static float SafeDeltaTime()
        {
            float value = Time.deltaTime;
            return float.IsNaN(value) || float.IsInfinity(value) || value < 0f ? 0f : value;
        }

        private static WorldPosition ToWorldPosition(Vector3 value) => new(value.x, value.y, value.z);
    }
}
