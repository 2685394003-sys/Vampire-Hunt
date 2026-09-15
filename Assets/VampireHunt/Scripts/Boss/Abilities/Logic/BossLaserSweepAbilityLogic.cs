using System;
using System.Collections.Generic;
using UnityEngine.Scripting;
using VampireHunt.Contracts;
using VampireHunt.SharedKernel;

namespace VampireHunt.Boss.Abilities.Logic
{
    /// <summary>
    /// Server-authoritative multi-target tracking laser. Participant count is frozen in the
    /// cast context, target selection and hit tests happen only here, and clients receive
    /// presentation snapshots through a dedicated network port.
    /// </summary>
    [Preserve]
    public sealed class BossLaserSweepAbilityLogic : BossGameplayAbilityLogic
    {
        private const int MaxBeams = 64;
        private const double DirectionPublishInterval = .05d;

        private readonly BossPlayerTarget[] m_TargetCandidates = new BossPlayerTarget[MaxBeams];
        private readonly EntityId[] m_LockedTargets = new EntityId[MaxBeams];
        private readonly Float3[] m_CurrentDirections = new Float3[MaxBeams];
        private readonly bool[] m_PublishedBeams = new bool[MaxBeams];
        private readonly Dictionary<ulong, double> m_NextDamageTimeByTarget =
            new Dictionary<ulong, double>();

        private int m_BeamCount;
        private int m_PublishedBeamCount;
        private double m_LastTickTime;
        private double m_NextDirectionPublishTime;

        public override void OnCastStarted(in BossAbilityCastContext context)
        {
            base.OnCastStarted(context);
            m_NextDamageTimeByTarget.Clear();
            m_LastTickTime = context.StartServerTime;
            m_NextDirectionPublishTime = double.MaxValue;
            m_PublishedBeamCount = 0;

            int quantityUnits = context.Modifiers.ParticipantCount <= 1
                ? 1
                : context.Modifiers.QuantityUnits;
            m_BeamCount = Math.Max(1, Math.Min(MaxBeams, Tuning.LaserBeamCount * quantityUnits));

            int candidateCount = Math.Max(0, Math.Min(
                m_TargetCandidates.Length,
                Services.PlayerTargetQuery?.QueryAlivePlayers(m_TargetCandidates) ?? 0));
            int firstTarget = candidateCount > 0
                ? (int)(context.RandomSeed % (uint)candidateCount)
                : 0;
            Float3 initialFacing = ResolvePlanarDirection(
                Services.FacingService?.CurrentFacing ?? context.Direction,
                context.Direction);

            for (int i = 0; i < m_BeamCount; i++)
            {
                m_PublishedBeams[i] = false;
                m_LockedTargets[i] = candidateCount > 0
                    ? m_TargetCandidates[(firstTarget + i) % candidateCount].EntityId
                    : context.TargetEntityId != 0
                        ? new EntityId(context.TargetEntityId)
                        : EntityId.None;
                m_CurrentDirections[i] = initialFacing;
                m_PublishedBeams[i] = PublishPresentation(i);
                if (m_PublishedBeams[i]) m_PublishedBeamCount++;
            }
        }

        protected override void Resolve(double serverTime)
        {
            // At release every beam immediately faces its own locked player. Bounded tracking
            // begins only on subsequent resolve ticks, matching the authored behaviour.
            for (int i = 0; i < m_BeamCount; i++)
            {
                m_CurrentDirections[i] = ResolveDesiredDirection(i, m_CurrentDirections[i]);
                PublishDirection(i, snap: true);
            }

            if (m_BeamCount > 0) Services.FacingService?.TrySetFacing(m_CurrentDirections[0]);
            m_LastTickTime = serverTime;
            m_NextDirectionPublishTime = serverTime + DirectionPublishInterval;
            DamageTouchingPlayers(serverTime);
        }

        public override void Tick(double serverTime)
        {
            if (Phase != BossAbilityCastPhase.Resolve) return;

            double deltaTime = Math.Max(0d, serverTime - m_LastTickTime);
            m_LastTickTime = serverTime;
            for (int i = 0; i < m_BeamCount; i++)
            {
                Float3 desired = ResolveDesiredDirection(i, m_CurrentDirections[i]);
                m_CurrentDirections[i] = RotateTowardsOnGround(
                    m_CurrentDirections[i],
                    desired,
                    Tuning.RotationSpeed * (float)deltaTime);
            }

            if (m_BeamCount > 0) Services.FacingService?.TrySetFacing(m_CurrentDirections[0]);
            if (serverTime >= m_NextDirectionPublishTime)
            {
                for (int i = 0; i < m_BeamCount; i++) PublishDirection(i, snap: false);
                m_NextDirectionPublishTime = serverTime + DirectionPublishInterval;
            }
            DamageTouchingPlayers(serverTime);
        }

        public override void Cancel(double serverTime) => CancelPresentation();
        public override void Dispose() => CancelPresentation();

        private Float3 ResolveDesiredDirection(int beamIndex, in Float3 fallback)
        {
            EntityId targetId = m_LockedTargets[beamIndex];
            if (targetId != EntityId.None && Services.PlayerTargetQuery != null &&
                Services.PlayerTargetQuery.TryResolve(targetId, out BossPlayerTarget target))
            {
                Float3 towardTarget = new Float3(
                    target.Position.X - Context.SourcePosition.X,
                    0f,
                    target.Position.Z - Context.SourcePosition.Z).Normalized();
                if (towardTarget.SqrMagnitude > .0001f) return towardTarget;
            }

            return ResolvePlanarDirection(fallback, Context.Direction);
        }

        private void DamageTouchingPlayers(double serverTime)
        {
            for (int i = 0; i < m_BeamCount; i++)
            {
                Float3 direction = m_CurrentDirections[i];
                Float3 center = Add(
                    Context.SourcePosition,
                    new Float3(0f, Tuning.Height * .5f, 0f));
                center = Add(center, Scale(direction, Tuning.Range * .5f));
                int count = QueryBox(
                    center,
                    direction,
                    new Float3(Tuning.Width * .5f, Tuning.Height * .5f, Tuning.Range * .5f));
                DamageHitsWithPerTargetCooldown(
                    count,
                    m_NextDamageTimeByTarget,
                    serverTime,
                    Tuning.Interval,
                    direction);
            }
        }

        private bool PublishPresentation(int beamIndex)
        {
            if (m_LockedTargets[beamIndex] == EntityId.None ||
                Services.TrackingLaserPresentationService == null) return false;
            return Services.TrackingLaserPresentationService.TryPublish(
                new BossTrackingLaserPresentationRequest(
                    Context.AbilityId,
                    Context.CastSequence,
                    (uint)beamIndex,
                    Context.StartServerTime,
                    m_LockedTargets[beamIndex],
                    m_CurrentDirections[beamIndex],
                    new Float3(Tuning.Width, Tuning.Height, Tuning.Range),
                    Tuning.RotationSpeed,
                    Context.TelegraphDuration));
        }

        private void PublishDirection(int beamIndex, bool snap)
        {
            if (beamIndex < 0 || beamIndex >= m_BeamCount || !m_PublishedBeams[beamIndex]) return;
            Services.TrackingLaserPresentationService?.TryUpdate(
                Context.AbilityId,
                Context.CastSequence,
                (uint)beamIndex,
                m_CurrentDirections[beamIndex],
                snap);
        }

        private void CancelPresentation()
        {
            if (m_PublishedBeamCount <= 0) return;
            Services.TrackingLaserPresentationService?.TryCancel(
                Context.AbilityId,
                Context.CastSequence);
            m_PublishedBeamCount = 0;
        }

        private static Float3 ResolvePlanarDirection(in Float3 primary, in Float3 fallback)
        {
            Float3 planar = new Float3(primary.X, 0f, primary.Z).Normalized();
            if (planar.SqrMagnitude > .0001f) return planar;
            planar = new Float3(fallback.X, 0f, fallback.Z).Normalized();
            return planar.SqrMagnitude > .0001f ? planar : new Float3(0f, 0f, 1f);
        }

        private static Float3 RotateTowardsOnGround(
            in Float3 current,
            in Float3 desired,
            float maxDegreesDelta)
        {
            Float3 from = new Float3(current.X, 0f, current.Z).Normalized();
            Float3 to = new Float3(desired.X, 0f, desired.Z).Normalized();
            if (from.SqrMagnitude <= .0001f) return to;
            if (to.SqrMagnitude <= .0001f || maxDegreesDelta <= 0f) return from;

            double dot = Math.Max(-1d, Math.Min(1d, from.X * to.X + from.Z * to.Z));
            double crossY = from.Z * to.X - from.X * to.Z;
            float signedDegrees = (float)(Math.Atan2(crossY, dot) * 180d / Math.PI);
            float step = Math.Max(-maxDegreesDelta, Math.Min(maxDegreesDelta, signedDegrees));
            return RotateY(from, step);
        }
    }
}
