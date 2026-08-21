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
        private readonly Rigidbody body;
        private readonly Func<float> moveSpeedSource;
        private readonly Func<float> dashSpeedMultiplierSource;
        private readonly float moveSpeed;
        private readonly float dashSpeedMultiplier;
        private uint poseSequence;

        public OwnerMovementMotor(
            Transform transform,
            EntityId playerId,
            IPlayerPoseTransport poseTransport,
            float moveSpeed = 5f,
            float dashSpeedMultiplier = 2f,
            Func<double> clock = null,
            Rigidbody body = null,
            Func<float> moveSpeedSource = null,
            Func<float> dashSpeedMultiplierSource = null)
        {
            this.transform = transform ?? throw new ArgumentNullException(nameof(transform));
            if (!playerId.IsValid) throw new ArgumentException("A valid player id is required.", nameof(playerId));
            this.playerId = playerId;
            this.poseTransport = poseTransport ?? throw new ArgumentNullException(nameof(poseTransport));
            this.clock = clock;
            this.body = body;
            this.moveSpeedSource = moveSpeedSource;
            this.dashSpeedMultiplierSource = dashSpeedMultiplierSource;
            this.moveSpeed = Math.Max(0f, moveSpeed);
            this.dashSpeedMultiplier = Math.Max(1f, dashSpeedMultiplier);
        }

        public WorldPosition Position => ToWorldPosition(transform.position);
        public uint LastPoseSequence => poseSequence;

        /// <summary>
        /// Applies one physics-step of planar owner movement. The Rigidbody owns
        /// vertical velocity so gravity, falling and ground contacts are never
        /// replaced by the input adapter.
        /// </summary>
        public void Drive(MoveVector moveInput, float deltaTime)
        {
            MoveVector direction = moveInput.IsFinite ? moveInput.ClampMagnitude(1f) : default;
            Vector3 delta = new(direction.X, direction.Y, direction.Z);
            Vector3 planarVelocity = Vector3.zero;
            if (delta.sqrMagnitude > 0.000001f)
            {
                delta.Normalize();
                planarVelocity = Vector3.ProjectOnPlane(delta, Vector3.up) * CurrentMoveSpeed();
            }
            ApplyPlanarVelocity(planarVelocity, deltaTime);
            SubmitPose(direction, false);
        }

        /// <summary>Executes one dash step and reports it as a dash pose.</summary>
        public void Dash(MoveVector direction, float deltaTime)
        {
            MoveVector safe = direction.IsFinite ? direction.ClampMagnitude(1f) : default;
            Vector3 delta = new(safe.X, safe.Y, safe.Z);
            Vector3 planarVelocity = Vector3.zero;
            if (delta.sqrMagnitude > 0.000001f)
            {
                delta.Normalize();
                planarVelocity = Vector3.ProjectOnPlane(delta, Vector3.up) *
                    CurrentMoveSpeed() * CurrentDashSpeedMultiplier();
            }
            ApplyPlanarVelocity(planarVelocity, deltaTime);
            SubmitPose(safe, true);
        }

        /// <summary>
        /// Reports a pose after a compatibility motor (knockback/root motion)
        /// moved the Transform without applying a second movement rule.
        /// </summary>
        public void ReportPose(MoveVector facing, bool isDashing = false) =>
            SubmitPose(facing.IsFinite ? facing : default, isDashing);

        private void SubmitPose(MoveVector facing, bool dashing)
        {
            poseSequence = poseSequence == uint.MaxValue ? 1U : poseSequence + 1U;
            poseTransport.SubmitPose(playerId, new MovementPose(
                Position, facing, clock?.Invoke() ?? Time.timeAsDouble, poseSequence, dashing));
        }

        private float CurrentMoveSpeed()
        {
            float value = moveSpeedSource?.Invoke() ?? moveSpeed;
            return IsFinite(value) && value >= 0f ? value : moveSpeed;
        }

        private float CurrentDashSpeedMultiplier()
        {
            float value = dashSpeedMultiplierSource?.Invoke() ?? dashSpeedMultiplier;
            return IsFinite(value) && value >= 1f ? value : dashSpeedMultiplier;
        }

        private void ApplyPlanarVelocity(Vector3 velocity, float deltaTime)
        {
            if (body != null && !body.isKinematic)
            {
                Vector3 current = body.linearVelocity;
                body.linearVelocity = new Vector3(velocity.x, current.y, velocity.z);
                return;
            }

            float dt = IsFinite(deltaTime) && deltaTime >= 0f ? deltaTime : 0f;
            transform.position += new Vector3(velocity.x, 0f, velocity.z) * dt;
        }

        private static bool IsFinite(float value) =>
            !float.IsNaN(value) && !float.IsInfinity(value);

        private static WorldPosition ToWorldPosition(Vector3 value) => new(value.x, value.y, value.z);
    }
}
