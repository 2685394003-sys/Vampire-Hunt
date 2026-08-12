using System;
using UnityEngine;

/// <summary>
/// One design-authored blood pact row. SpecialEffect is descriptive data only;
/// this class deliberately applies only the ordinary numeric columns.
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

    public string DisplayName => displayName;
    public string PactId => pactId;
    public int Tier => tier;
    public bool IsPlayerPact => tier > 0;
    public bool IsEnemyPact => tier < 0;
    public string SpecialEffect => specialEffect;
    public bool HasSpecialEffect => !string.IsNullOrWhiteSpace(specialEffect);

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

    public int RemoveNumericEffects(PlayerNetworkState player)
    {
        string sourceId = $"blood_pact:{pactId}";
        return IsPlayerPact
            ? player != null ? player.RemoveStatModifiersFromSource(sourceId) : 0
            : IsEnemyPact ? EnemyRunStats.RemoveModifiersFromSource(sourceId) : 0;
    }

    private static void ApplyPlayerFlat(
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
            value));
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
}
