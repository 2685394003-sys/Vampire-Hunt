using UnityEngine.Scripting;

namespace VampireHunt.Boss.Abilities.Logic
{
    /// <summary>
    /// Presentation-only test logic. Real abilities replace this script with their own implementation.
    /// </summary>
    [Preserve]
    public sealed class BossNoOpAbilityLogic : IBossAbilityLogicRuntime
    {
        public void OnCastStarted(in BossAbilityCastContext context) { }
        public void OnPhaseEntered(BossAbilityCastPhase phase, double serverTime) { }
        public void Tick(double serverTime) { }
        public void Cancel(double serverTime) { }
        public void Dispose() { }
    }
}
