using System.Collections.Generic;
using VampireHunt.Contracts;
using UnityEngine.Scripting;

namespace VampireHunt.Boss.Abilities.Logic
{
    [Preserve]
    public sealed class BossRadialShockwaveAbilityLogic : BossGameplayAbilityLogic
    {
        private readonly HashSet<ulong> m_DamagedTargets = new HashSet<ulong>();
        private int m_CurrentStep;
        private double m_NextStepTime;

        public override void OnCastStarted(in BossAbilityCastContext context)
        {
            base.OnCastStarted(context);
            m_DamagedTargets.Clear();
            m_CurrentStep = 0;
            m_NextStepTime = 0d;
        }

        protected override void Resolve(double serverTime)
        {
            PerformStep();
            m_NextStepTime = serverTime + Tuning.Interval;
        }

        public override void Tick(double serverTime)
        {
            if (Phase != BossAbilityCastPhase.Resolve || m_CurrentStep >= Tuning.Repetitions ||
                serverTime < m_NextStepTime) return;
            PerformStep();
            m_NextStepTime += Tuning.Interval;
        }

        private void PerformStep()
        {
            m_CurrentStep++;
            float radius = Tuning.Radius * m_CurrentStep / Tuning.Repetitions;
            int count = QuerySphere(Context.SourcePosition, radius);
            DamageHitsOnce(count, m_DamagedTargets, Context.Direction);
        }
    }
}
