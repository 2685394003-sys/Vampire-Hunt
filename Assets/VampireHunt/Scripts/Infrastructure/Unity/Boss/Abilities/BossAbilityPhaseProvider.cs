using UnityEngine;
using VampireHunt.Boss.Abilities;

namespace VampireHunt.Infrastructure.Unity.Boss
{
    /// <summary>
    /// The single Unity-side owner of the phase-set configuration reference.
    /// Runtime hosts and presenters ask this provider for phase or ability definitions.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossAbilityPhaseProvider : MonoBehaviour
    {
        [SerializeField] private BossPhaseSetAsset phaseSet;
        [Min(1)] [SerializeField] private int startingPhase = 1;

        public BossPhaseSetAsset PhaseSet => phaseSet;
        public int StartingPhase => startingPhase;

        public bool TryCreateStartingPhase(out BossPhaseDefinition definition) =>
            TryCreatePhase(startingPhase, out definition);

        public bool TryCreatePhase(int phaseNumber, out BossPhaseDefinition definition)
        {
            definition = null;
            return phaseSet != null && phaseSet.TryCreateDefinition(phaseNumber, out definition);
        }

        public bool TryGetAbility(uint abilityId, out BossAbilityAsset ability)
        {
            ability = null;
            return phaseSet != null && phaseSet.TryGetAbility(abilityId, out ability);
        }

        private void OnValidate()
        {
            startingPhase = Mathf.Max(1, startingPhase);
        }
    }
}
