using System;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Player.Abilities.LaserWeapon;

namespace VampireHunt.Infrastructure.Unity
{
    /// <summary>
    /// Inspector-friendly ScriptableObject for the continuous laser weapon.
    /// The runtime is shared; one asset per laser-type weapon (laser=140, ...).
    /// </summary>
    [CreateAssetMenu(fileName = "LaserWeaponAbility", menuName = "Vampire Hunt/Combat/Laser Weapon Ability")]
    public sealed class LaserWeaponAbilityAsset : ScriptableObject
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
        [Min(1)] [SerializeField] private uint abilityId = 140;
        [SerializeField] private AbilitySlot slot = AbilitySlot.Primary;
        [SerializeField] private DamageTags weaponTag = DamageTags.Laser;

        [Header("Beam")]
        [Tooltip("Multiplier applied to the player's base Damage per tick.")]
        [Min(0f)] [SerializeField] private float damageMultiplier = 0.4f;
        [Tooltip("Time between damage ticks; also gates the hold-to-fire cadence.")]
        [Min(0.01f)] [SerializeField] private float tickInterval = 0.1f;
        [Min(0.1f)] [SerializeField] private float range = 15f;
        [Tooltip("Beam cross-section width (sphere-cast diameter).")]
        [Min(0.1f)] [SerializeField] private float width = 0.5f;
        [Tooltip("每 tick 最多穿透（造成伤害）的普通敌人数量；Boss 不可穿透，光束命中 Boss 即停止。")]
        [Min(1)] [SerializeField] private int pierceCount = 3;
        [Min(0f)] [SerializeField] private float spawnForwardOffset = 0.8f;
        [SerializeField] private float spawnHeight = 1f;

        [Header("Knockback")]
        [Tooltip("击退力倍率：最终击退力 = 全局击退力(KnockbackForce) × 此倍率，1 = 继承全局。")]
        [Min(0f)] [SerializeField] private float knockbackMultiplier = 1f;

        [Header("Element and on-hit effects")]
        [SerializeField] private ElementId element;
        [SerializeField] private StatusEntry[] onHitStatuses = Array.Empty<StatusEntry>();

        public LaserWeaponAbilityRuntime CreateRuntime()
        {
            var specs = new StatusEffectSpec[onHitStatuses?.Length ?? 0];
            for (int i = 0; i < specs.Length; i++)
                specs[i] = onHitStatuses[i] != null ? onHitStatuses[i].ToSpec() : default;

            return new LaserWeaponAbilityRuntime(new LaserWeaponAbilityDefinition(
                abilityId, slot, weaponTag, damageMultiplier, tickInterval, range, width,
                pierceCount,
                spawnHeight, spawnForwardOffset, knockbackMultiplier, element, specs));
        }
    }
}
