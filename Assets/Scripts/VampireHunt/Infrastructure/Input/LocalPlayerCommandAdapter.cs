using System;
using VampireHunt.Player.Contracts;

namespace VampireHunt.Infrastructure.Input
{
    public sealed class LocalPlayerCommandAdapter : IPlayerCommandGateway
    {
        private readonly IPlayerCommandHandler handler;

        public LocalPlayerCommandAdapter(IPlayerCommandHandler handler)
        {
            this.handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public CommandResult SubmitDash(DashCommand command) => handler.Handle(command);
        public CommandResult SubmitAttack(AttackCommand command) => handler.Handle(command);
        public CommandResult SelectBloodPact(SelectBloodPactCommand command) => handler.Handle(command);
    }
}
