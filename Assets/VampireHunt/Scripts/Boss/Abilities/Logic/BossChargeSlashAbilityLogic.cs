using VampireHunt.Contracts;
using UnityEngine.Scripting;

namespace VampireHunt.Boss.Abilities.Logic
{
    [Preserve]
    public sealed class BossChargeSlashAbilityLogic : BossGameplayAbilityLogic
    {
        private Float3 m_LockedDirection;
        private Float3 m_LockedCenter;
        private bool m_TelegraphPublished;

        public override void OnCastStarted(in BossAbilityCastContext context)
        {
            base.OnCastStarted(context);

            // Prefer the nearest alive player inside this skill's range, then freeze that sample.
            // Flattening and storing it here makes the warning an immutable world-space lane:
            // the player can dodge it, but moving cannot drag the lane along afterwards.
            Float3 targetDirection = context.Direction;
            if (Services.PlayerTargetQuery != null && Services.PlayerTargetQuery.TryGetNearest(
                    context.SourcePosition, Tuning.Range, out BossPlayerTarget nearest))
                targetDirection = Direction(context.SourcePosition, nearest.Position);

            m_LockedDirection = new Float3(targetDirection.X, 0f, targetDirection.Z).Normalized();
            if (m_LockedDirection.SqrMagnitude <= 0.0001f)
                m_LockedDirection = new Float3(0f, 0f, 1f);

            m_LockedCenter = Add(context.SourcePosition, Scale(m_LockedDirection, Tuning.Range * 0.5f));
            m_TelegraphPublished = Services.AreaTelegraphService?.TryPublish(
                BossAreaTelegraphRequest.Box(
                    context.AbilityId,
                    context.CastSequence,
                    context.StartServerTime,
                    m_LockedCenter,
                    m_LockedDirection,
                    new Float3(Tuning.Width, Tuning.Height, Tuning.Range))) ?? false;
        }

        protected override void Resolve(double serverTime)
        {
            int count = QueryBox(m_LockedCenter, m_LockedDirection,
                new Float3(Tuning.Width * 0.5f, Tuning.Height * 0.5f, Tuning.Range * 0.5f));
            DamageHits(count, m_LockedDirection);
        }

        public override void Cancel(double serverTime)
        {
            CancelTelegraph();
        }

        public override void Dispose()
        {
            CancelTelegraph();
        }

        private void CancelTelegraph()
        {
            if (!m_TelegraphPublished) return;
            Services.AreaTelegraphService?.TryCancel(Context.AbilityId, Context.CastSequence);
            m_TelegraphPublished = false;
        }
    }
}
