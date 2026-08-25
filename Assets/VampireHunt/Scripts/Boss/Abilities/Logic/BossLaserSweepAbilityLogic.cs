using VampireHunt.Contracts;
using VampireHunt.SharedKernel;
using UnityEngine.Scripting;

namespace VampireHunt.Boss.Abilities.Logic
{
    [Preserve]
    public sealed class BossLaserSweepAbilityLogic : BossGameplayAbilityLogic
    {
        private double m_NextDamageTime;

        protected override void Resolve(double serverTime)
        {
            m_NextDamageTime = serverTime;
            PerformTick();
        }

        public override void Tick(double serverTime)
        {
            if (Phase != BossAbilityCastPhase.Resolve || serverTime < m_NextDamageTime) return;
            PerformTick();
            m_NextDamageTime = serverTime + Tuning.Interval;
        }

        private void PerformTick()
        {
            Float3 direction = Context.Direction;
            if (Context.TargetEntityId != 0 && Services.PlayerTargetQuery != null &&
                Services.PlayerTargetQuery.TryResolve(new EntityId(Context.TargetEntityId), out BossPlayerTarget target))
                direction = Direction(Context.SourcePosition, target.Position);
            Float3 center = Add(Context.SourcePosition, Scale(direction, Tuning.Range * 0.5f));
            int count = QueryBox(center, direction,
                new Float3(Tuning.Width * 0.5f, Tuning.Height * 0.5f, Tuning.Range * 0.5f));
            DamageHits(count, direction);
        }
    }
}
