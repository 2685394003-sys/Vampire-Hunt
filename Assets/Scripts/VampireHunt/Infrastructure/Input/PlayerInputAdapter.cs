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
        private readonly InputAction moveInput;
        private readonly InputAction dashInput;
        private readonly InputAction attackInput;
        private readonly OwnerMovementMotor movementMotor;
        private readonly IPlayerCommandGateway commandGateway;
        private readonly EntityId playerId;
        private readonly IAimWorldPositionSource aimSource;
        private readonly Func<MoveVector, MoveVector> moveResolver;
        private readonly Action attackAccepted;
        private uint commandSequence;

        public PlayerInputAdapter(
            EntityId playerId,
            OwnerMovementMotor movementMotor,
            IPlayerCommandGateway commandGateway,
            InputActionReference moveAction = null,
            InputActionReference dashAction = null,
            InputActionReference attackAction = null,
            IAimWorldPositionSource aimSource = null,
            Func<MoveVector, MoveVector> moveResolver = null,
            Action attackAccepted = null)
        {
            if (!playerId.IsValid) throw new ArgumentException("A valid player id is required.", nameof(playerId));
            this.playerId = playerId;
            this.movementMotor = movementMotor;
            this.commandGateway = commandGateway ?? throw new ArgumentNullException(nameof(commandGateway));
            this.moveAction = moveAction;
            this.dashAction = dashAction;
            this.attackAction = attackAction;
            this.moveInput = null;
            this.dashInput = null;
            this.attackInput = null;
            this.aimSource = aimSource;
            this.moveResolver = moveResolver;
            this.attackAccepted = attackAccepted;
        }

        /// <summary>
        /// Compatibility overload for prefabs that still serialize InputAction
        /// fields directly on a MonoBehaviour. The input system adapter remains
        /// the only class that samples those actions for gameplay commands.
        /// </summary>
        public PlayerInputAdapter(
            EntityId playerId,
            OwnerMovementMotor movementMotor,
            IPlayerCommandGateway commandGateway,
            InputAction moveAction,
            InputAction dashAction = null,
            InputAction attackAction = null,
            IAimWorldPositionSource aimSource = null,
            Func<MoveVector, MoveVector> moveResolver = null,
            Action attackAccepted = null)
        {
            if (!playerId.IsValid) throw new ArgumentException("A valid player id is required.", nameof(playerId));
            this.playerId = playerId;
            this.movementMotor = movementMotor;
            this.commandGateway = commandGateway ?? throw new ArgumentNullException(nameof(commandGateway));
            this.moveAction = null;
            this.dashAction = null;
            this.attackAction = null;
            this.moveInput = moveAction;
            this.dashInput = dashAction;
            this.attackInput = attackAction;
            this.aimSource = aimSource;
            this.moveResolver = moveResolver;
            this.attackAccepted = attackAccepted;
        }

        public MoveVector SampleInput()
        {
            InputAction action = moveInput ?? moveAction?.action;
            Vector2 input = action == null ? Vector2.zero : action.ReadValue<Vector2>();
            return new MoveVector(input.x, input.y);
        }

        public void Tick()
        {
            MoveVector input = SampleInput();
            MoveVector motorInput = moveResolver?.Invoke(input) ?? input;
            if (WasPressed(dashInput ?? dashAction?.action))
            {
                commandGateway.SubmitDash(new DashCommand(
                    playerId,
                    motorInput,
                    NextSequence()));
            }
            if (WasPressed(attackInput ?? attackAction?.action))
            {
                WorldPosition aim = aimSource?.CurrentAim(playerId) ??
                    (movementMotor == null ? WorldPosition.Origin : movementMotor.Position);
                CommandResult result = commandGateway.SubmitAttack(new AttackCommand(playerId, aim, NextSequence()));
                if (result.Accepted) attackAccepted?.Invoke();
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

        private static bool WasPressed(InputAction action) =>
            action != null && action.WasPressedThisFrame();
    }
}
