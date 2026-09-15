using System;
using System.Collections.Generic;
using VampireHunt.Contracts;
using UnityEngine.Scripting;

namespace VampireHunt.Boss.Abilities.Logic
{
    [Preserve]
    public sealed class BossGridCutAbilityLogic : BossGameplayAbilityLogic
    {
        private readonly HashSet<ulong> m_DamagedThisPulse = new HashSet<ulong>();
        private int m_RepetitionsRemaining;
        private double m_NextRepeatTime;

        public override void OnCastStarted(in BossAbilityCastContext context)
        {
            base.OnCastStarted(context);
            m_RepetitionsRemaining = Tuning.Repetitions;
        }

        protected override void Resolve(double serverTime)
        {
            PerformGrid();
            m_NextRepeatTime = serverTime + Tuning.Interval;
        }

        public override void OnPhaseEntered(BossAbilityCastPhase phase, double serverTime)
        {
            // If a slow server frame crosses the resolve boundary, apply every grid pulse
            // whose authored hit time has already passed before leaving Resolve.
            if (Phase == BossAbilityCastPhase.Resolve && phase != BossAbilityCastPhase.Resolve)
                CatchUpScheduledPulses(serverTime);
            base.OnPhaseEntered(phase, serverTime);
        }

        public override void Tick(double serverTime)
        {
            if (Phase != BossAbilityCastPhase.Resolve || m_RepetitionsRemaining <= 0) return;
            CatchUpScheduledPulses(serverTime);
        }

        private void CatchUpScheduledPulses(double serverTime)
        {
            while (m_RepetitionsRemaining > 0 && serverTime >= m_NextRepeatTime)
            {
                PerformGrid();
                m_NextRepeatTime += Tuning.Interval;
            }
        }

        private void PerformGrid()
        {
            if (m_RepetitionsRemaining <= 0) return;
            m_DamagedThisPulse.Clear();
            QueryOrthogonalGrid();
            m_RepetitionsRemaining--;
        }

        /// <summary>
        /// The authored visual is six horizontal strips crossing six vertical strips.
        /// Each visible strip owns one matching server-side box, so presentation and
        /// damage never disagree about which spaces are safe.
        /// </summary>
        private void QueryOrthogonalGrid()
        {
            float halfExtent = Math.Max(.05f, Tuning.Radius);
            float halfWidth = Math.Max(.025f, Tuning.Width * .5f);
            float halfHeight = Math.Max(.025f, Tuning.Height * .5f);
            float usableHalfExtent = Math.Max(0f, halfExtent - halfWidth);
            int lineCount = Math.Max(1, Tuning.GridLineCount);

            for (int line = 0; line < lineCount; line++)
            {
                float t = lineCount == 1 ? .5f : line / (float)(lineCount - 1);
                float offset = -usableHalfExtent + usableHalfExtent * 2f * t;

                // Horizontal strip: long axis follows world X, offset on world Z.
                QueryStrip(
                    Add(Context.SourcePosition, new Float3(0f, halfHeight, offset)),
                    new Float3(1f, 0f, 0f),
                    halfWidth,
                    halfHeight,
                    halfExtent);

                // Vertical strip: long axis follows world Z, offset on world X.
                QueryStrip(
                    Add(Context.SourcePosition, new Float3(offset, halfHeight, 0f)),
                    new Float3(0f, 0f, 1f),
                    halfWidth,
                    halfHeight,
                    halfExtent);
            }
        }

        private void QueryStrip(
            in Float3 center,
            in Float3 forward,
            float halfWidth,
            float halfHeight,
            float halfLength)
        {
            int count = QueryBox(center, forward, new Float3(halfWidth, halfHeight, halfLength));
            DamageHitsOnce(count, m_DamagedThisPulse, Float3.Zero);
        }
    }
}
