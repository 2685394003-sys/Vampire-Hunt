using UnityEngine.Scripting;
using VampireHunt.Contracts;
using VampireHunt.SharedKernel;

namespace VampireHunt.Boss.Abilities.Logic
{
    [Preserve]
    public sealed class BossBombardmentAbilityLogic : BossGameplayAbilityLogic
    {
        private readonly BossPlayerTarget[] m_Targets = new BossPlayerTarget[64];
        private readonly Float3[] m_LockedCenters = new Float3[64];
        private int m_LockedCount;
        private bool m_TelegraphPublished;

        public override void OnCastStarted(in BossAbilityCastContext context)
        {
            base.OnCastStarted(context);
            m_LockedCount = CaptureTargetPositions();
            m_TelegraphPublished = PublishTelegraphs();
        }

        protected override void Resolve(double serverTime)
        {
            for (int i = 0; i < m_LockedCount; i++)
            {
                Float3 center = m_LockedCenters[i];
                int hitCount = QuerySphere(center, Tuning.Radius);
                DamageHitsRadially(hitCount, center);
            }
        }

        public override void Cancel(double serverTime)
        {
            CancelTelegraphs();
        }

        public override void Dispose()
        {
            CancelTelegraphs();
        }

        private int CaptureTargetPositions()
        {
            int count = Services.PlayerTargetQuery?.QueryAlivePlayers(m_Targets) ?? 0;
            float range = Tuning.Range;
            float rangeSqr = range * range;
            int locked = 0;

            for (int i = 0; i < count && locked < m_LockedCenters.Length; i++)
            {
                Float3 position = m_Targets[i].Position;
                if (range > 0f && SqrDistance(Context.SourcePosition, position) > rangeSqr) continue;
                m_LockedCenters[locked++] = position;
            }

            // Target-required scheduling guarantees a primary target in normal network play.
            // This fallback keeps authored previews and custom target-query adapters usable.
            if (locked == 0 && Context.TargetEntityId != 0)
                m_LockedCenters[locked++] = Context.TargetPosition;
            return locked;
        }

        private bool PublishTelegraphs()
        {
            if (m_LockedCount <= 0 || Services.AreaTelegraphService == null) return false;
            var centers = new Float3[m_LockedCount];
            for (int i = 0; i < m_LockedCount; i++) centers[i] = m_LockedCenters[i];
            return Services.AreaTelegraphService.TryPublish(new BossAreaTelegraphRequest(
                Context.AbilityId,
                Context.CastSequence,
                Context.StartServerTime,
                Tuning.Radius,
                centers,
                Context.TelegraphDuration));
        }

        private void CancelTelegraphs()
        {
            if (!m_TelegraphPublished) return;
            Services.AreaTelegraphService?.TryCancel(Context.AbilityId, Context.CastSequence);
            m_TelegraphPublished = false;
        }

        private static float SqrDistance(in Float3 left, in Float3 right)
        {
            float x = left.X - right.X;
            float y = left.Y - right.Y;
            float z = left.Z - right.Z;
            return x * x + y * y + z * z;
        }
    }
}
