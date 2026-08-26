using System;
using System.Collections.Generic;
using UnityEngine.Scripting;
using VampireHunt.Contracts;
using VampireHunt.SharedKernel;

namespace VampireHunt.Boss.Abilities.Logic
{
    /// <summary>
    /// Server-authoritative tracking laser. One random player is locked for the full cast;
    /// the beam turns at a bounded speed and each touched player owns an independent hit CD.
    /// </summary>
    [Preserve]
    public sealed class BossLaserSweepAbilityLogic : BossGameplayAbilityLogic
    {
        private readonly BossPlayerTarget[] m_TargetCandidates = new BossPlayerTarget[64];
        private readonly Dictionary<ulong, double> m_NextDamageTimeByTarget =
            new Dictionary<ulong, double>();

        private EntityId m_LockedTargetId;
        private Float3 m_CurrentDirection;
        private double m_LastTickTime;
        private bool m_PresentationPublished;

        public override void OnCastStarted(in BossAbilityCastContext context)
        {
            base.OnCastStarted(context);
            m_NextDamageTimeByTarget.Clear();
            m_LastTickTime = context.StartServerTime;
            m_LockedTargetId = SelectRandomTarget(context);
            Float3 initialFacing = Services.FacingService?.CurrentFacing ?? context.Direction;
            m_CurrentDirection = ResolvePlanarDirection(initialFacing, context.Direction);
            m_PresentationPublished = PublishPresentation();
        }

        protected override void Resolve(double serverTime)
        {
            // Telegraph only marks the target. At release, snap the Boss and beam directly
            // toward the target once; smooth tracking starts on subsequent resolve ticks.
            m_CurrentDirection = ResolveDesiredDirection(m_CurrentDirection);
            Services.FacingService?.TrySetFacing(m_CurrentDirection);
            m_LastTickTime = serverTime;
            DamageTouchingPlayers(serverTime);
        }

        public override void Tick(double serverTime)
        {
            if (Phase != BossAbilityCastPhase.Resolve) return;
            UpdateTracking(serverTime);
            DamageTouchingPlayers(serverTime);
        }

        public override void Cancel(double serverTime) => CancelPresentation();
        public override void Dispose() => CancelPresentation();

        private EntityId SelectRandomTarget(in BossAbilityCastContext context)
        {
            int count = Services.PlayerTargetQuery?.QueryAlivePlayers(m_TargetCandidates) ?? 0;
            if (count > 0)
            {
                int index = (int)(context.RandomSeed % (uint)count);
                return m_TargetCandidates[index].EntityId;
            }

            return context.TargetEntityId != 0
                ? new EntityId(context.TargetEntityId)
                : EntityId.None;
        }

        private void UpdateTracking(double serverTime)
        {
            double deltaTime = Math.Max(0d, serverTime - m_LastTickTime);
            m_LastTickTime = serverTime;
            Float3 desired = ResolveDesiredDirection(m_CurrentDirection);
            m_CurrentDirection = RotateTowardsOnGround(
                m_CurrentDirection,
                desired,
                Tuning.RotationSpeed * (float)deltaTime);
            Services.FacingService?.TrySetFacing(m_CurrentDirection);
        }

        private Float3 ResolveDesiredDirection(in Float3 fallback)
        {
            if (m_LockedTargetId != EntityId.None && Services.PlayerTargetQuery != null &&
                Services.PlayerTargetQuery.TryResolve(m_LockedTargetId, out BossPlayerTarget target))
            {
                Float3 towardTarget = new Float3(
                    target.Position.X - Context.SourcePosition.X,
                    0f,
                    target.Position.Z - Context.SourcePosition.Z).Normalized();
                if (towardTarget.SqrMagnitude > .0001f) return towardTarget;
            }

            Float3 planarFallback = new Float3(fallback.X, 0f, fallback.Z).Normalized();
            return planarFallback.SqrMagnitude > .0001f
                ? planarFallback
                : new Float3(0f, 0f, 1f);
        }

        private static Float3 ResolvePlanarDirection(in Float3 primary, in Float3 fallback)
        {
            Float3 planar = new Float3(primary.X, 0f, primary.Z).Normalized();
            if (planar.SqrMagnitude > .0001f) return planar;
            planar = new Float3(fallback.X, 0f, fallback.Z).Normalized();
            return planar.SqrMagnitude > .0001f ? planar : new Float3(0f, 0f, 1f);
        }

        private void DamageTouchingPlayers(double serverTime)
        {
            Float3 center = Add(
                Context.SourcePosition,
                new Float3(0f, Tuning.Height * .5f, 0f));
            center = Add(center, Scale(m_CurrentDirection, Tuning.Range * .5f));
            int count = QueryBox(
                center,
                m_CurrentDirection,
                new Float3(Tuning.Width * .5f, Tuning.Height * .5f, Tuning.Range * .5f));
            DamageHitsWithPerTargetCooldown(
                count,
                m_NextDamageTimeByTarget,
                serverTime,
                Tuning.Interval,
                m_CurrentDirection);
        }

        private bool PublishPresentation()
        {
            if (m_LockedTargetId == EntityId.None ||
                Services.TrackingLaserPresentationService == null) return false;
            return Services.TrackingLaserPresentationService.TryPublish(
                new BossTrackingLaserPresentationRequest(
                    Context.AbilityId,
                    Context.CastSequence,
                    Context.StartServerTime,
                    m_LockedTargetId,
                    m_CurrentDirection,
                    new Float3(Tuning.Width, Tuning.Height, Tuning.Range),
                    Tuning.RotationSpeed));
        }

        private void CancelPresentation()
        {
            if (!m_PresentationPublished) return;
            Services.TrackingLaserPresentationService?.TryCancel(
                Context.AbilityId,
                Context.CastSequence);
            m_PresentationPublished = false;
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
