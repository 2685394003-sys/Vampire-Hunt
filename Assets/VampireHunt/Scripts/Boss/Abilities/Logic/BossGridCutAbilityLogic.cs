using VampireHunt.Contracts;
using UnityEngine.Scripting;

namespace VampireHunt.Boss.Abilities.Logic
{
    [Preserve]
    public sealed class BossGridCutAbilityLogic : BossGameplayAbilityLogic
    {
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

        public override void Tick(double serverTime)
        {
            if (Phase != BossAbilityCastPhase.Resolve || m_RepetitionsRemaining <= 0 ||
                serverTime < m_NextRepeatTime) return;
            PerformGrid();
            m_NextRepeatTime += Tuning.Interval;
        }

        private void PerformGrid()
        {
            if (m_RepetitionsRemaining <= 0) return;
            Float3 halfExtents = new Float3(Tuning.Width * 0.5f, Tuning.Height * 0.5f, Tuning.Range * 0.5f);
            int first = QueryBox(Context.SourcePosition, new Float3(0f, 0f, 1f), halfExtents);
            DamageHits(first, Float3.Zero);
            int second = QueryBox(Context.SourcePosition, new Float3(1f, 0f, 0f), halfExtents);
            DamageHits(second, Float3.Zero);
            m_RepetitionsRemaining--;
        }
    }
}
