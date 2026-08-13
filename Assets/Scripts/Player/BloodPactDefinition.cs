using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One design-authored blood pact row. Ordinary numeric columns and advanced
/// GAS abilities share the same immutable run-balance asset.
/// </summary>
[Serializable]
public sealed class BloodPactDefinition
{
    [SerializeField] private string displayName;
    [SerializeField] private string pactId;
    [SerializeField] private int tier;
    [SerializeField] private float moveSpeed;
    [SerializeField] private float attackPower;
    [SerializeField] private float maxHealth;
    [SerializeField] private float maxStamina;
    [SerializeField] private float critRate;
    [SerializeField] private float critDamage;
    [SerializeField] private float cooldownReduction;
    [SerializeField] private float weaponRange;
    [SerializeField] private float knockbackForce;
    [SerializeField] private float invincibleTime;
    [SerializeField, TextArea] private string specialEffect;
    [SerializeField] private List<GameplayAbilityDefinition> gameplayAbilities = new();

    public string DisplayName => displayName;
    public string PactId => pactId;
    public int Tier => tier;
    public bool IsPlayerPact => tier > 0;
    public bool IsEnemyPact => tier < 0;
    public bool IsRepeatable => tier == 1;
    public string SpecialEffect => specialEffect;
    public bool HasSpecialEffect => !string.IsNullOrWhiteSpace(specialEffect);
    public IReadOnlyList<GameplayAbilityDefinition> GameplayAbilities =>
        gameplayAbilities ??
        (IReadOnlyList<GameplayAbilityDefinition>)Array.Empty<GameplayAbilityDefinition>();
    public bool HasGameplayAbilities => GameplayAbilities.Count > 0;
    public bool HasNumericEffects =>
        !Mathf.Approximately(moveSpeed, 0f) ||
        !Mathf.Approximately(attackPower, 0f) ||
        !Mathf.Approximately(maxHealth, 0f) ||
        !Mathf.Approximately(maxStamina, 0f) ||
        !Mathf.Approximately(critRate, 0f) ||
        !Mathf.Approximately(critDamage, 0f) ||
        !Mathf.Approximately(cooldownReduction, 0f) ||
        !Mathf.Approximately(weaponRange, 0f) ||
        !Mathf.Approximately(knockbackForce, 0f) ||
        !Mathf.Approximately(invincibleTime, 0f);
    public bool HasImplementedEffects => HasNumericEffects || HasGameplayAbilities;
    public bool IsRuntimeImplemented =>
        HasImplementedEffects && (!HasSpecialEffect || HasGameplayAbilities);

    public string BuildCardDescription()
    {
        List<string> lines = new();
        AddFlatLine(lines, "移动速度", moveSpeed);
        AddFlatLine(lines, "攻击力", attackPower);
        AddFlatLine(lines, "最大生命", maxHealth);
        AddFlatLine(lines, "最大体力", maxStamina);
        AddPercentLine(lines, "暴击率", critRate);
        AddFlatLine(lines, "暴击伤害", critDamage);
        if (!Mathf.Approximately(cooldownReduction, 0f))
            lines.Add($"攻击间隔 -{cooldownReduction:0.##}秒");
        AddFlatLine(lines, "攻击范围", weaponRange);
        AddFlatLine(lines, "击退力量", knockbackForce);
        AddFlatLine(lines, "无敌时间", invincibleTime, "秒");

        if (HasSpecialEffect)
        {
            lines.Add(specialEffect.Trim());
        }

        return lines.Count > 0 ? string.Join("\n", lines) : "神秘效果等待觉醒";
    }

    public bool ApplyNumericEffects(PlayerNetworkState player)
    {
        if (!NetworkAuthority.IsServerOrOffline()) return false;

        string sourceId = $"blood_pact:{pactId}";
        if (IsPlayerPact)
        {
            if (player == null) return false;
            ApplyPlayerFlat(player, sourceId, PlayerStatType.MoveSpeed, moveSpeed);
            ApplyPlayerFlat(player, sourceId, PlayerStatType.BaseAttack, attackPower);
            ApplyPlayerFlat(player, sourceId, PlayerStatType.MaxHealth, maxHealth);
            ApplyPlayerFlat(player, sourceId, PlayerStatType.MaxStamina, maxStamina);
            ApplyPlayerFlat(player, sourceId, PlayerStatType.CritRate, critRate);
            ApplyPlayerFlat(player, sourceId, PlayerStatType.CritDamage, critDamage);
            ApplyPlayerFlat(
                player,
                sourceId,
                PlayerStatType.AttackInterval,
                -cooldownReduction);
            ApplyPlayerFlat(player, sourceId, PlayerStatType.AttackRange, weaponRange);
            ApplyPlayerFlat(player, sourceId, PlayerStatType.KnockbackForce, knockbackForce);
            ApplyPlayerFlat(player, sourceId, PlayerStatType.InvincibleTime, invincibleTime);
            return true;
        }

        if (!IsEnemyPact) return false;
        ApplyEnemyFlat(sourceId, EnemyStatType.MoveSpeed, moveSpeed);
        ApplyEnemyFlat(sourceId, EnemyStatType.Damage, attackPower);
        ApplyEnemyFlat(sourceId, EnemyStatType.MaxHealth, maxHealth);
        ApplyEnemyFlat(sourceId, EnemyStatType.AttackCooldown, -cooldownReduction);
        ApplyEnemyFlat(sourceId, EnemyStatType.WeaponRange, weaponRange);
        ApplyEnemyFlat(sourceId, EnemyStatType.KnockbackForce, knockbackForce);
        return true;
    }

    /// <summary>Atomically installs numeric modifiers and advanced abilities.</summary>
    public bool ApplyEffects(PlayerNetworkState player)
    {
        if (!NetworkAuthority.IsServerOrOffline() || !HasImplementedEffects) return false;
        if (IsEnemyPact) return ApplyNumericEffects(player);
        if (!IsPlayerPact || player == null ||
            !player.AbilitySystem.CanGrant(GameplayAbilities))
        {
            return false;
        }

        if (!ApplyNumericEffects(player)) return false;
        string sourceId = $"blood_pact:{pactId}";
        if (player.AbilitySystem.GrantAbilities(GameplayAbilities, sourceId)) return true;

        RemoveNumericEffects(player);
        return false;
    }

    public int RemoveNumericEffects(PlayerNetworkState player)
    {
        string sourceId = $"blood_pact:{pactId}";
        return IsPlayerPact
            ? player != null ? player.RemoveStatModifiersFromSource(sourceId) : 0
            : IsEnemyPact ? EnemyRunStats.RemoveModifiersFromSource(sourceId) : 0;
    }

    public int RemoveEffects(PlayerNetworkState player)
    {
        string sourceId = $"blood_pact:{pactId}";
        int removed = RemoveNumericEffects(player);
        if (IsPlayerPact && player?.AbilitySystem != null)
            removed += player.AbilitySystem.RemoveAbilitiesBySource(sourceId);
        return removed;
    }

    private void ApplyPlayerFlat(
        PlayerNetworkState player,
        string sourceId,
        PlayerStatType stat,
        float value)
    {
        if (Mathf.Approximately(value, 0f)) return;
        player.AddStatModifier(new PlayerStatModifier(
            $"{sourceId}:{stat}",
            sourceId,
            stat,
            PlayerModifierOperation.Flat,
            value,
            maxStacks: IsRepeatable ? int.MaxValue : 1));
    }

    private static void ApplyEnemyFlat(string sourceId, EnemyStatType stat, float value)
    {
        if (Mathf.Approximately(value, 0f)) return;
        EnemyRunStats.AddModifier(new EnemyStatModifier(
            $"{sourceId}:{stat}",
            sourceId,
            stat,
            PlayerModifierOperation.Flat,
            value));
    }

    private static void AddFlatLine(
        List<string> lines,
        string label,
        float value,
        string suffix = "")
    {
        if (Mathf.Approximately(value, 0f)) return;
        string sign = value > 0f ? "+" : string.Empty;
        lines.Add($"{label} {sign}{value:0.##}{suffix}");
    }

    private static void AddPercentLine(List<string> lines, string label, float value)
    {
        if (Mathf.Approximately(value, 0f)) return;
        string sign = value > 0f ? "+" : string.Empty;
        lines.Add($"{label} {sign}{value * 100f:0.#}%");
    }
}
