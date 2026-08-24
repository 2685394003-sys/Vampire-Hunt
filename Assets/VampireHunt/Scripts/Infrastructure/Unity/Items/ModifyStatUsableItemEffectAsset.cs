using Blocks.Gameplay.Core;
using UnityEngine;

namespace VampireHunt.Infrastructure.Unity.Items
{
    [CreateAssetMenu(
        fileName = "ModifyStatItemEffect",
        menuName = "Vampire Hunt/Items/Use Effects/Modify Stat")]
    public sealed class ModifyStatUsableItemEffectAsset : UsableItemEffectAsset
    {
        [SerializeField] private StatDefinition stat;
        [SerializeField] private float amount = 25f;
        [Tooltip("Prevents using the item when this effect cannot change the stat, such as healing at full health.")]
        [SerializeField] private bool requireEffectiveChange = true;

        public override bool CanApply(in UsableItemEffectContext context)
        {
            if (context.Stats == null || stat == null || string.IsNullOrWhiteSpace(stat.statName) ||
                Mathf.Approximately(amount, 0f)) return false;
            if (!requireEffectiveChange) return true;

            int statId = Animator.StringToHash(stat.statName);
            float current = context.Stats.GetCurrentValue(statId);
            return amount > 0f
                ? current < context.Stats.GetMaxValue(statId)
                : current > stat.minValue;
        }

        public override bool TryApply(in UsableItemEffectContext context)
        {
            if (!CanApply(context)) return false;
            int statId = Animator.StringToHash(stat.statName);
            float before = context.Stats.GetCurrentValue(statId);
            ModificationSource source = amount > 0f
                ? ModificationSource.Healing
                : ModificationSource.Consumption;
            context.Stats.ModifyStat(statId, amount, context.OwnerClientId, source);
            return !requireEffectiveChange ||
                   !Mathf.Approximately(before, context.Stats.GetCurrentValue(statId));
        }
    }
}
