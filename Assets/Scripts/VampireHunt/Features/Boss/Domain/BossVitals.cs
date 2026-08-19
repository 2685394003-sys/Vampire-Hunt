using System;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;

namespace VampireHunt.Boss.Domain
{
    internal sealed class BossVitals : IDamageReceiver
    {
        private readonly BossEncounterState encounter;

        public int CurrentHealth { get; private set; }
        public int MaxHealth { get; }
        public bool IsInvulnerable { get; private set; }
        public bool IsAlive => CurrentHealth > 0;
        public EntityId LastDamageSourceId { get; private set; }

        public BossVitals(int maxHealth, BossEncounterState encounter)
        {
            if (maxHealth <= 0) throw new ArgumentOutOfRangeException(nameof(maxHealth));
            MaxHealth = maxHealth;
            CurrentHealth = maxHealth;
            this.encounter = encounter ?? throw new ArgumentNullException(nameof(encounter));
        }

        public DamageResult ApplyDamage(in ResolvedDamage damage)
        {
            if (damage.FinalDamage <= 0 || !IsAlive || IsInvulnerable)
                return DamageResult.NoDamage(damage.FinalDamage, damage.Hit.Position);

            int mitigated = encounter.ResolveIncomingDamage(damage.FinalDamage);
            int applied = Math.Min(CurrentHealth, Math.Max(0, mitigated));
            CurrentHealth -= applied;
            if (applied > 0) LastDamageSourceId = damage.SourceId;
            return new DamageResult(
                damage.FinalDamage,
                applied,
                damage.WasCritical,
                CurrentHealth <= 0,
                damage.Hit.Position);
        }

        public void SetInvulnerable(bool value)
        {
            if (IsAlive) IsInvulnerable = value;
        }

        public void Reset()
        {
            CurrentHealth = MaxHealth;
            IsInvulnerable = false;
            LastDamageSourceId = default;
        }
    }
}
