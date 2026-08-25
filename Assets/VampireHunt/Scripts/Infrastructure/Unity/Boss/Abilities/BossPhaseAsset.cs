using System;
using System.Collections.Generic;
using UnityEngine;
using VampireHunt.Boss.Abilities;

namespace VampireHunt.Infrastructure.Unity.Boss
{
    [CreateAssetMenu(fileName = "BossPhase", menuName = "Vampire Hunt/Boss/Phase")]
    public sealed class BossPhaseAsset : ScriptableObject
    {
        [Min(1)] [SerializeField] private int phaseNumber = 1;
        [SerializeField] private string displayName = "Phase 1";
        [SerializeField] private BossPhaseAbilityEntryAsset[] abilities =
            Array.Empty<BossPhaseAbilityEntryAsset>();

        public int PhaseNumber => phaseNumber;
        public IReadOnlyList<BossPhaseAbilityEntryAsset> Abilities => abilities;

        public BossPhaseDefinition CreateDefinition()
        {
            var entries = new List<BossPhaseAbilityEntry>();
            var seenIds = new HashSet<uint>();

            for (int i = 0; i < abilities.Length; i++)
            {
                BossPhaseAbilityEntryAsset entry = abilities[i];
                if (entry == null || !entry.Enabled || entry.Ability == null ||
                    !seenIds.Add(entry.Ability.AbilityId)) continue;
                entries.Add(new BossPhaseAbilityEntry(
                    entry.Ability.CreateDefinition(),
                    entry.WeightMultiplier,
                    entry.MaxUses,
                    entry.InitialCooldown));
            }

            return new BossPhaseDefinition(phaseNumber, displayName, entries.ToArray());
        }

        private void OnValidate()
        {
            phaseNumber = Mathf.Max(1, phaseNumber);
            abilities ??= Array.Empty<BossPhaseAbilityEntryAsset>();
        }
    }
}
