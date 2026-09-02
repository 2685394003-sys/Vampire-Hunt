using System;
using UnityEngine;
using UnityEngine.Serialization;
using VampireHunt.Contracts;
using VampireHunt.Player.Abilities.ProjectileWeapon;

namespace VampireHunt.Infrastructure.Unity
{
    /// <summary>
    /// Inspector-friendly ScriptableObject for a projectile weapon ability.
    /// One asset per weapon (sniper=110, auto-rifle=120, ...); the runtime is shared.
    /// </summary>
    [CreateAssetMenu(fileName = "ProjectileWeaponAbility", menuName = "Vampire Hunt/Combat/Projectile Weapon Ability")]
    public sealed class ProjectileWeaponAbilityAsset : ScriptableObject
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
        [Min(1)] [SerializeField] private uint abilityId = 110;
        [SerializeField] private AbilitySlot slot = AbilitySlot.Primary;
        [Tooltip("Damage tag identifying this weapon for combat resolution (sniper, rifle, ...).")]
        [SerializeField] private DamageTags weaponTag = DamageTags.Sniper;

        [Header("Cast")]
        [Tooltip("Multiplier applied to the player's base Damage attribute per shot.")]
        [Min(0f)] [SerializeField] private float damageMultiplier = 3f;
        [Min(0.01f)] [SerializeField] private float baseCooldown = 1.5f;
        [Min(0.1f)] [SerializeField] private float travelDistance = 20f;
        [Min(0.1f)] [SerializeField] private float projectileSpeed = 50f;
        [Min(0.1f)] [SerializeField] private float projectileSize = 0.5f;
        [Min(1)] [SerializeField] private int projectileCount = 1;
        [Min(1)] [SerializeField] private int pierceCount = 1;
        [Min(0f)] [SerializeField] private float spreadAngle = 0f;
        [Min(0f)] [SerializeField] private float spawnForwardOffset = 0.8f;
        [SerializeField] private float spawnHeight = 1f;

        [Header("Knockback")]
        [Tooltip("击退力倍率：最终击退力 = 全局击退力(KnockbackForce) × 此倍率，1 = 继承全局。")]
        [Min(0f)] [SerializeField] private float knockbackMultiplier = 1f;

        [Header("Element and on-hit effects")]
        [SerializeField] private ElementId element;
        [SerializeField] private StatusEntry[] onHitStatuses = Array.Empty<StatusEntry>();
        [Tooltip("属性精通：影响所有元素效果（挂元素层数、闪电连锁传导）的倍率（狙击=3、步枪=0.5）。")]
        [Min(0f)] [SerializeField, FormerlySerializedAs("conductivity")] private float elementMastery = 1f;

        public ProjectileWeaponAbilityRuntime CreateRuntime()
        {
            var specs = new StatusEffectSpec[onHitStatuses?.Length ?? 0];
            for (int i = 0; i < specs.Length; i++)
                specs[i] = onHitStatuses[i] != null ? onHitStatuses[i].ToSpec() : default;

            return new ProjectileWeaponAbilityRuntime(new ProjectileWeaponAbilityDefinition(
                abilityId, slot, weaponTag, damageMultiplier, baseCooldown, travelDistance,
                projectileSpeed, projectileSize, projectileCount, pierceCount, spreadAngle,
                spawnForwardOffset, spawnHeight, knockbackMultiplier, element, elementMastery, specs));
        }
    }
}
