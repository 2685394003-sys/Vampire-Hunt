using VampireHunt.Contracts;
using UnityEngine.Scripting;

namespace VampireHunt.Boss.Abilities.Logic
{
    [Preserve]
    public sealed class BossRadialShockwaveAbilityLogic : BossGameplayAbilityLogic
    {
        protected override void Resolve(double serverTime)
        {
            int count = QuerySphere(Context.SourcePosition, Tuning.Radius);
            DamageHits(count, Context.Direction);
        }
    }
}
