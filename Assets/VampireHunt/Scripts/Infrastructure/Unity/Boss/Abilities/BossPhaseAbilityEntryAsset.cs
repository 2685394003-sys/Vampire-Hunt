using System;
using UnityEngine;

namespace VampireHunt.Infrastructure.Unity.Boss
{
    [Serializable]
    public sealed class BossPhaseAbilityEntryAsset
    {
        [SerializeField] private bool enabled = true;
        [SerializeField] private BossAbilityAsset ability;
        [Min(0f)] [SerializeField] private float weightMultiplier = 1f;
        [Tooltip("0 means unlimited uses in this phase.")]
        [Min(0)] [SerializeField] private int maxUses;
        [Min(0f)] [SerializeField] private float initialCooldown;

        public bool Enabled => enabled;
        public BossAbilityAsset Ability => ability;
        public float WeightMultiplier => weightMultiplier;
        public int MaxUses => maxUses;
        public float InitialCooldown => initialCooldown;
    }
}
