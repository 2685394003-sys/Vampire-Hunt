using System;
using VampireHunt.Infrastructure.Netcode.Contracts;
using VampireHunt.Player.Contracts;

namespace VampireHunt.Infrastructure.Netcode
{
    /// <summary>
    /// The only command ingress used by network adapters. It validates the
    /// command kind and forwards ownership/sequence decisions to the injected
    /// application endpoint; no gameplay rule is duplicated here.
    /// </summary>
    public sealed class NetworkCommandRouter
    {
        private readonly IOwnedPlayerCommandEndpoint ownedEndpoint;
        private readonly IPlayerCommandHandler localEndpoint;

        public NetworkCommandRouter(IOwnedPlayerCommandEndpoint endpoint)
        {
            ownedEndpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        }

        public NetworkCommandRouter(IPlayerCommandHandler endpoint)
        {
            localEndpoint = endpoint ?? throw new ArgumentNullException(nameof(endpoint));
        }

        public CommandResult Route(ulong clientId, DashCommand command)
        {
            return ownedEndpoint != null
                ? ownedEndpoint.Route(clientId, command)
                : localEndpoint.Handle(command);
        }

        public CommandResult Route(ulong clientId, AttackCommand command)
        {
            return ownedEndpoint != null
                ? ownedEndpoint.Route(clientId, command)
                : localEndpoint.Handle(command);
        }

        public CommandResult Route(ulong clientId, SelectBloodPactCommand command)
        {
            return ownedEndpoint != null
                ? ownedEndpoint.Route(clientId, command)
                : localEndpoint.Handle(command);
        }

        public CommandResult Route(NetworkCommandEnvelope envelope)
        {
            return envelope.Kind switch
            {
                NetworkCommandKind.Dash => Route(envelope.SenderId, envelope.Dash),
                NetworkCommandKind.Attack => Route(envelope.SenderId, envelope.Attack),
                NetworkCommandKind.SelectBloodPact => Route(envelope.SenderId, envelope.SelectBloodPact),
                _ => new CommandResult(CommandResultStatus.Invalid, "Unknown network command", envelope.Sequence)
            };
        }
    }
}
