using System;
using VampireHunt.Player.Contracts;
using VampireHunt.Player.Domain;
using VampireHunt.Core;

namespace VampireHunt.Player.Application
{
    /// <summary>Single server entry for non-movement player commands.</summary>
    public sealed class PlayerCommandService : IPlayerCommandHandler
    {
        private readonly IPlayerRepository repository;
        private readonly IPlayerOwnership ownership;
        private readonly PlayerCombatService combat;
        private readonly PlayerProgressionService progression;
        private readonly BloodPactOfferService bloodPacts;
        private readonly IMovementClock clock;

        public PlayerCommandService(
            IPlayerRepository repository,
            IPlayerOwnership ownership = null,
            PlayerCombatService combat = null,
            PlayerProgressionService progression = null,
            BloodPactOfferService bloodPacts = null,
            IMovementClock clock = null)
        {
            this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
            this.ownership = ownership;
            this.combat = combat;
            this.progression = progression;
            this.bloodPacts = bloodPacts;
            this.clock = clock;
        }

        public PlayerCommandService(
            IPlayerRepository repository,
            IPlayerOwnership ownership,
            PlayerCombatService combat,
            PlayerProgressionService progression,
            BloodPactOfferService bloodPacts,
            IGameClock clock)
            : this(repository, ownership, combat, progression, bloodPacts, new GameClockAdapter(clock))
        {
        }

        public CommandResult Handle(DashCommand command) => Handle(0ul, command);
        public CommandResult Handle(AttackCommand command) => Handle(0ul, command);
        public CommandResult Handle(SelectBloodPactCommand command) => Handle(0ul, command);

        public CommandResult Handle(ulong senderId, DashCommand command)
        {
            if (!TryGetOwned(command.PlayerId, senderId, command.Sequence, out PlayerAggregate player, out CommandResult failure))
                return failure;
            if (!player.TryAcceptCommandSequence(command.Sequence))
                return new CommandResult(CommandResultStatus.StaleSequence, "Command sequence is stale", command.Sequence);
            if (!player.IsAlive)
                return new CommandResult(CommandResultStatus.Dead, "Player is not alive", command.Sequence);
            if (!command.Direction.IsFinite || command.Direction.IsZero)
                return new CommandResult(CommandResultStatus.Invalid, "Dash direction is invalid", command.Sequence);

            double now = clock?.Now ?? 0d;
            if (!player.TryDash(now))
                return new CommandResult(CommandResultStatus.Cooldown, "Dash is unavailable", command.Sequence);
            return CommandResult.Accept(command.Sequence);
        }

        public CommandResult Handle(ulong senderId, AttackCommand command)
        {
            if (!TryGetOwned(command.PlayerId, senderId, command.Sequence, out PlayerAggregate player, out CommandResult failure))
                return failure;
            if (!player.TryAcceptCommandSequence(command.Sequence))
                return new CommandResult(CommandResultStatus.StaleSequence, "Command sequence is stale", command.Sequence);
            if (!player.IsAlive)
                return new CommandResult(CommandResultStatus.Dead, "Player is not alive", command.Sequence);
            if (combat == null)
                return new CommandResult(CommandResultStatus.Invalid, "Combat service is unavailable", command.Sequence);

            AttackResult result = combat.Attack(player, command, clock?.Now ?? 0d);
            return result.Accepted
                ? CommandResult.Accept(command.Sequence)
                : new CommandResult(CommandResultStatus.Rejected, result.Reason, command.Sequence);
        }

        public CommandResult Handle(ulong senderId, SelectBloodPactCommand command)
        {
            if (!TryGetOwned(command.PlayerId, senderId, command.Sequence, out PlayerAggregate player, out CommandResult failure))
                return failure;
            if (!player.TryAcceptCommandSequence(command.Sequence))
                return new CommandResult(CommandResultStatus.StaleSequence, "Command sequence is stale", command.Sequence);
            if (!player.IsAlive)
                return new CommandResult(CommandResultStatus.Dead, "Player is not alive", command.Sequence);
            if (bloodPacts == null)
                return new CommandResult(CommandResultStatus.Invalid, "Blood Pact service is unavailable", command.Sequence);

            SelectionResult result = bloodPacts.Select(
                command.PlayerId,
                command.Selection,
                command.OfferVersion);
            return result.Accepted
                ? CommandResult.Accept(command.Sequence)
                : new CommandResult(MapSelectionFailure(result.Reason), result.Reason.ToString(), command.Sequence);
        }

        public CommandResult GrantReward(EntityId playerId, RewardGrant reward)
        {
            if (progression == null)
                return new CommandResult(CommandResultStatus.Invalid, "Progression service is unavailable");
            return progression.GrantReward(playerId, reward)
                ? CommandResult.Accept()
                : new CommandResult(CommandResultStatus.Rejected, "Reward was rejected");
        }

        private bool TryGetOwned(
            EntityId playerId,
            ulong senderId,
            uint sequence,
            out PlayerAggregate player,
            out CommandResult failure)
        {
            if (!repository.TryGet(playerId, out player))
            {
                failure = new CommandResult(CommandResultStatus.NotFound, "Player was not found", sequence);
                return false;
            }
            if (ownership != null && !ownership.IsOwner(playerId, senderId))
            {
                failure = new CommandResult(CommandResultStatus.Unauthorized, "Sender does not own player", sequence);
                return false;
            }
            failure = default;
            return true;
        }

        private static CommandResultStatus MapSelectionFailure(BloodPactSelectionCode code) => code switch
        {
            BloodPactSelectionCode.Dead => CommandResultStatus.Dead,
            BloodPactSelectionCode.StaleOffer => CommandResultStatus.StaleOffer,
            BloodPactSelectionCode.InsufficientScarlet => CommandResultStatus.InsufficientResource,
            BloodPactSelectionCode.InvalidPlayer => CommandResultStatus.NotFound,
            _ => CommandResultStatus.Rejected
        };

        private sealed class GameClockAdapter : IMovementClock
        {
            private readonly IGameClock clock;
            public GameClockAdapter(IGameClock clock) => this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            public double Now => clock.Now;
        }
    }
}
