using UnityEngine;

/// <summary>
/// Immutable player baseline authored by design. Runtime upgrades must be applied
/// to PlayerNetworkState and never mutate this asset.
/// </summary>
[CreateAssetMenu(
    fileName = "PlayerStats",
    menuName = "Vampire Hunt/Balance/Player Stats",
    order = 1)]
public sealed class PlayerStatsConfig : ScriptableObject
{
    private const string DefaultResourcePath = "GameBalance/PlayerMainStats";

    [Header("策划标识 / Design Identity")]
    [SerializeField] private string playerId = "player_main";

    [Header("生存 / Survival")]
    [SerializeField, Min(1f)] private float maxHp = 100f;
    [SerializeField, Min(0f)] private float maxStamina = 100f;
    [SerializeField, Min(0f)] private float dashStaminaCost = 15f;
    [SerializeField, Min(0f)] private float staminaRecoverSpeed = 15f;
    [SerializeField, Min(0f)] private float maxScarlet = 100f;

    [Header("战斗 / Combat")]
    [SerializeField, Min(0f)] private float baseAtk = 10f;
    [SerializeField, Min(0f)] private float attackRange = 2f;
    [SerializeField, Min(0.01f)] private float attackInterval = 1f;
    [SerializeField, Min(0f)] private float knockbackForce = 5f;
    [SerializeField, Range(0f, 1f)] private float critRate = 0.05f;
    [SerializeField, Min(1f)] private float critDamage = 2f;

    [Header("移动与受伤反馈 / Movement & Hurt Feedback")]
    [SerializeField, Min(0f)] private float moveSpeed = 5f;
    [SerializeField, Min(0f)] private float invincibleTime = 0.8f;
    [Tooltip("每秒闪烁次数 / flashes per second")]
    [SerializeField, Min(0.01f)] private float flashSpeed = 10f;

    [Header("战斗系统补充参数 / Supplemental Combat Settings")]
    [Tooltip("策划 CSV 暂未包含：击退维持时间。")]
    [SerializeField, Min(0f)] private float knockbackDuration = 2f;
    [Tooltip("策划 CSV 暂未包含：敌人受击后的额外硬直时间。")]
    [SerializeField, Min(0f)] private float stunDuration = 2f;
    [SerializeField] private LayerMask enemyLayer = 1 << 7;

    public string PlayerId => playerId;
    public int MaxHealth => Mathf.Max(1, Mathf.RoundToInt(maxHp));
    public float MaxHp => maxHp;
    public float MaxStamina => maxStamina;
    public float DashStaminaCost => dashStaminaCost;
    public float StaminaRecoverSpeed => staminaRecoverSpeed;
    public float BaseAttack => baseAtk;
    public float AttackRange => attackRange;
    public float AttackInterval => attackInterval;
    public float KnockbackForce => knockbackForce;
    public float MoveSpeed => moveSpeed;
    public float CritRate => critRate;
    public float CritDamage => critDamage;
    public float InvincibleTime => invincibleTime;
    public float FlashSpeed => flashSpeed;
    public float MaxScarlet => maxScarlet;
    public float KnockbackDuration => knockbackDuration;
    public float StunDuration => stunDuration;
    public LayerMask EnemyLayer => enemyLayer;

    public static PlayerStatsConfig LoadDefault() =>
        Resources.Load<PlayerStatsConfig>(DefaultResourcePath);

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(playerId)) playerId = name;
        maxHp = Mathf.Max(1f, maxHp);
        maxStamina = Mathf.Max(0f, maxStamina);
        dashStaminaCost = Mathf.Max(0f, dashStaminaCost);
        staminaRecoverSpeed = Mathf.Max(0f, staminaRecoverSpeed);
        baseAtk = Mathf.Max(0f, baseAtk);
        attackRange = Mathf.Max(0f, attackRange);
        attackInterval = Mathf.Max(0.01f, attackInterval);
        knockbackForce = Mathf.Max(0f, knockbackForce);
        moveSpeed = Mathf.Max(0f, moveSpeed);
        critRate = Mathf.Clamp01(critRate);
        critDamage = Mathf.Max(1f, critDamage);
        invincibleTime = Mathf.Max(0f, invincibleTime);
        flashSpeed = Mathf.Max(0.01f, flashSpeed);
        maxScarlet = Mathf.Max(0f, maxScarlet);
        knockbackDuration = Mathf.Max(0f, knockbackDuration);
        stunDuration = Mathf.Max(0f, stunDuration);
    }
}
