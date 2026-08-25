using VampireHunt.Contracts;
using UnityEngine.Scripting;

namespace VampireHunt.Boss.Abilities.Logic
{
    [Preserve]
    public sealed class BossSweepAbilityLogic : BossGameplayAbilityLogic
    {
        private int m_PassesRemaining;
        private double m_NextPassTime;
        private bool m_LeftFirst;

        public override void OnCastStarted(in BossAbilityCastContext context)
        {
            base.OnCastStarted(context);
            bool left = Services.BossBodyState?.IsLeftHandFunctional ?? true;
            bool right = Services.BossBodyState?.IsRightHandFunctional ?? true;
            m_PassesRemaining = left && right ? System.Math.Max(2, Tuning.Repetitions) : left || right ? 1 : 0;
            m_LeftFirst = (context.RandomSeed & 1u) == 0u;
        }

        protected override void Resolve(double serverTime)
        {
            PerformPass();
            m_NextPassTime = serverTime + Tuning.Interval;
        }

        public override void Tick(double serverTime)
        {
            if (Phase != BossAbilityCastPhase.Resolve || m_PassesRemaining <= 0 || serverTime < m_NextPassTime) return;
            PerformPass();
            m_NextPassTime += Tuning.Interval;
        }

        private void PerformPass()
        {
            if (m_PassesRemaining <= 0) return;
            int passIndex = m_PassesRemaining;
            bool leftToRight = m_LeftFirst == ((passIndex & 1) == 0);
            Float3 lateral = RotateY(Context.Direction, leftToRight ? 90f : -90f);
            Float3 center = Add(Context.SourcePosition, Scale(Context.Direction, Tuning.Range * 0.5f));
            center = Add(center, Scale(lateral, Tuning.Width * 0.15f));
            int count = QueryBox(center, Context.Direction,
                new Float3(Tuning.Width * 0.5f, Tuning.Height * 0.5f, Tuning.Range * 0.5f));
            DamageHits(count, Context.Direction);
            m_PassesRemaining--;
        }
    }
}
