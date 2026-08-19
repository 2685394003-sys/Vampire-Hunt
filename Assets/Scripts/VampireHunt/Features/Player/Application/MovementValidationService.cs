using System;
using System.Collections.Generic;
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
        // MovementState is an adapter-owned pose store and deliberately only
        // exposes the last accepted pose. Keep the server receive time beside
        // it so a client cannot manufacture movement budget with its clock.
        private readonly Dictionary<EntityId, double> lastAcceptedReceiveTimes = new();

        internal MovementValidationService(
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

        internal MovementValidationService(
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

            // ReportedAt is an input-integrity check, not the elapsed-time
            // source for movement. It must be checked before the first pose is
            // committed, otherwise one stale packet can seed history forever.
            if (!reportedPose.IsFinite || !IsFinite(now) || !IsWithinServerWindow(reportedPose.ReportedAt, now))
                return Reject(playerId, MovementVerdictCode.InvalidPose, reportedPose);

            if (!movementState.TryGetLastAcceptedPose(playerId, out MovementPose previous))
            {
                if (!world.IsInsideBounds(reportedPose.Position))
                    return Reject(playerId, MovementVerdictCode.OutOfBounds, reportedPose);
                movementState.CommitAcceptedPose(playerId, reportedPose);
                lastAcceptedReceiveTimes[playerId] = now;
                return MovementVerdict.Accept(reportedPose);
            }

            if (!IsNewer(reportedPose.Sequence, previous.Sequence))
                return Reject(playerId, MovementVerdictCode.InvalidPose, previous);

            if (!world.IsInsideBounds(reportedPose.Position))
                return Reject(playerId, MovementVerdictCode.OutOfBounds, previous);

            if (!world.IsPathClear(previous.Position, reportedPose.Position))
                return Reject(playerId, MovementVerdictCode.ThroughWall, previous);

            bool isDashing = player.MobilityState.IsDashing(now);
            bool needsDashAuthorization = reportedPose.IsDashing && !isDashing;
            if (needsDashAuthorization && !player.MobilityState.CanDash(now))
                return Reject(playerId, MovementVerdictCode.DashNotAuthorized, previous);
            isDashing |= reportedPose.IsDashing;

            double distance = Distance(previous.Position, reportedPose.Position);
            double elapsed = ServerElapsed(playerId, now);
            double maximumDistance = options.MaximumSpeed * elapsed *
                (isDashing ? Math.Max(options.DashSpeedMultiplier, player.MobilityState.DashSpeedMultiplier) : 1d);
            if (distance > maximumDistance + 0.01d)
                return Reject(playerId, MovementVerdictCode.TooFast, previous);

            // Consume a newly requested dash only after all pose checks pass.
            // A forged/too-fast dash must not burn the player's server-side
            // stamina or cooldown while being corrected.
            if (needsDashAuthorization && !player.MobilityState.ConsumeDash(now))
                return Reject(playerId, MovementVerdictCode.DashNotAuthorized, previous);

            MovementPose accepted = new(
                reportedPose.Position,
                reportedPose.Facing,
                reportedPose.ReportedAt,
                reportedPose.Sequence,
                isDashing);
            movementState.CommitAcceptedPose(playerId, accepted);
            lastAcceptedReceiveTimes[playerId] = now;
            return MovementVerdict.Accept(accepted);
        }

        private double ServerElapsed(EntityId playerId, double now)
        {
            // If the adapter already had a pose when this validator was
            // created, there is no trustworthy receive timestamp to pair with
            // it. Recover conservatively with zero budget; the first accepted
            // packet establishes a fresh server-time baseline.
            if (!lastAcceptedReceiveTimes.TryGetValue(playerId, out double previousReceivedAt) ||
                !IsFinite(previousReceivedAt) || previousReceivedAt > now)
            {
                return 0d;
            }

            double elapsed = now - previousReceivedAt;
            return elapsed < 0d || double.IsNaN(elapsed) ? 0d : elapsed;
        }

        private bool IsWithinServerWindow(double reportedAt, double now)
        {
            if (!IsFinite(reportedAt)) return false;

            // Compare differences instead of adding bounds so a large but
            // finite server timestamp cannot overflow the window arithmetic.
            double age = now - reportedAt;
            if (!IsWithinInclusiveUpperBound(age, options.MaximumReportAge)) return false;
            double future = reportedAt - now;
            return IsWithinInclusiveUpperBound(future, options.MaximumFutureSkew);
        }

        private static bool IsWithinInclusiveUpperBound(double value, double upperBound)
        {
            if (value <= upperBound) return true;

            // Decimal timestamps such as 100.2 and 100.1 are not represented
            // exactly as doubles. Permit only a scale-aware rounding epsilon
            // so a mathematically inclusive bound remains inclusive without
            // granting a meaningful amount of extra client clock skew.
            double scale = Math.Max(1d, Math.Max(Math.Abs(value), Math.Abs(upperBound)));
            return value - upperBound <= 1e-12d * scale;
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
