using System.Collections.Generic;
using UnityEngine;
using VampireHunt.Player.Domain;

namespace VampireHunt.Player.Authoring
{
    /// <summary>
    /// Editable balance asset. Runtime code receives only the immutable PlayerSpec
    /// produced by CreateSpec; it never retains this ScriptableObject.
    /// </summary>
    [CreateAssetMenu(
        fileName = "PlayerDefinition",
        menuName = "Vampire Hunt/Player/Player Definition",
        order = 1)]
    public sealed class PlayerDefinition : ScriptableObject
    {
        [SerializeField] private int maxHealth = 100;
        [SerializeField] private float maxStamina = 100f;
        [SerializeField] private float dashStaminaCost = 15f;
        [SerializeField] private float staminaRecoveryPerSecond = 15f;
        [SerializeField] private float dashCooldown = 1f;
        [SerializeField] private float dashDuration = 0.15f;
        [SerializeField] private float dashSpeedMultiplier = 2f;
        [SerializeField] private float attackInterval = 1f;
        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float baseAttack = 10f;
        [SerializeField] private float attackRange = 2f;
        [SerializeField] private float attackConeAngle = 110f;
        [SerializeField] private float critRate = 0.05f;
        [SerializeField] private float critDamage = 2f;
        [SerializeField] private float invincibleTime = 0.8f;
        [SerializeField] private float maxScarlet = 100f;
        [SerializeField] private float knockbackForce = 5f;
        [SerializeField] private float knockbackDuration = 0.2f;
        [SerializeField] private float stunDuration = 0.2f;

        public int MaxHealth => Mathf.Max(1, maxHealth);
        public float MaxStamina => Mathf.Max(0f, maxStamina);
        public float DashStaminaCost => Mathf.Max(0f, dashStaminaCost);
        public float StaminaRecoveryPerSecond => Mathf.Max(0f, staminaRecoveryPerSecond);
        public float DashCooldown => Mathf.Max(0f, dashCooldown);
        public float DashDuration => Mathf.Max(0f, dashDuration);
        public float DashSpeedMultiplier => Mathf.Max(1f, dashSpeedMultiplier);
        public float AttackInterval => Mathf.Max(0f, attackInterval);
        public float MoveSpeed => Mathf.Max(0f, moveSpeed);
        public float BaseAttack => Mathf.Max(0f, baseAttack);
        public float AttackRange => Mathf.Max(0f, attackRange);
        public float AttackConeAngle => Mathf.Clamp(attackConeAngle, 0f, 180f);
        public float CritRate => Mathf.Clamp01(critRate);
        public float CritDamage => Mathf.Max(1f, critDamage);
        public float InvincibleTime => Mathf.Max(0f, invincibleTime);
        public float MaxScarlet => Mathf.Max(0f, maxScarlet);
        public float KnockbackForce => Mathf.Max(0f, knockbackForce);
        public float KnockbackDuration => Mathf.Max(0f, knockbackDuration);
        public float StunDuration => Mathf.Max(0f, stunDuration);

        public PlayerSpec CreateSpec()
        {
            Dictionary<PlayerStat, float> values = new()
            {
                [PlayerStat.MaxHealth] = MaxHealth,
                [PlayerStat.MaxStamina] = MaxStamina,
                [PlayerStat.DashStaminaCost] = DashStaminaCost,
                [PlayerStat.StaminaRecovery] = StaminaRecoveryPerSecond,
                [PlayerStat.BaseAttack] = BaseAttack,
                [PlayerStat.AttackRange] = AttackRange,
                [PlayerStat.AttackInterval] = AttackInterval,
                [PlayerStat.MoveSpeed] = MoveSpeed,
                [PlayerStat.CritRate] = CritRate,
                [PlayerStat.CritDamage] = CritDamage,
                [PlayerStat.InvincibleTime] = InvincibleTime,
                [PlayerStat.MaxScarlet] = MaxScarlet,
                [PlayerStat.KnockbackForce] = KnockbackForce,
                [PlayerStat.DashSpeedMultiplier] = DashSpeedMultiplier,
                [PlayerStat.DashDuration] = DashDuration
            };

            return new PlayerSpec(
                MaxHealth,
                MaxStamina,
                DashStaminaCost,
                StaminaRecoveryPerSecond,
                DashCooldown,
                DashDuration,
                DashSpeedMultiplier,
                AttackInterval,
                values,
                AttackConeAngle,
                KnockbackForce,
                KnockbackDuration,
                StunDuration);
        }

        private void OnValidate()
        {
            maxHealth = Mathf.Max(1, maxHealth);
            maxStamina = Mathf.Max(0f, maxStamina);
            dashStaminaCost = Mathf.Max(0f, dashStaminaCost);
            staminaRecoveryPerSecond = Mathf.Max(0f, staminaRecoveryPerSecond);
            dashCooldown = Mathf.Max(0f, dashCooldown);
            dashDuration = Mathf.Max(0f, dashDuration);
            dashSpeedMultiplier = Mathf.Max(1f, dashSpeedMultiplier);
            attackInterval = Mathf.Max(0f, attackInterval);
            moveSpeed = Mathf.Max(0f, moveSpeed);
            baseAttack = Mathf.Max(0f, baseAttack);
            attackRange = Mathf.Max(0f, attackRange);
            attackConeAngle = Mathf.Clamp(attackConeAngle, 0f, 180f);
            critRate = Mathf.Clamp01(critRate);
            critDamage = Mathf.Max(1f, critDamage);
            invincibleTime = Mathf.Max(0f, invincibleTime);
            maxScarlet = Mathf.Max(0f, maxScarlet);
            knockbackForce = Mathf.Max(0f, knockbackForce);
            knockbackDuration = Mathf.Max(0f, knockbackDuration);
            stunDuration = Mathf.Max(0f, stunDuration);
        }
    }

    public static class PlayerSpecFactory
    {
        public static PlayerSpec Create(PlayerDefinition definition)
        {
            if (definition == null) throw new System.ArgumentNullException(nameof(definition));
            return definition.CreateSpec();
        }
    }
}
