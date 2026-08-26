using System;

namespace VampireHunt.Boss.Abilities
{
    /// <summary>
    /// Unity-free, data-table payload shared by Boss skill assets. A skill logic reads only
    /// the fields it needs; unused fields stay harmless. Keeping this value in the ability
    /// asset lets designers tune combat without editing or duplicating scripts.
    /// </summary>
    [Serializable]
    public sealed class BossAbilityTuning
    {
        public float Damage = 10f;
        public float Knockback = 4f;
        public float Radius = 4f;
        public float Range = 8f;
        public float Width = 2f;
        public float Height = 2f;
        public int Repetitions = 1;
        public float Interval = 0.25f;
        public uint ProjectileId = 1;
        public int ProjectileCount = 16;
        public float ProjectileSpeed = 8f;
        public float ProjectileScale = 1f;
        public float RotationSpeed = 35f;
        public float Duration = 2f;
        public uint StatusId;
        public float StatusDuration = 1f;
        public float ClockDrainRate = 2f;
        public float TravelDuration = 0.45f;
        public float DissolveDuration = 0.3f;
        public float VfxHeight = 1.2f;

        public BossAbilityTuning CloneValidated()
        {
            return new BossAbilityTuning
            {
                Damage = Math.Max(0f, Damage),
                Knockback = Math.Max(0f, Knockback),
                Radius = Math.Max(0.05f, Radius),
                Range = Math.Max(0.05f, Range),
                Width = Math.Max(0.05f, Width),
                Height = Math.Max(0.05f, Height),
                Repetitions = Math.Max(1, Repetitions),
                Interval = Math.Max(0.01f, Interval),
                ProjectileId = Math.Max(1u, ProjectileId),
                ProjectileCount = Math.Max(1, ProjectileCount),
                ProjectileSpeed = Math.Max(0.1f, ProjectileSpeed),
                ProjectileScale = Math.Max(0.01f, ProjectileScale),
                RotationSpeed = RotationSpeed,
                Duration = Math.Max(0.01f, Duration),
                StatusId = StatusId,
                StatusDuration = Math.Max(0.01f, StatusDuration),
                ClockDrainRate = Math.Max(0.01f, ClockDrainRate),
                TravelDuration = Math.Max(0.01f, TravelDuration),
                DissolveDuration = Math.Max(0.01f, DissolveDuration),
                VfxHeight = Math.Max(0f, VfxHeight)
            };
        }
    }

    public interface IBossAbilityTuningConsumer
    {
        void BindTuning(BossAbilityTuning tuning);
    }
}
