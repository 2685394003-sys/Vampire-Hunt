using UnityEngine.Scripting;

namespace VampireHunt.Boss.Abilities.Logic
{
    [Preserve]
    public sealed class BossFrenzyAbilityLogic : BossGameplayAbilityLogic
    {
        protected override void Resolve(double serverTime)
        {
            Services.RunClockModifier?.TrySetDrainRate(Tuning.ClockDrainRate);
        }
    }
}
