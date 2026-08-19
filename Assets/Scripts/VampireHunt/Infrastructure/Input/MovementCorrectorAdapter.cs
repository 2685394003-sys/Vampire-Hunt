using System;
using UnityEngine;
using VampireHunt.Core;
using VampireHunt.Infrastructure.Input.Contracts;
using VampireHunt.Player.Application;
using EntityId = VampireHunt.Core.EntityId;
using VampireHunt.Player.Contracts;

namespace VampireHunt.Infrastructure.Input
{
    public sealed class MovementCorrectorAdapter : IMovementCorrectionPort, IMovementCorrector
    {
        private readonly IPlayerTransformResolver transforms;

        public MovementCorrectorAdapter(IPlayerTransformResolver transforms)
        {
            this.transforms = transforms ?? throw new ArgumentNullException(nameof(transforms));
        }

        public void ForcePose(EntityId playerId, MovementPose pose)
        {
            if (!transforms.TryGet(playerId, out Transform transform) || transform == null || !pose.IsFinite)
                return;
            transform.position = new Vector3(pose.Position.X, pose.Position.Y, pose.Position.Z);
            Vector3 facing = new(pose.Facing.X, pose.Facing.Y, pose.Facing.Z);
            if (facing.sqrMagnitude > 0.000001f) transform.forward = facing.normalized;
        }
    }
}
