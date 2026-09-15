using System;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Player.Abilities.FlameThrower;

namespace VampireHunt.Infrastructure.Unity
{
    /// <summary>
    /// Inspector-friendly ScriptableObject for the short-range cone flamethrower.
    /// Fixed Fire element; burns (Burn) on hit by default.
    /// </summary>
    [CreateAssetMenu(fileName = "FlameThrowerAbility", menuName = "Vampire Hunt/Combat/Flame Thrower Ability")]
    public sealed class FlameThrowerAbilityAsset : ScriptableObject
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
        [Min(1)] [SerializeField] private uint abilityId = 150;
        [SerializeField] private AbilitySlot slot = AbilitySlot.Primary;
        [SerializeField] private DamageTags weaponTag = DamageTags.Flame;

        [Header("Cone")]
        [Tooltip("Multiplier applied to the player's base Damage per tick.")]
        [Min(0f)] [SerializeField] private float damageMultiplier = 0.3f;
        [Tooltip("Time between damage ticks; also gates the hold-to-fire cadence.")]
        [Min(0.01f)] [SerializeField] private float tickInterval = 0.1f;
        [Min(0.1f)] [SerializeField] private float range = 5f;
        [Tooltip("Cone spread angle in degrees.")]
        [Range(0f, 180f)] [SerializeField] private float coneAngle = 60f;
        [Min(0f)] [SerializeField] private float spawnForwardOffset = 0.8f;
        [SerializeField] private float spawnHeight = 1f;

        [Header("Element and on-hit effects")]
        [SerializeField] private ElementId element = ElementId.Fire;
        [SerializeField] private StatusEntry[] onHitStatuses = Array.Empty<StatusEntry>();

        public FlameThrowerAbilityRuntime CreateRuntime()
        {
            var specs = new StatusEffectSpec[onHitStatuses?.Length ?? 0];
            for (int i = 0; i < specs.Length; i++)
                specs[i] = onHitStatuses[i] != null ? onHitStatuses[i].ToSpec() : default;

            return new FlameThrowerAbilityRuntime(new FlameThrowerAbilityDefinition(
                abilityId, slot, weaponTag, damageMultiplier, tickInterval, range, coneAngle,
                spawnHeight, spawnForwardOffset, element, specs));
        }
    }
}
