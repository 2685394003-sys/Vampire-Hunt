using Unity.Netcode;
using VampireHunt.Core;
using VampireHunt.Infrastructure.Netcode.Contracts;
using VampireHunt.Player.Contracts;
using EntityId = VampireHunt.Core.EntityId;

namespace VampireHunt.Infrastructure.Netcode.Player
{
    /// <summary>
    /// NGO boundary for owner-authoritative player pose replication.
    ///
    /// The owner only submits a versioned pose DTO.  The server resolves the
    /// sender from RpcParams, validates the logical id through the injected
    /// ownership directory, and records the pose for server-side consumers.
    /// This type intentionally contains no movement rule.
    /// </summary>
    public abstract class PlayerPoseNetworkAdapter : NetworkBehaviour
    {
        private IPlayerRuntimePort runtime;
        private INetworkCommandOwnership poseOwnership;

        protected void BindPoseRuntime(IPlayerRuntimePort next) => runtime = next;

        protected void BindPoseOwnership(INetworkCommandOwnership ownership) => poseOwnership = ownership;

        /// <summary>
        /// Sends an owner pose or runs the same endpoint directly in offline
        /// mode.  Callers must have already applied their local prediction;
        /// this method never grants client-side authority over the server.
        /// </summary>
        protected bool SubmitOwnerPose(EntityId playerId, MovementPose pose)
        {
            if (!IsNetworkSessionActive)
            {
                if (runtime == null || playerId != runtime.PlayerId) return false;
                ApplyPose(playerId, pose);
                return true;
            }

            // Remote owners intentionally do not receive a local runtime
            // endpoint. The server validates the logical id and sender after
            // decoding the wire; only an already-bound local endpoint is
            // checked here when one exists (host/offline compatibility).
            if (!IsSpawned || !IsOwner || !playerId.IsValid ||
                (runtime != null && playerId != runtime.PlayerId))
                return false;
            if (!MovementPoseWire.TryFrom(playerId, pose, out MovementPoseWire wire))
                return false;

            SubmitPoseRpc(wire, default(RpcParams));
            return true;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner, Delivery = RpcDelivery.Unreliable)]
        private void SubmitPoseRpc(MovementPoseWire wire, RpcParams rpcParams)
        {
            if (!IsServer || runtime == null || poseOwnership == null ||
                !wire.TryToPose(out EntityId playerId, out MovementPose pose))
                return;

            ulong senderId = rpcParams.Receive.SenderClientId;
            if (playerId != runtime.PlayerId || !poseOwnership.Owns(senderId, playerId))
                return;

            ApplyPose(playerId, pose);
        }

        private void ApplyPose(EntityId playerId, MovementPose pose)
        {
            if (runtime == null || playerId != runtime.PlayerId) return;
            runtime.SubmitPose(pose);
        }

        private bool IsNetworkSessionActive =>
            NetworkManager != null && NetworkManager.IsListening;
    }
}
