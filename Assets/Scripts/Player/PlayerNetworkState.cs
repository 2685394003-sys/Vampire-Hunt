using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-owned per-player run state. PlayerStatsConfig is the immutable baseline;
/// every roguelike upgrade is applied to this runtime copy and replicated.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class PlayerNetworkState : NetworkBehaviour, IPlayerRunStats
{
    [SerializeField] private PlayerStatsConfig baseStats;
    [SerializeField] private bool hideVisualsWhenDead = true;

    private readonly NetworkVariable<int> networkMaxHealth = ServerVariable(1);
    private readonly NetworkVariable<int> networkHealth = ServerVariable(1);
    private readonly NetworkVariable<float> networkMaxStamina = ServerVariable(100f);
    private readonly NetworkVariable<float> networkStamina = ServerVariable(100f);
    private readonly NetworkVariable<float> networkDashStaminaCost = ServerVariable(15f);
    private readonly NetworkVariable<float> networkStaminaRecovery = ServerVariable(15f);
    private readonly NetworkVariable<float> networkDamage = ServerVariable(10f);
    private readonly NetworkVariable<float> networkWeaponRange = ServerVariable(2f);
    private readonly NetworkVariable<float> networkMoveSpeed = ServerVariable(5f);
    private readonly NetworkVariable<float> networkAttackCooldown = ServerVariable(1f);
    private readonly NetworkVariable<float> networkKnockbackForce = ServerVariable(5f);
    private readonly NetworkVariable<float> networkCritRate = ServerVariable(0.05f);
    private readonly NetworkVariable<float> networkCritDamage = ServerVariable(2f);
    private readonly NetworkVariable<float> networkInvincibleTime = ServerVariable(0.8f);
    private readonly NetworkVariable<float> networkFlashSpeed = ServerVariable(10f);
    private readonly NetworkVariable<float> networkMaxScarlet = ServerVariable(100f);
    private readonly NetworkVariable<float> networkScarlet = ServerVariable(0f);
    private readonly NetworkVariable<bool> networkAlive = ServerVariable(true);
    private readonly NetworkVariable<bool> networkInitialized = ServerVariable(false);

    private int offlineMaxHealth;
    private int offlineHealth;
    private float offlineMaxStamina;
    private float offlineStamina;
    private float offlineDashStaminaCost;
    private float offlineStaminaRecovery;
    private float offlineDamage;
    private float offlineWeaponRange;
    private float offlineMoveSpeed;
    private float offlineAttackCooldown;
    private float offlineKnockbackForce;
    private float offlineCritRate;
    private float offlineCritDamage;
    private float offlineInvincibleTime;
    private float offlineFlashSpeed;
    private float offlineMaxScarlet;
    private float offlineScarlet;
    private bool offlineAlive;
    private bool staminaRecoveryPaused;

    public event Action<int, int> HealthChanged;
    public event Action<float, float> StaminaChanged;
    public event Action<float, float> ScarletChanged;
    public event Action<bool> AliveChanged;
    public event Action<PlayerStatType> RunStatChanged;

    public PlayerStatsConfig BaseStats => ResolveBaseStats();
    public string PlayerId => BaseStats != null ? BaseStats.PlayerId : "player_missing_config";
    public int MaxHealth => UseNetworkValues ? networkMaxHealth.Value : offlineMaxHealth;
    public int CurrentHealth => UseNetworkValues ? networkHealth.Value : offlineHealth;
    public float MaxStamina => Read(networkMaxStamina, offlineMaxStamina);
    public float CurrentStamina => Read(networkStamina, offlineStamina);
    public float DashStaminaCost => Read(networkDashStaminaCost, offlineDashStaminaCost);
    public float StaminaRecoverySpeed => Read(networkStaminaRecovery, offlineStaminaRecovery);
    public float Damage => Read(networkDamage, offlineDamage);
    public float WeaponRange => Read(networkWeaponRange, offlineWeaponRange);
    public float MoveSpeed => Read(networkMoveSpeed, offlineMoveSpeed);
    public float AttackCooldown => Read(networkAttackCooldown, offlineAttackCooldown);
    public float KnockbackForce => Read(networkKnockbackForce, offlineKnockbackForce);
    public float CritRate => Read(networkCritRate, offlineCritRate);
    public float CritDamage => Read(networkCritDamage, offlineCritDamage);
    public float InvincibleTime => Read(networkInvincibleTime, offlineInvincibleTime);
    public float FlashSpeed => Read(networkFlashSpeed, offlineFlashSpeed);
    public float MaxScarlet => Read(networkMaxScarlet, offlineMaxScarlet);
    public float CurrentScarlet => Read(networkScarlet, offlineScarlet);
    public bool IsAlive => UseNetworkValues ? networkAlive.Value : offlineAlive;

    // These two supplemental timings are intentionally baseline-only until design
    // adds them to the progression sheet.
    public float KnockbackTime => BaseStats != null ? BaseStats.KnockbackDuration : 0f;
    public float StunTime => BaseStats != null ? BaseStats.StunDuration : 0f;
    public LayerMask EnemyLayer => BaseStats != null ? BaseStats.EnemyLayer : 0;

    private bool UseNetworkValues => NetworkAuthority.IsNetworkActive && IsSpawned;

    private static NetworkVariable<T> ServerVariable<T>(T value) where T : unmanaged =>
        new(value, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public static PlayerNetworkState EnsureForMigration(GameObject playerObject)
    {
        if (playerObject == null) return null;

        PlayerNetworkState existing = playerObject.GetComponent<PlayerNetworkState>();
        if (existing != null) return existing;

        if (NetworkAuthority.IsNetworkActive)
        {
            Debug.LogError(
                $"[Network] Player '{playerObject.name}' was spawned without PlayerNetworkState. " +
                "Fix the network player prefab; runtime network components cannot be added safely.",
                playerObject);
            return null;
        }

        return playerObject.AddComponent<PlayerNetworkState>();
    }

    private void Awake()
    {
        ResolveBaseStats();
        InitializeOfflineState();
    }

    private void OnEnable() => NetworkPlayerRegistry.Register(this);
    private void OnDisable() => NetworkPlayerRegistry.Unregister(this);

    public override void OnNetworkSpawn()
    {
        NetworkPlayerRegistry.Register(this);
        SubscribeNetworkEvents();

        if (IsServer && !networkInitialized.Value) InitializeNetworkState();
        PublishAll();
    }

    public override void OnNetworkDespawn()
    {
        UnsubscribeNetworkEvents();
        NetworkPlayerRegistry.Unregister(this);
    }

    private void Update()
    {
        if (!NetworkAuthority.IsServerOrOffline(this) ||
            !IsAlive ||
            staminaRecoveryPaused ||
            CurrentStamina >= MaxStamina)
        {
            return;
        }

        SetStamina(Mathf.Min(
            MaxStamina,
            CurrentStamina + StaminaRecoverySpeed * Time.deltaTime));
    }

    public bool TryConsumeStamina(float amount)
    {
        if (!NetworkAuthority.IsServerOrOffline(this) ||
            amount < 0f ||
            CurrentStamina + 0.0001f < amount)
        {
            return false;
        }

        SetStamina(CurrentStamina - amount);
        return true;
    }

    public void RestoreStamina(float amount)
    {
        if (NetworkAuthority.IsServerOrOffline(this) && amount > 0f)
            SetStamina(CurrentStamina + amount);
    }

    public void SetStaminaRecoveryPaused(bool value)
    {
        if (NetworkAuthority.IsServerOrOffline(this)) staminaRecoveryPaused = value;
    }

    public bool TryConsumeScarlet(float amount)
    {
        if (!NetworkAuthority.IsServerOrOffline(this) ||
            amount < 0f ||
            CurrentScarlet + 0.0001f < amount)
        {
            return false;
        }

        SetScarlet(CurrentScarlet - amount);
        return true;
    }

    public void AddScarlet(float amount)
    {
        if (NetworkAuthority.IsServerOrOffline(this) && amount > 0f)
            SetScarlet(CurrentScarlet + amount);
    }

    public bool ApplyDamage(int amount)
    {
        if (!NetworkAuthority.IsServerOrOffline(this) || amount <= 0 || !IsAlive)
            return false;

        SetHealth(Mathf.Max(0, CurrentHealth - amount));
        if (CurrentHealth <= 0) SetAlive(false);
        return true;
    }

    public void HealToFull()
    {
        if (!NetworkAuthority.IsServerOrOffline(this)) return;
        SetHealth(MaxHealth);
        SetAlive(true);
    }

    public void ForceDeath()
    {
        if (!NetworkAuthority.IsServerOrOffline(this)) return;
        SetHealth(0);
        SetAlive(false);
    }

    /// <summary>Rolls one server-authoritative damage value for a whole swing.</summary>
    public int RollAttackDamage(out bool wasCritical)
    {
        wasCritical = NetworkAuthority.IsServerOrOffline(this) &&
                      CritRate > 0f &&
                      UnityEngine.Random.value < CritRate;
        float result = Damage * (wasCritical ? CritDamage : 1f);
        return Mathf.Max(0, Mathf.RoundToInt(result));
    }

    public void ResetForNewRun()
    {
        if (!NetworkAuthority.IsServerOrOffline(this)) return;

        if (UseNetworkValues) InitializeNetworkState();
        else
        {
            InitializeOfflineState();
            PublishAll();
        }
    }

    public bool ApplyRunUpgrade(PlayerStatUpgrade upgrade)
    {
        if (!NetworkAuthority.IsServerOrOffline(this) ||
            float.IsNaN(upgrade.value) ||
            float.IsInfinity(upgrade.value) ||
            (upgrade.operation == PlayerStatOperation.Multiply && upgrade.value < 0f))
        {
            return false;
        }

        float current = GetStatValue(upgrade.stat);
        float next = upgrade.operation == PlayerStatOperation.Add
            ? current + upgrade.value
            : current * upgrade.value;
        SetStatValue(upgrade.stat, next);
        RunStatChanged?.Invoke(upgrade.stat);
        return true;
    }

    /// <summary>Compatibility helper for the existing Boss phase reward.</summary>
    public void ApplyUpgrade(int damageBonus, float moveSpeedBonus, int maxHealthBonus)
    {
        ApplyRunUpgrade(new PlayerStatUpgrade(PlayerStatType.BaseAttack, damageBonus));
        ApplyRunUpgrade(new PlayerStatUpgrade(PlayerStatType.MoveSpeed, moveSpeedBonus));
        ApplyRunUpgrade(new PlayerStatUpgrade(PlayerStatType.MaxHealth, maxHealthBonus));
    }

    private PlayerStatsConfig ResolveBaseStats()
    {
        if (baseStats == null) baseStats = PlayerStatsConfig.LoadDefault();
        if (baseStats == null)
        {
            Debug.LogError(
                "[Player Stats] Missing PlayerStatsConfig. Assign one on PlayerNetworkState " +
                "or create Resources/GameBalance/PlayerMainStats.asset.",
                this);
        }
        return baseStats;
    }

    private void InitializeOfflineState()
    {
        PlayerStatsConfig config = ResolveBaseStats();
        offlineMaxHealth = config != null ? config.MaxHealth : 1;
        offlineHealth = offlineMaxHealth;
        offlineMaxStamina = config != null ? config.MaxStamina : 100f;
        offlineStamina = offlineMaxStamina;
        offlineDashStaminaCost = config != null ? config.DashStaminaCost : 15f;
        offlineStaminaRecovery = config != null ? config.StaminaRecoverSpeed : 15f;
        offlineDamage = config != null ? config.BaseAttack : 10f;
        offlineWeaponRange = config != null ? config.AttackRange : 2f;
        offlineMoveSpeed = config != null ? config.MoveSpeed : 5f;
        offlineAttackCooldown = config != null ? config.AttackInterval : 1f;
        offlineKnockbackForce = config != null ? config.KnockbackForce : 5f;
        offlineCritRate = config != null ? config.CritRate : 0.05f;
        offlineCritDamage = config != null ? config.CritDamage : 2f;
        offlineInvincibleTime = config != null ? config.InvincibleTime : 0.8f;
        offlineFlashSpeed = config != null ? config.FlashSpeed : 10f;
        offlineMaxScarlet = config != null ? config.MaxScarlet : 100f;
        offlineScarlet = 0f;
        offlineAlive = true;
    }

    private void InitializeNetworkState()
    {
        PlayerStatsConfig config = ResolveBaseStats();
        networkMaxHealth.Value = config != null ? config.MaxHealth : 1;
        networkHealth.Value = networkMaxHealth.Value;
        networkMaxStamina.Value = config != null ? config.MaxStamina : 100f;
        networkStamina.Value = networkMaxStamina.Value;
        networkDashStaminaCost.Value = config != null ? config.DashStaminaCost : 15f;
        networkStaminaRecovery.Value = config != null ? config.StaminaRecoverSpeed : 15f;
        networkDamage.Value = config != null ? config.BaseAttack : 10f;
        networkWeaponRange.Value = config != null ? config.AttackRange : 2f;
        networkMoveSpeed.Value = config != null ? config.MoveSpeed : 5f;
        networkAttackCooldown.Value = config != null ? config.AttackInterval : 1f;
        networkKnockbackForce.Value = config != null ? config.KnockbackForce : 5f;
        networkCritRate.Value = config != null ? config.CritRate : 0.05f;
        networkCritDamage.Value = config != null ? config.CritDamage : 2f;
        networkInvincibleTime.Value = config != null ? config.InvincibleTime : 0.8f;
        networkFlashSpeed.Value = config != null ? config.FlashSpeed : 10f;
        networkMaxScarlet.Value = config != null ? config.MaxScarlet : 100f;
        networkScarlet.Value = 0f;
        networkAlive.Value = true;
        networkInitialized.Value = true;
    }

    private float GetStatValue(PlayerStatType stat) => stat switch
    {
        PlayerStatType.MaxHealth => MaxHealth,
        PlayerStatType.MaxStamina => MaxStamina,
        PlayerStatType.DashStaminaCost => DashStaminaCost,
        PlayerStatType.StaminaRecovery => StaminaRecoverySpeed,
        PlayerStatType.BaseAttack => Damage,
        PlayerStatType.AttackRange => WeaponRange,
        PlayerStatType.AttackInterval => AttackCooldown,
        PlayerStatType.KnockbackForce => KnockbackForce,
        PlayerStatType.MoveSpeed => MoveSpeed,
        PlayerStatType.CritRate => CritRate,
        PlayerStatType.CritDamage => CritDamage,
        PlayerStatType.InvincibleTime => InvincibleTime,
        PlayerStatType.FlashSpeed => FlashSpeed,
        PlayerStatType.MaxScarlet => MaxScarlet,
        _ => 0f
    };

    private void SetStatValue(PlayerStatType stat, float value)
    {
        switch (stat)
        {
            case PlayerStatType.MaxHealth:
                SetMaxHealth(Mathf.Max(1, Mathf.RoundToInt(value)));
                break;
            case PlayerStatType.MaxStamina:
                SetMaxStamina(Mathf.Max(0f, value));
                break;
            case PlayerStatType.DashStaminaCost:
                SetRuntimeFloat(networkDashStaminaCost, ref offlineDashStaminaCost, Mathf.Max(0f, value));
                break;
            case PlayerStatType.StaminaRecovery:
                SetRuntimeFloat(networkStaminaRecovery, ref offlineStaminaRecovery, Mathf.Max(0f, value));
                break;
            case PlayerStatType.BaseAttack:
                SetRuntimeFloat(networkDamage, ref offlineDamage, Mathf.Max(0f, value));
                break;
            case PlayerStatType.AttackRange:
                SetRuntimeFloat(networkWeaponRange, ref offlineWeaponRange, Mathf.Max(0f, value));
                break;
            case PlayerStatType.AttackInterval:
                SetRuntimeFloat(networkAttackCooldown, ref offlineAttackCooldown, Mathf.Max(0.01f, value));
                break;
            case PlayerStatType.KnockbackForce:
                SetRuntimeFloat(networkKnockbackForce, ref offlineKnockbackForce, Mathf.Max(0f, value));
                break;
            case PlayerStatType.MoveSpeed:
                SetRuntimeFloat(networkMoveSpeed, ref offlineMoveSpeed, Mathf.Max(0f, value));
                break;
            case PlayerStatType.CritRate:
                SetRuntimeFloat(networkCritRate, ref offlineCritRate, Mathf.Clamp01(value));
                break;
            case PlayerStatType.CritDamage:
                SetRuntimeFloat(networkCritDamage, ref offlineCritDamage, Mathf.Max(1f, value));
                break;
            case PlayerStatType.InvincibleTime:
                SetRuntimeFloat(networkInvincibleTime, ref offlineInvincibleTime, Mathf.Max(0f, value));
                break;
            case PlayerStatType.FlashSpeed:
                SetRuntimeFloat(networkFlashSpeed, ref offlineFlashSpeed, Mathf.Max(0.01f, value));
                break;
            case PlayerStatType.MaxScarlet:
                SetMaxScarlet(Mathf.Max(0f, value));
                break;
        }
    }

    private void SetMaxHealth(int value)
    {
        int previous = MaxHealth;
        int delta = value - previous;
        if (UseNetworkValues)
        {
            networkMaxHealth.Value = value;
            networkHealth.Value = Mathf.Clamp(networkHealth.Value + Mathf.Max(0, delta), 0, value);
        }
        else
        {
            offlineMaxHealth = value;
            offlineHealth = Mathf.Clamp(offlineHealth + Mathf.Max(0, delta), 0, value);
            HealthChanged?.Invoke(offlineHealth, offlineMaxHealth);
        }
    }

    private void SetMaxStamina(float value)
    {
        float delta = value - MaxStamina;
        SetRuntimeFloat(networkMaxStamina, ref offlineMaxStamina, value);
        SetStamina(Mathf.Min(value, CurrentStamina + Mathf.Max(0f, delta)));
    }

    private void SetMaxScarlet(float value)
    {
        SetRuntimeFloat(networkMaxScarlet, ref offlineMaxScarlet, value);
        SetScarlet(Mathf.Min(CurrentScarlet, value));
    }

    private void SetHealth(int value)
    {
        value = Mathf.Clamp(value, 0, MaxHealth);
        if (UseNetworkValues) networkHealth.Value = value;
        else if (offlineHealth != value)
        {
            offlineHealth = value;
            HealthChanged?.Invoke(offlineHealth, offlineMaxHealth);
        }
    }

    private void SetStamina(float value)
    {
        value = Mathf.Clamp(value, 0f, MaxStamina);
        if (UseNetworkValues) networkStamina.Value = value;
        else if (!Mathf.Approximately(offlineStamina, value))
        {
            offlineStamina = value;
            StaminaChanged?.Invoke(offlineStamina, offlineMaxStamina);
        }
    }

    private void SetScarlet(float value)
    {
        value = Mathf.Clamp(value, 0f, MaxScarlet);
        if (UseNetworkValues) networkScarlet.Value = value;
        else if (!Mathf.Approximately(offlineScarlet, value))
        {
            offlineScarlet = value;
            ScarletChanged?.Invoke(offlineScarlet, offlineMaxScarlet);
        }
    }

    private void SetAlive(bool value)
    {
        if (UseNetworkValues) networkAlive.Value = value;
        else if (offlineAlive != value)
        {
            offlineAlive = value;
            ApplyAlivePresentation(value);
            AliveChanged?.Invoke(value);
        }
    }

    private float Read(NetworkVariable<float> networkValue, float offlineValue) =>
        UseNetworkValues ? networkValue.Value : offlineValue;

    private void SetRuntimeFloat(
        NetworkVariable<float> networkValue,
        ref float offlineValue,
        float value)
    {
        if (UseNetworkValues) networkValue.Value = value;
        else offlineValue = value;
    }

    private void SubscribeNetworkEvents()
    {
        networkHealth.OnValueChanged += HandleNetworkHealthChanged;
        networkMaxHealth.OnValueChanged += HandleNetworkMaxHealthChanged;
        networkStamina.OnValueChanged += HandleNetworkStaminaChanged;
        networkMaxStamina.OnValueChanged += HandleNetworkMaxStaminaChanged;
        networkScarlet.OnValueChanged += HandleNetworkScarletChanged;
        networkMaxScarlet.OnValueChanged += HandleNetworkMaxScarletChanged;
        networkAlive.OnValueChanged += HandleNetworkAliveChanged;
        networkDashStaminaCost.OnValueChanged += HandleDashCostChanged;
        networkStaminaRecovery.OnValueChanged += HandleStaminaRecoveryChanged;
        networkDamage.OnValueChanged += HandleDamageChanged;
        networkWeaponRange.OnValueChanged += HandleAttackRangeChanged;
        networkMoveSpeed.OnValueChanged += HandleMoveSpeedChanged;
        networkAttackCooldown.OnValueChanged += HandleAttackIntervalChanged;
        networkKnockbackForce.OnValueChanged += HandleKnockbackChanged;
        networkCritRate.OnValueChanged += HandleCritRateChanged;
        networkCritDamage.OnValueChanged += HandleCritDamageChanged;
        networkInvincibleTime.OnValueChanged += HandleInvincibleTimeChanged;
        networkFlashSpeed.OnValueChanged += HandleFlashSpeedChanged;
    }

    private void UnsubscribeNetworkEvents()
    {
        networkHealth.OnValueChanged -= HandleNetworkHealthChanged;
        networkMaxHealth.OnValueChanged -= HandleNetworkMaxHealthChanged;
        networkStamina.OnValueChanged -= HandleNetworkStaminaChanged;
        networkMaxStamina.OnValueChanged -= HandleNetworkMaxStaminaChanged;
        networkScarlet.OnValueChanged -= HandleNetworkScarletChanged;
        networkMaxScarlet.OnValueChanged -= HandleNetworkMaxScarletChanged;
        networkAlive.OnValueChanged -= HandleNetworkAliveChanged;
        networkDashStaminaCost.OnValueChanged -= HandleDashCostChanged;
        networkStaminaRecovery.OnValueChanged -= HandleStaminaRecoveryChanged;
        networkDamage.OnValueChanged -= HandleDamageChanged;
        networkWeaponRange.OnValueChanged -= HandleAttackRangeChanged;
        networkMoveSpeed.OnValueChanged -= HandleMoveSpeedChanged;
        networkAttackCooldown.OnValueChanged -= HandleAttackIntervalChanged;
        networkKnockbackForce.OnValueChanged -= HandleKnockbackChanged;
        networkCritRate.OnValueChanged -= HandleCritRateChanged;
        networkCritDamage.OnValueChanged -= HandleCritDamageChanged;
        networkInvincibleTime.OnValueChanged -= HandleInvincibleTimeChanged;
        networkFlashSpeed.OnValueChanged -= HandleFlashSpeedChanged;
    }

    private void HandleNetworkHealthChanged(int previous, int current) =>
        HealthChanged?.Invoke(current, MaxHealth);
    private void HandleNetworkMaxHealthChanged(int previous, int current)
    {
        HealthChanged?.Invoke(CurrentHealth, current);
        RunStatChanged?.Invoke(PlayerStatType.MaxHealth);
    }
    private void HandleNetworkStaminaChanged(float previous, float current) =>
        StaminaChanged?.Invoke(current, MaxStamina);
    private void HandleNetworkMaxStaminaChanged(float previous, float current)
    {
        StaminaChanged?.Invoke(CurrentStamina, current);
        RunStatChanged?.Invoke(PlayerStatType.MaxStamina);
    }
    private void HandleNetworkScarletChanged(float previous, float current) =>
        ScarletChanged?.Invoke(current, MaxScarlet);
    private void HandleNetworkMaxScarletChanged(float previous, float current)
    {
        ScarletChanged?.Invoke(CurrentScarlet, current);
        RunStatChanged?.Invoke(PlayerStatType.MaxScarlet);
    }
    private void HandleNetworkAliveChanged(bool previous, bool current)
    {
        ApplyAlivePresentation(current);
        AliveChanged?.Invoke(current);
    }

    private void HandleDashCostChanged(float p, float c) => Notify(PlayerStatType.DashStaminaCost);
    private void HandleStaminaRecoveryChanged(float p, float c) => Notify(PlayerStatType.StaminaRecovery);
    private void HandleDamageChanged(float p, float c) => Notify(PlayerStatType.BaseAttack);
    private void HandleAttackRangeChanged(float p, float c) => Notify(PlayerStatType.AttackRange);
    private void HandleMoveSpeedChanged(float p, float c) => Notify(PlayerStatType.MoveSpeed);
    private void HandleAttackIntervalChanged(float p, float c) => Notify(PlayerStatType.AttackInterval);
    private void HandleKnockbackChanged(float p, float c) => Notify(PlayerStatType.KnockbackForce);
    private void HandleCritRateChanged(float p, float c) => Notify(PlayerStatType.CritRate);
    private void HandleCritDamageChanged(float p, float c) => Notify(PlayerStatType.CritDamage);
    private void HandleInvincibleTimeChanged(float p, float c) => Notify(PlayerStatType.InvincibleTime);
    private void HandleFlashSpeedChanged(float p, float c) => Notify(PlayerStatType.FlashSpeed);
    private void Notify(PlayerStatType stat) => RunStatChanged?.Invoke(stat);

    private void PublishAll()
    {
        ApplyAlivePresentation(IsAlive);
        HealthChanged?.Invoke(CurrentHealth, MaxHealth);
        StaminaChanged?.Invoke(CurrentStamina, MaxStamina);
        ScarletChanged?.Invoke(CurrentScarlet, MaxScarlet);
        AliveChanged?.Invoke(IsAlive);
    }

    private void ApplyAlivePresentation(bool alive)
    {
        foreach (Collider playerCollider in GetComponentsInChildren<Collider>(true))
            playerCollider.enabled = alive;

        if (!hideVisualsWhenDead) return;
        foreach (Renderer playerRenderer in GetComponentsInChildren<Renderer>(true))
            playerRenderer.enabled = alive;
    }
}
