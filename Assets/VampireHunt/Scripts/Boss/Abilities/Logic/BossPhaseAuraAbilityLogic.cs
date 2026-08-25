using UnityEngine.Scripting;

namespace VampireHunt.Boss.Abilities.Logic
{
    /// <summary>Gameplay no-op by design; its replicated timeline drives the golden transition VFX.</summary>
    [Preserve]
    public sealed class BossPhaseAuraAbilityLogic : BossGameplayAbilityLogic { }
}
