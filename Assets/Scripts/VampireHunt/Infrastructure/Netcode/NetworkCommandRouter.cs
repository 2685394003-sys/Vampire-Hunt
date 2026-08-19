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
                NetworkCommandKind.Dash => envelope.TryGetDash(out DashCommand dash)
                    ? Route(envelope.SenderId, dash)
                    : Invalid(envelope.Sequence, "Invalid Dash wire payload"),
                NetworkCommandKind.Attack => envelope.TryGetAttack(out AttackCommand attack)
                    ? Route(envelope.SenderId, attack)
                    : Invalid(envelope.Sequence, "Invalid Attack wire payload"),
                NetworkCommandKind.SelectBloodPact => envelope.TryGetSelectBloodPact(out SelectBloodPactCommand bloodPact)
                    ? Route(envelope.SenderId, bloodPact)
                    : Invalid(envelope.Sequence, "Invalid Blood Pact wire payload"),
                _ => new CommandResult(CommandResultStatus.Invalid, "Unknown network command", envelope.Sequence)
            };
        }

        private static CommandResult Invalid(uint sequence, string reason) =>
            new CommandResult(CommandResultStatus.Invalid, reason, sequence);
    }
}
