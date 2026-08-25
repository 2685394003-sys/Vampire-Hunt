using VampireHunt.Contracts;
using UnityEngine.Scripting;

namespace VampireHunt.Boss.Abilities.Logic
{
    [Preserve]
    public sealed class BossBombardmentAbilityLogic : BossGameplayAbilityLogic
    {
        protected override void Resolve(double serverTime)
        {
            int count = QuerySphere(Context.TargetPosition, Tuning.Radius);
            DamageHitsRadially(count, Context.TargetPosition);
        }
    }
}
