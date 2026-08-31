using System;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Player.Abilities.SwordWave;

namespace VampireHunt.Infrastructure.Unity
{
    [CreateAssetMenu(fileName = "SwordWaveAbility", menuName = "Vampire Hunt/Combat/Sword Wave Ability")]
    public sealed class SwordWaveAbilityAsset : ScriptableObject
    {
        [Serializable]
        private sealed class StatusEntry
        {
            public uint statusId = 0;
            [Min(1)] public int stacks = 1;
            [Min(0f)] public float duration = 0f;
            [Min(0f)] public float magnitude = 0f;
            public ElementId element = ElementId.None;

            public StatusEffectSpec ToSpec() =>
                new StatusEffectSpec(statusId, stacks, duration, magnitude, element);
        }

        [Header("Identity")]
        [Min(1)] [SerializeField] private uint abilityId = 1;
        [SerializeField] private AbilitySlot slot = AbilitySlot.Primary;

        [Header("Cast")]
        [Min(0.01f)] [SerializeField] private float baseCooldown = 0.45f;
        [Min(0.1f)] [SerializeField] private float travelDistance = 6f;
        [Min(0.1f)] [SerializeField] private float projectileSpeed = 14f;
        [Min(0.1f)] [SerializeField] private float projectileSize = 1f;
        [Min(1)] [SerializeField] private int projectileCount = 1;
        [Min(1)] [SerializeField] private int pierceCount = 1;
        [Min(0f)] [SerializeField] private float spreadAngle;
        [Min(0f)] [SerializeField] private float spawnForwardOffset = 0.8f;
        [SerializeField] private float spawnHeight = 1f;

        [Header("Knockback")]
        [Tooltip("击退力倍率：最终击退力 = 全局击退力(KnockbackForce) × 此倍率，1 = 继承全局。")]
        [Min(0f)] [SerializeField] private float knockbackMultiplier = 1f;

        [Header("Element and on-hit effects")]
        [SerializeField] private ElementId element;
        [SerializeField] private StatusEntry[] onHitStatuses = Array.Empty<StatusEntry>();

        public SwordWaveAbilityRuntime CreateRuntime()
        {
            var specs = new StatusEffectSpec[onHitStatuses?.Length ?? 0];
            for (int i = 0; i < specs.Length; i++)
                specs[i] = onHitStatuses[i] != null ? onHitStatuses[i].ToSpec() : default;

            return new SwordWaveAbilityRuntime(new SwordWaveAbilityDefinition(
                abilityId, slot, baseCooldown, travelDistance, projectileSpeed, projectileSize,
                projectileCount, pierceCount, spreadAngle, spawnForwardOffset, spawnHeight,
                knockbackMultiplier, element, specs));
        }
    }
}
