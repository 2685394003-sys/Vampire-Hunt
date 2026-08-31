using System;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Player.Abilities.Missile;

namespace VampireHunt.Infrastructure.Unity
{
    /// <summary>
    /// Inspector-friendly ScriptableObject for the missile bomb weapon (Peachone style).
    /// Elevation (arcAngle) and blast radius are carried on the cast plan's SpreadAngle /
    /// ProjectileSize fields, so the bomb projectile needs no extra network fields.
    /// </summary>
    [CreateAssetMenu(fileName = "MissileAbility", menuName = "Vampire Hunt/Combat/Missile Ability")]
    public sealed class MissileAbilityAsset : ScriptableObject
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
        [Min(1)] [SerializeField] private uint abilityId = 130;
        [SerializeField] private AbilitySlot slot = AbilitySlot.Primary;
        [SerializeField] private DamageTags weaponTag = DamageTags.Missile;

        [Header("Cast")]
        [Tooltip("Multiplier applied to the player's base Damage for the flight (piercing) hit.")]
        [Min(0f)] [SerializeField] private float damageMultiplier = 0.3f;
        [Min(0.01f)] [SerializeField] private float baseCooldown = 1.2f;
        [Min(0.1f)] [SerializeField] private float range = 25f;
        [Min(0.1f)] [SerializeField] private float projectileSpeed = 16f;
        [Min(0)] [SerializeField] private int pierceCount = 1;
        [Tooltip("Launch elevation angle (degrees). Lower = flatter arc (kept low so the camera sees it).")]
        [Range(0f, 89.9f)] [SerializeField] private float arcAngle = 25f;
        [Tooltip("Gravity used for the lob arc and landing-distance speed calc.")]
        [Min(0.1f)] [SerializeField] private float gravity = 9.8f;
        [Min(0.1f)] [SerializeField] private float blastRadius = 2f;
        [Min(1)] [SerializeField] private int projectileCount = 1;
        [Min(0f)] [SerializeField] private float spawnForwardOffset = 0.5f;
        [SerializeField] private float spawnHeight = 1f;

        [Header("Knockback")]
        [Tooltip("击退力倍率：最终击退力 = 全局击退力(KnockbackForce) × 此倍率，1 = 继承全局。")]
        [Min(0f)] [SerializeField] private float knockbackMultiplier = 1f;

        [Header("Element and on-hit effects")]
        [SerializeField] private ElementId element;
        [SerializeField] private StatusEntry[] onHitStatuses = Array.Empty<StatusEntry>();

        public MissileAbilityRuntime CreateRuntime()
        {
            var specs = new StatusEffectSpec[onHitStatuses?.Length ?? 0];
            for (int i = 0; i < specs.Length; i++)
                specs[i] = onHitStatuses[i] != null ? onHitStatuses[i].ToSpec() : default;

            return new MissileAbilityRuntime(new MissileAbilityDefinition(
                abilityId, slot, weaponTag, damageMultiplier, baseCooldown, range,
                projectileSpeed, pierceCount, arcAngle, blastRadius, gravity, projectileCount,
                spawnHeight, spawnForwardOffset, knockbackMultiplier, element, specs));
        }
    }
}
