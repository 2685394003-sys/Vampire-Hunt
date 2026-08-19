using System;
using UnityEngine;
using UnityEngine.InputSystem;
using VampireHunt.Core;
using VampireHunt.Infrastructure.Input.Contracts;
using VampireHunt.Player.Contracts;
using EntityId = VampireHunt.Core.EntityId;

namespace VampireHunt.Infrastructure.Input
{
    /// <summary>Input System adapter; all gameplay effects enter via gateways.</summary>
    public sealed class PlayerInputAdapter
    {
        private readonly InputActionReference moveAction;
        private readonly InputActionReference dashAction;
        private readonly InputActionReference attackAction;
        private readonly OwnerMovementMotor movementMotor;
        private readonly IPlayerCommandGateway commandGateway;
        private readonly EntityId playerId;
        private readonly IAimWorldPositionSource aimSource;
        private uint commandSequence;

        public PlayerInputAdapter(
            EntityId playerId,
            OwnerMovementMotor movementMotor,
            IPlayerCommandGateway commandGateway,
            InputActionReference moveAction = null,
            InputActionReference dashAction = null,
            InputActionReference attackAction = null,
            IAimWorldPositionSource aimSource = null)
        {
            if (!playerId.IsValid) throw new ArgumentException("A valid player id is required.", nameof(playerId));
            this.playerId = playerId;
            this.movementMotor = movementMotor;
            this.commandGateway = commandGateway ?? throw new ArgumentNullException(nameof(commandGateway));
            this.moveAction = moveAction;
            this.dashAction = dashAction;
            this.attackAction = attackAction;
            this.aimSource = aimSource;
        }

        public MoveVector SampleInput()
        {
            Vector2 input = moveAction?.action == null ? Vector2.zero : moveAction.action.ReadValue<Vector2>();
            return new MoveVector(input.x, input.y);
        }

        public void Tick()
        {
            MoveVector input = SampleInput();
            movementMotor?.Drive(input);
            if (WasPressed(dashAction)) movementMotor?.Dash(input);
            if (WasPressed(attackAction))
            {
                WorldPosition aim = aimSource?.CurrentAim(playerId) ??
                    (movementMotor == null ? WorldPosition.Origin : movementMotor.Position);
                commandGateway.SubmitAttack(new AttackCommand(playerId, aim, NextSequence()));
            }
        }

        public CommandResult SubmitDash(MoveVector direction)
        {
            return commandGateway.SubmitDash(new DashCommand(playerId, direction, NextSequence()));
        }

        public CommandResult SubmitAttack(WorldPosition aimAt)
        {
            return commandGateway.SubmitAttack(new AttackCommand(playerId, aimAt, NextSequence()));
        }

        public CommandResult SelectBloodPact(BloodPactId selection, uint offerVersion)
        {
            return commandGateway.SelectBloodPact(new SelectBloodPactCommand(
                playerId, selection, offerVersion, NextSequence()));
        }

        private uint NextSequence() => commandSequence == uint.MaxValue ? 1U : ++commandSequence;

        private static bool WasPressed(InputActionReference reference) =>
            reference?.action != null && reference.action.WasPressedThisFrame();
    }
}
