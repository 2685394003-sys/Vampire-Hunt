using System;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;

namespace VampireHunt.Player.Domain
{
    /// <summary>Domain-only damage outcome used before Combat adapts it to DamageResult.</summary>
    public readonly struct PlayerDamageOutcome
    {
        public int RequestedDamage { get; }
        public int AppliedDamage { get; }
        public bool WasKilled { get; }
        public bool WasInvincible { get; }
        public bool WasCritical { get; }
        public EntityId SourceId { get; }
        public WorldPosition HitPosition { get; }

        public PlayerDamageOutcome(
            int requestedDamage,
            int appliedDamage,
            bool wasKilled,
            bool wasInvincible,
            bool wasCritical,
            EntityId sourceId,
            WorldPosition hitPosition)
        {
            RequestedDamage = Math.Max(0, requestedDamage);
            AppliedDamage = Math.Max(0, appliedDamage);
            WasKilled = wasKilled;
            WasInvincible = wasInvincible;
            WasCritical = wasCritical;
            SourceId = sourceId;
            HitPosition = hitPosition;
        }
    }

    /// <summary>Server-owned health and invincibility-window rules.</summary>
    public sealed class PlayerVitals : IDamageReceiver, IHealingReceiver
    {
        private int currentHealth;
        private double invincibleUntil;

        public int CurrentHealth => currentHealth;
        public int MaxHealth { get; private set; }
        public bool IsAlive => currentHealth > 0;
        public bool IsInvincibleWindow { get; private set; }
        public double InvincibleUntil => invincibleUntil;
        public double InvincibilityDuration { get; private set; }

        public PlayerVitals(int maxHealth, int? initialHealth = null, double invincibilityDuration = 0d)
        {
            MaxHealth = Math.Max(1, maxHealth);
            currentHealth = Math.Clamp(initialHealth ?? MaxHealth, 0, MaxHealth);
            invincibleUntil = double.NegativeInfinity;
            IsInvincibleWindow = false;
            InvincibilityDuration = Math.Max(0d, invincibilityDuration);
        }

        public void SetInvincibilityDuration(double duration) =>
            InvincibilityDuration = Math.Max(0d, duration);

        public void SetMaxHealth(int maxHealth, bool preserveRatio = false)
        {
            maxHealth = Math.Max(1, maxHealth);
            if (preserveRatio && MaxHealth > 0)
            {
                double ratio = currentHealth / (double)MaxHealth;
                MaxHealth = maxHealth;
                currentHealth = Math.Clamp((int)Math.Round(MaxHealth * ratio), 0, MaxHealth);
                return;
            }

            MaxHealth = maxHealth;
            currentHealth = Math.Min(currentHealth, MaxHealth);
        }

        public void RestoreToFull()
        {
            currentHealth = MaxHealth;
            IsInvincibleWindow = false;
            invincibleUntil = double.NegativeInfinity;
        }

        public void Reset(int? health = null)
        {
            currentHealth = Math.Clamp(health ?? MaxHealth, 0, MaxHealth);
            IsInvincibleWindow = false;
            invincibleUntil = double.NegativeInfinity;
        }

        public bool IsInvincibleAt(double now)
        {
            if (!IsAlive || double.IsNaN(now) || double.IsInfinity(now)) return false;
            bool active = now < invincibleUntil;
            IsInvincibleWindow = active;
            return active;
        }

        public void BeginInvincibility(double now, double duration)
        {
            if (!IsAlive || double.IsNaN(now) || double.IsInfinity(now)) return;
            duration = Math.Max(0d, duration);
            invincibleUntil = Math.Max(invincibleUntil, now + duration);
            IsInvincibleWindow = now < invincibleUntil;
        }

        public PlayerDamageOutcome ApplyDamage(
            int damage,
            double now,
            double invincibilityDuration,
            EntityId sourceId = default,
            WorldPosition hitPosition = default,
            bool wasCritical = false)
        {
            int requested = Math.Max(0, damage);
            if (!IsAlive || requested == 0)
            {
                IsInvincibleWindow = IsInvincibleAt(now);
                return new PlayerDamageOutcome(
                    requested,
                    0,
                    false,
                    false,
                    wasCritical,
                    sourceId,
                    hitPosition);
            }

            if (IsInvincibleAt(now))
            {
                return new PlayerDamageOutcome(
                    requested,
                    0,
                    false,
                    true,
                    wasCritical,
                    sourceId,
                    hitPosition);
            }

            int applied = Math.Min(requested, currentHealth);
            currentHealth -= applied;
            bool killed = currentHealth == 0;
            if (!killed) BeginInvincibility(now, invincibilityDuration);
            else
            {
                IsInvincibleWindow = false;
                invincibleUntil = double.NegativeInfinity;
            }

            return new PlayerDamageOutcome(
                requested,
                applied,
                killed,
                false,
                wasCritical,
                sourceId,
                hitPosition);
        }

        public int ApplyHealing(int amount)
        {
            if (!IsAlive || amount <= 0) return 0;
            int applied = Math.Min(amount, MaxHealth - currentHealth);
            currentHealth += applied;
            return applied;
        }

        public DamageResult ApplyDamage(in ResolvedDamage damage)
        {
            return ApplyDamage(in damage, 0d);
        }

        public DamageResult ApplyDamage(in ResolvedDamage damage, double now)
        {
            PlayerDamageOutcome outcome = ApplyDamage(
                damage.FinalDamage,
                now,
                InvincibilityDuration,
                damage.SourceId,
                damage.Hit.Position,
                damage.WasCritical);
            return new DamageResult(
                damage.FinalDamage,
                outcome.AppliedDamage,
                damage.WasCritical,
                outcome.WasKilled,
                damage.Hit.Position);
        }
    }
}
