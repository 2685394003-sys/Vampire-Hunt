using System;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;

namespace VampireHunt.Enemies.Domain
{
    /// <summary>Authoritative enemy life state; all damage enters via Combat.</summary>
    public sealed class EnemyVitals : IDamageReceiver, IHealingReceiver
    {
        private EntityId _ownerId;

        public EntityId OwnerId => _ownerId;
        public int MaxHealth { get; private set; }
        public int CurrentHealth { get; private set; }
        public bool IsAlive => CurrentHealth > 0;

        public void Reset(EntityId ownerId, int maxHealth)
        {
            if (!ownerId.IsValid) throw new ArgumentException("A spawned enemy requires a valid EntityId.", nameof(ownerId));
            if (maxHealth <= 0) throw new ArgumentOutOfRangeException(nameof(maxHealth));
            _ownerId = ownerId;
            MaxHealth = maxHealth;
            CurrentHealth = maxHealth;
        }

        public DamageResult ApplyDamage(in ResolvedDamage damage)
        {
            int requested = Math.Max(0, damage.FinalDamage);
            if (!IsAlive || damage.TargetId != _ownerId || requested == 0)
            {
                return new DamageResult(requested, 0, damage.WasCritical, false, damage.Hit.Position);
            }

            int applied = Math.Min(CurrentHealth, requested);
            CurrentHealth -= applied;
            bool killed = CurrentHealth == 0;
            return new DamageResult(requested, applied, damage.WasCritical, killed, damage.Hit.Position);
        }

        public int ApplyHealing(int amount)
        {
            if (amount <= 0 || !IsAlive) return 0;
            int applied = Math.Min(amount, MaxHealth - CurrentHealth);
            CurrentHealth += applied;
            return applied;
        }
    }
}
