using UnityEngine.Scripting;
using VampireHunt.Contracts;

namespace VampireHunt.Boss.Abilities.Logic
{
    [Preserve]
    public sealed class BossSweepAbilityLogic : BossGameplayAbilityLogic
    {
        private enum SweepHand : byte { Left = 0, Right = 1 }

        private readonly SweepHand[] m_Passes = new SweepHand[2];
        private readonly bool[] m_CancelledPasses = new bool[2];
        private int m_PassCount;
        private int m_NextPassIndex;
        private double m_NextPassTime;
        private Float3 m_LockedDirection;
        private Float3 m_LockedCenter;
        private bool m_TelegraphsPublished;

        public override void OnCastStarted(in BossAbilityCastContext context)
        {
            base.OnCastStarted(context);
            m_PassCount = 0;
            m_NextPassIndex = 0;
            m_CancelledPasses[0] = false;
            m_CancelledPasses[1] = false;

            Float3 targetDirection = context.Direction;
            if (Services.PlayerTargetQuery != null && Services.PlayerTargetQuery.TryGetNearest(
                    context.SourcePosition, Tuning.Range, out BossPlayerTarget nearest))
                targetDirection = Direction(context.SourcePosition, nearest.Position);

            m_LockedDirection = new Float3(targetDirection.X, 0f, targetDirection.Z).Normalized();
            if (m_LockedDirection.SqrMagnitude <= .0001f)
                m_LockedDirection = new Float3(0f, 0f, 1f);
            m_LockedCenter = Add(context.SourcePosition, Scale(m_LockedDirection, Tuning.Range * .5f));

            bool leftFunctional = IsFunctional(SweepHand.Left);
            bool rightFunctional = IsFunctional(SweepHand.Right);
            if (leftFunctional && rightFunctional)
            {
                bool leftFirst = (context.RandomSeed & 1u) == 0u;
                m_Passes[0] = leftFirst ? SweepHand.Left : SweepHand.Right;
                m_Passes[1] = leftFirst ? SweepHand.Right : SweepHand.Left;
                m_PassCount = 2;
            }
            else if (leftFunctional)
            {
                m_Passes[0] = SweepHand.Left;
                m_PassCount = 1;
            }
            else if (rightFunctional)
            {
                m_Passes[0] = SweepHand.Right;
                m_PassCount = 1;
            }

            m_TelegraphsPublished = PublishTelegraphs();
        }

        protected override void Resolve(double serverTime)
        {
            CancelBrokenPendingPasses();
            PerformPass(0);
            m_NextPassIndex = 1;
            m_NextPassTime = serverTime + Tuning.Interval;
        }

        public override void Tick(double serverTime)
        {
            CancelBrokenPendingPasses();
            if (Phase != BossAbilityCastPhase.Resolve) return;

            while (m_NextPassIndex < m_PassCount && serverTime >= m_NextPassTime)
            {
                PerformPass(m_NextPassIndex++);
                m_NextPassTime += Tuning.Interval;
            }
        }

        public override void Cancel(double serverTime)
        {
            CancelAllTelegraphs();
        }

        public override void Dispose()
        {
            CancelAllTelegraphs();
        }

        private void PerformPass(int passIndex)
        {
            if (passIndex < 0 || passIndex >= m_PassCount || m_CancelledPasses[passIndex]) return;
            if (!IsFunctional(m_Passes[passIndex]))
            {
                CancelPass(passIndex);
                return;
            }

            int count = QueryBox(
                m_LockedCenter,
                m_LockedDirection,
                new Float3(Tuning.Width * .5f, Tuning.Height * .5f, Tuning.Range * .5f));
            DamageHits(count, m_LockedDirection);
        }

        private bool PublishTelegraphs()
        {
            if (m_PassCount <= 0 || Services.SweepTelegraphService == null) return false;
            var passes = new BossSweepTelegraphPass[m_PassCount];
            for (int i = 0; i < m_PassCount; i++)
            {
                passes[i] = new BossSweepTelegraphPass(
                    (uint)i,
                    Context.StartServerTime + Tuning.Interval * i,
                    m_LockedCenter,
                    m_LockedDirection,
                    new Float3(Tuning.Width, Tuning.Height, Tuning.Range),
                    m_Passes[i] == SweepHand.Left
                        ? BossSweepDirection.LeftToRight
                        : BossSweepDirection.RightToLeft);
            }
            return Services.SweepTelegraphService.TryPublish(new BossSweepTelegraphRequest(
                Context.AbilityId,
                Context.CastSequence,
                passes,
                Context.TelegraphDuration));
        }

        private void CancelBrokenPendingPasses()
        {
            for (int i = m_NextPassIndex; i < m_PassCount; i++)
                if (!m_CancelledPasses[i] && !IsFunctional(m_Passes[i])) CancelPass(i);
        }

        private void CancelPass(int passIndex)
        {
            if (passIndex < 0 || passIndex >= m_PassCount || m_CancelledPasses[passIndex]) return;
            m_CancelledPasses[passIndex] = true;
            if (m_TelegraphsPublished)
                Services.SweepTelegraphService?.TryCancelPass(
                    Context.AbilityId,
                    Context.CastSequence,
                    (uint)passIndex);
        }

        private void CancelAllTelegraphs()
        {
            if (!m_TelegraphsPublished) return;
            Services.SweepTelegraphService?.TryCancel(Context.AbilityId, Context.CastSequence);
            m_TelegraphsPublished = false;
        }

        private bool IsFunctional(SweepHand hand)
        {
            if (Services.BossBodyState == null) return true;
            return hand == SweepHand.Left
                ? Services.BossBodyState.IsLeftHandFunctional
                : Services.BossBodyState.IsRightHandFunctional;
        }
    }
}
