using System;
using Unity.Netcode;
using VampireHunt.Core;
using VampireHunt.Core.Contracts;
using VampireHunt.Infrastructure.Netcode.Contracts;
using VampireHunt.Player.Contracts;

namespace VampireHunt.Infrastructure.Netcode
{
    /// <summary>
    /// Actual NGO command ingress.  RPC methods only decode a bounded wire
    /// value, bind the sender supplied by NGO, and invoke the existing router;
    /// gameplay rules remain in the application endpoint.
    /// </summary>
    public sealed class NetworkCommandRpcAdapter : NetworkBehaviour
    {
        private NetworkCommandRouter router;
        private INetworkCommandOwnership ownership;

        public void Configure(NetworkCommandRouter commandRouter, INetworkCommandOwnership commandOwnership)
        {
            router = commandRouter ?? throw new ArgumentNullException(nameof(commandRouter));
            ownership = commandOwnership ?? throw new ArgumentNullException(nameof(commandOwnership));
        }

        public bool IsConfigured => router != null && ownership != null;

        /// <summary>
        /// Queues a command from a local owner.  On a server this takes the
        /// server client id directly; on a client the wire contains no sender
        /// field and the RPC receive context supplies it on the server.
        /// </summary>
        public CommandResult SubmitDash(DashCommand command)
        {
            if (!TryCreate(NetworkCommandWire.From(command), command.Sequence, out CommandResult invalid))
                return invalid;
            NetworkCommandWire wire = NetworkCommandWire.From(command);
            return Submit(wire, command.Sequence);
        }

        public CommandResult SubmitAttack(AttackCommand command)
        {
            if (!TryCreate(NetworkCommandWire.From(command), command.Sequence, out CommandResult invalid))
                return invalid;
            NetworkCommandWire wire = NetworkCommandWire.From(command);
            return Submit(wire, command.Sequence);
        }

        public CommandResult SubmitBloodPact(SelectBloodPactCommand command)
        {
            NetworkCommandWire wire;
            try
            {
                wire = NetworkCommandWire.From(command);
            }
            catch (ArgumentException)
            {
                return Invalid(command.Sequence, "Command field exceeds the fixed wire limit");
            }

            return Submit(wire, command.Sequence);
        }

        /// <summary>
        /// Testable/server transport entry.  The caller must pass the sender
        /// obtained from RpcParams; the wire itself cannot override it.
        /// </summary>
        public bool TryRouteServerWire(
            NetworkCommandWire wire,
            ulong senderId,
            out CommandResult result)
        {
            result = Invalid(wire.Sequence, "Unconfigured command RPC adapter");
            if (!IsConfigured || !wire.IsVersionSupported)
            {
                result = Invalid(wire.Sequence, "Unsupported command protocol version");
                return false;
            }

            if (!wire.TryGetPlayerId(out EntityId playerId) || !ownership.Owns(senderId, playerId))
            {
                result = new CommandResult(CommandResultStatus.Unauthorized, "Sender does not own player", wire.Sequence);
                return false;
            }

            NetworkCommandEnvelope envelope = NetworkCommandEnvelope.FromWire(senderId, wire);
            result = router.Route(envelope);
            return result.Status != CommandResultStatus.Invalid &&
                   result.Status != CommandResultStatus.Unauthorized;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SubmitCommandRpc(NetworkCommandWire wire, RpcParams rpcParams)
        {
            if (!IsServer) return;
            // Never read a sender id from NetworkCommandWire.  NGO supplies
            // this value from the authenticated connection context.
            TryRouteServerWire(wire, rpcParams.Receive.SenderClientId, out _);
        }

        private CommandResult Submit(NetworkCommandWire wire, uint sequence)
        {
            if (!IsConfigured) return Invalid(sequence, "Unconfigured command RPC adapter");

            if (IsServer)
            {
                ulong senderId = Unity.Netcode.NetworkManager.ServerClientId;
                TryRouteServerWire(wire, senderId, out CommandResult result);
                return result;
            }

            if (!IsClient || !IsSpawned)
                return Invalid(sequence, "Network object is not ready");

            SubmitCommandRpc(wire, default(RpcParams));
            // The authoritative result is produced on the server.  This
            // result means only that the bounded RPC was queued locally.
            return CommandResult.Accept(sequence);
        }

        private static bool TryCreate(
            NetworkCommandWire wire,
            uint sequence,
            out CommandResult invalid)
        {
            invalid = default;
            if (!wire.IsVersionSupported)
            {
                invalid = Invalid(sequence, "Unsupported command protocol version");
                return false;
            }

            return true;
        }

        private static CommandResult Invalid(uint sequence, string reason) =>
            new CommandResult(CommandResultStatus.Invalid, reason, sequence);
    }

}
