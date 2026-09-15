using System;
using System.Collections.Generic;
using UnityEngine;
using VampireHunt.Boss.Abilities;

namespace VampireHunt.Infrastructure.Unity.Boss
{
    [CreateAssetMenu(fileName = "BossPhaseSet", menuName = "Vampire Hunt/Boss/Phase Set")]
    public sealed class BossPhaseSetAsset : ScriptableObject
    {
        [SerializeField] private BossPhaseAsset[] phases = Array.Empty<BossPhaseAsset>();

        public IReadOnlyList<BossPhaseAsset> Phases => phases;

        public bool TryCreateDefinition(int phaseNumber, out BossPhaseDefinition definition)
        {
            definition = null;
            for (int i = 0; i < phases.Length; i++)
            {
                BossPhaseAsset phase = phases[i];
                if (phase == null || phase.PhaseNumber != phaseNumber) continue;
                definition = phase.CreateDefinition();
                return true;
            }
            return false;
        }

        public bool TryGetAbility(uint abilityId, out BossAbilityAsset ability)
        {
            for (int phaseIndex = 0; phaseIndex < phases.Length; phaseIndex++)
            {
                BossPhaseAsset phase = phases[phaseIndex];
                if (phase == null) continue;
                IReadOnlyList<BossPhaseAbilityEntryAsset> entries = phase.Abilities;
                for (int abilityIndex = 0; abilityIndex < entries.Count; abilityIndex++)
                {
                    BossAbilityAsset candidate = entries[abilityIndex]?.Ability;
                    if (candidate == null || candidate.AbilityId != abilityId) continue;
                    ability = candidate;
                    return true;
                }
            }

            ability = null;
            return false;
        }

        private void OnValidate()
        {
            phases ??= Array.Empty<BossPhaseAsset>();
        }
    }
}
