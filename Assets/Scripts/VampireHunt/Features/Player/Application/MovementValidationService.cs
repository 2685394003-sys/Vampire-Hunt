using System;
using VampireHunt.Core;
using VampireHunt.Player.Contracts;
using VampireHunt.Player.Domain;

namespace VampireHunt.Player.Application
{
    /// <summary>
    /// Validates the Owner's latest pose against server time and the last accepted
    /// pose. It never writes a Transform; correction is an adapter concern.
    /// </summary>
    public sealed class MovementValidationService
    {
        private readonly IPlayerRepository repository;
        private readonly IMovementState movementState;
        private readonly IMovementCorrector corrector;
        private readonly IMovementWorldQuery world;
        private readonly IMovementClock clock;
        private readonly MovementValidationOptions options;

        public MovementValidationService(
            IPlayerRepository repository,
            IMovementState movementState,
            IMovementCorrector corrector,
            IMovementWorldQuery world,
            IMovementClock clock,
            MovementValidationOptions options)
        {
            this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
            this.movementState = movementState ?? throw new ArgumentNullException(nameof(movementState));
            this.corrector = corrector ?? throw new ArgumentNullException(nameof(corrector));
            this.world = world ?? throw new ArgumentNullException(nameof(world));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.options = options;
        }

        public MovementValidationService(
            IPlayerRepository repository,
            IMovementState movementState,
            IMovementCorrector corrector,
            IMovementWorldQuery world,
            IGameClock clock,
            MovementValidationOptions options)
            : this(repository, movementState, corrector, world, new GameClockAdapter(clock), options)
        {
        }

        public MovementVerdict Validate(EntityId playerId, MovementPose reportedPose)
        {
            double now = clock.Now;
            if (!repository.TryGet(playerId, out PlayerAggregate player))
                return new MovementVerdict(MovementVerdictCode.MissingPlayer, reportedPose);

            if (!reportedPose.IsFinite || !IsFinite(now))
                return Reject(playerId, MovementVerdictCode.InvalidPose, reportedPose);

            if (!movementState.TryGetLastAcceptedPose(playerId, out MovementPose previous))
            {
                if (!world.IsInsideBounds(reportedPose.Position))
                    return Reject(playerId, MovementVerdictCode.OutOfBounds, reportedPose);
                movementState.CommitAcceptedPose(playerId, reportedPose);
                return MovementVerdict.Accept(reportedPose);
            }

            if (!IsNewer(reportedPose.Sequence, previous.Sequence))
                return Reject(playerId, MovementVerdictCode.InvalidPose, previous);

            double previousAt = previous.ReportedAt;
            if (previousAt == 0d) previousAt = now;
            double reportedAt = reportedPose.ReportedAt == 0d ? now : reportedPose.ReportedAt;
            double elapsed = reportedAt - previousAt;
            if (elapsed <= 0d || elapsed > options.MaximumReportAge ||
                reportedAt > now + options.MaximumFutureSkew)
            {
                return Reject(playerId, MovementVerdictCode.InvalidPose, previous);
            }

            if (!world.IsInsideBounds(reportedPose.Position))
                return Reject(playerId, MovementVerdictCode.OutOfBounds, previous);

            if (!world.IsPathClear(previous.Position, reportedPose.Position))
                return Reject(playerId, MovementVerdictCode.ThroughWall, previous);

            bool isDashing = player.MobilityState.IsDashing(now);
            if (reportedPose.IsDashing && !isDashing && !player.MobilityState.ConsumeDash(now))
                return Reject(playerId, MovementVerdictCode.DashNotAuthorized, previous);
            isDashing |= reportedPose.IsDashing;

            double distance = Distance(previous.Position, reportedPose.Position);
            double maximumDistance = options.MaximumSpeed * elapsed *
                (isDashing ? Math.Max(options.DashSpeedMultiplier, player.MobilityState.DashSpeedMultiplier) : 1d);
            if (distance > maximumDistance + 0.01d)
                return Reject(playerId, MovementVerdictCode.TooFast, previous);

            MovementPose accepted = new(
                reportedPose.Position,
                reportedPose.Facing,
                reportedAt,
                reportedPose.Sequence,
                isDashing);
            movementState.CommitAcceptedPose(playerId, accepted);
            return MovementVerdict.Accept(accepted);
        }

        private MovementVerdict Reject(EntityId playerId, MovementVerdictCode reason, MovementPose fallback)
        {
            corrector.ForcePose(playerId, fallback);
            return MovementVerdict.Correct(fallback, reason);
        }

        private static bool IsNewer(uint candidate, uint previous)
        {
            uint distance = unchecked(candidate - previous);
            return distance != 0 && distance < 0x80000000u;
        }

        private static double Distance(WorldPosition left, WorldPosition right)
        {
            double x = left.X - right.X;
            double y = left.Y - right.Y;
            double z = left.Z - right.Z;
            return Math.Sqrt(x * x + y * y + z * z);
        }

        private static bool IsFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);

        private sealed class GameClockAdapter : IMovementClock
        {
            private readonly IGameClock clock;
            public GameClockAdapter(IGameClock clock) => this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            public double Now => clock.Now;
        }
    }
}
