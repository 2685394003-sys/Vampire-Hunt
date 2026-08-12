using System;

public enum PlayerStatType
{
    MaxHealth,
    MaxStamina,
    DashStaminaCost,
    StaminaRecovery,
    BaseAttack,
    AttackRange,
    AttackInterval,
    KnockbackForce,
    MoveSpeed,
    CritRate,
    CritDamage,
    InvincibleTime,
    FlashSpeed,
    MaxScarlet
}

public enum PlayerStatOperation
{
    Add,
    Multiply
}

/// <summary>
/// One roguelike run modifier. Multiply uses a direct factor: 1.1 means +10%,
/// while 0.9 means -10%.
/// </summary>
[Serializable]
public struct PlayerStatUpgrade
{
    public PlayerStatType stat;
    public PlayerStatOperation operation;
    public float value;

    public PlayerStatUpgrade(
        PlayerStatType stat,
        float value,
        PlayerStatOperation operation = PlayerStatOperation.Add)
    {
        this.stat = stat;
        this.value = value;
        this.operation = operation;
    }
}

/// <summary>Read/write boundary for future upgrade cards, pickups and shops.</summary>
public interface IPlayerRunStats
{
    string PlayerId { get; }
    int MaxHealth { get; }
    int CurrentHealth { get; }
    float MaxStamina { get; }
    float CurrentStamina { get; }
    float DashStaminaCost { get; }
    float StaminaRecoverySpeed { get; }
    float Damage { get; }
    float WeaponRange { get; }
    float AttackCooldown { get; }
    float KnockbackForce { get; }
    float MoveSpeed { get; }
    float CritRate { get; }
    float CritDamage { get; }
    float InvincibleTime { get; }
    float FlashSpeed { get; }
    float MaxScarlet { get; }
    float CurrentScarlet { get; }

    bool ApplyRunUpgrade(PlayerStatUpgrade upgrade);
    void ResetForNewRun();
}
