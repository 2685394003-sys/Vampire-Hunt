using System;
using UnityEngine;
using VampireHunt.Core;
using VampireHunt.Player.Contracts;
using EntityId = VampireHunt.Core.EntityId;

namespace VampireHunt.Infrastructure.Input.Contracts
{
    public interface IMovementCorrectionPort
    {
        void ForcePose(EntityId playerId, MovementPose pose);
    }

    public interface IPlayerPoseTransport
    {
        void SubmitPose(EntityId playerId, MovementPose pose);
    }

    public interface IPlayerTransformResolver
    {
        bool TryGet(EntityId playerId, out Transform transform);
    }

    public interface IAimWorldPositionSource
    {
        WorldPosition CurrentAim(EntityId playerId);
    }

    public sealed class DelegateAimWorldPositionSource : IAimWorldPositionSource
    {
        private readonly Func<EntityId, WorldPosition> resolver;

        public DelegateAimWorldPositionSource(Func<EntityId, WorldPosition> resolver)
        {
            this.resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        public WorldPosition CurrentAim(EntityId playerId) => resolver(playerId);
    }
}
