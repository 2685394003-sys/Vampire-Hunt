using VampireHunt.Contracts;
using UnityEngine.Scripting;

namespace VampireHunt.Boss.Abilities.Logic
{
    [Preserve]
    public sealed class BossChargeSlashAbilityLogic : BossGameplayAbilityLogic
    {
        protected override void Resolve(double serverTime)
        {
            Float3 center = Add(Context.SourcePosition, Scale(Context.Direction, Tuning.Range * 0.5f));
            int count = QueryBox(center, Context.Direction,
                new Float3(Tuning.Width * 0.5f, Tuning.Height * 0.5f, Tuning.Range * 0.5f));
            DamageHits(count, Context.Direction);
        }
    }
}
