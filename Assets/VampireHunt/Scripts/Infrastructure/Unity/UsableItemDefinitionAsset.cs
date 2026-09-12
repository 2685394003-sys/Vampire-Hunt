using System;
using VampireHunt.Contracts;
using UnityEngine;
using VampireHunt.Economy;
using VampireHunt.Infrastructure.Unity.Items;

namespace VampireHunt.Infrastructure.Unity
{
    [CreateAssetMenu(fileName = "UsableItemDefinition", menuName = "Vampire Hunt/Items/Usable Item")]
    public sealed class UsableItemDefinitionAsset : ItemDefinitionAsset
    {
        [SerializeField] private CombatIntentPolicy combatIntentPolicy = CombatIntentPolicy.NonCombat;
        public CombatIntentPolicy CombatIntent => combatIntentPolicy;
        [Header("Storage and Use")]
        [SerializeField] private UseConsumptionPolicy consumptionPolicy =
            UseConsumptionPolicy.ConsumeOnSuccess;
        [SerializeField, Min(1)] private int maxStackPerSlot = 5;
        [SerializeField, Min(0f)] private float cooldownSeconds;

        [Header("Use Effects")]
        [Tooltip("Server-side effects attempted when this item is used. The item succeeds when at least one effect applies.")]
        [SerializeField] private UsableItemEffectAsset[] useEffects =
            Array.Empty<UsableItemEffectAsset>();

        public override ItemKind Kind => ItemKind.Usable;
        public UseConsumptionPolicy ConsumptionPolicy => consumptionPolicy;
        public int MaxStackPerSlot => consumptionPolicy == UseConsumptionPolicy.RetainOnUse
            ? 1
            : maxStackPerSlot;
        public float CooldownSeconds => cooldownSeconds;
        public UsableItemEffectAsset[] UseEffects => useEffects ?? Array.Empty<UsableItemEffectAsset>();

        public override ItemDefinition ToDomain() => new UsableItemDefinition(
            ItemId,
            DisplayName,
            Description,
            consumptionPolicy,
            maxStackPerSlot,
            cooldownSeconds);

        protected override void OnValidate()
        {
            base.OnValidate();
            maxStackPerSlot = consumptionPolicy == UseConsumptionPolicy.RetainOnUse
                ? 1
                : Mathf.Max(1, maxStackPerSlot);
            cooldownSeconds = Mathf.Max(0f, cooldownSeconds);
        }
    }
}
