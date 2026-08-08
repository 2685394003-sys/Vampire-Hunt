using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-owned runtime state for one player. StatsManager remains shared game
/// balance configuration; mutable health/stamina/progression lives here.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class PlayerNetworkState : NetworkBehaviour
{
    [SerializeField] private StatsManager balance;
    [SerializeField] private bool hideVisualsWhenDead = true;

    private readonly NetworkVariable<int> networkMaxHealth = new(
        1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> networkHealth = new(
        1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> networkMaxStamina = new(
        100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> networkStamina = new(
        100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> networkDamage = new(
        1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> networkWeaponRange = new(
        1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> networkMoveSpeed = new(
        1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<float> networkAttackCooldown = new(
        0.35f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> networkAlive = new(
        true, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> networkInitialized = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private int offlineMaxHealth;
    private int offlineHealth;
    private float offlineMaxStamina;
    private float offlineStamina;
    private int offlineDamage;
    private float offlineWeaponRange;
    private float offlineMoveSpeed;
    private float offlineAttackCooldown;
    private bool offlineAlive;
    private bool staminaRecoveryPaused;

    public event Action<int, int> HealthChanged;
    public event Action<float, float> StaminaChanged;
    public event Action<bool> AliveChanged;

    public StatsManager Balance => ResolveBalance();
    public int MaxHealth => UseNetworkValues ? networkMaxHealth.Value : offlineMaxHealth;
    public int CurrentHealth => UseNetworkValues ? networkHealth.Value : offlineHealth;
    public float MaxStamina => UseNetworkValues ? networkMaxStamina.Value : offlineMaxStamina;
    public float CurrentStamina => UseNetworkValues ? networkStamina.Value : offlineStamina;
    public int Damage => UseNetworkValues ? networkDamage.Value : offlineDamage;
    public float WeaponRange => UseNetworkValues ? networkWeaponRange.Value : offlineWeaponRange;
    public float MoveSpeed => UseNetworkValues ? networkMoveSpeed.Value : offlineMoveSpeed;
    public float AttackCooldown => UseNetworkValues ? networkAttackCooldown.Value : offlineAttackCooldown;
    public bool IsAlive => UseNetworkValues ? networkAlive.Value : offlineAlive;

    public float KnockbackForce => ResolveBalance() != null ? ResolveBalance().knockbackForce : 0f;
    public float KnockbackTime => ResolveBalance() != null ? ResolveBalance().knockbackTime : 0f;
    public float StunTime => ResolveBalance() != null ? ResolveBalance().stunTime : 0f;
    public LayerMask EnemyLayer => ResolveBalance() != null ? ResolveBalance().enemyLayer : 0;

    private bool UseNetworkValues => NetworkAuthority.IsNetworkActive && IsSpawned;

    public static PlayerNetworkState EnsureForMigration(GameObject playerObject)
    {
        if (playerObject == null)
        {
            return null;
        }

        PlayerNetworkState existing = playerObject.GetComponent<PlayerNetworkState>();
        if (existing != null)
        {
            return existing;
        }

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
        ResolveBalance();
        InitializeOfflineState();
    }

    private void OnEnable()
    {
        NetworkPlayerRegistry.Register(this);
    }

    private void OnDisable()
    {
        NetworkPlayerRegistry.Unregister(this);
    }

    public override void OnNetworkSpawn()
    {
        NetworkPlayerRegistry.Register(this);
        networkHealth.OnValueChanged += HandleNetworkHealthChanged;
        networkMaxHealth.OnValueChanged += HandleNetworkMaxHealthChanged;
        networkStamina.OnValueChanged += HandleNetworkStaminaChanged;
        networkMaxStamina.OnValueChanged += HandleNetworkMaxStaminaChanged;
        networkAlive.OnValueChanged += HandleNetworkAliveChanged;

        if (IsServer && !networkInitialized.Value)
        {
            InitializeNetworkState();
        }

        PublishAll();
    }

    public override void OnNetworkDespawn()
    {
        networkHealth.OnValueChanged -= HandleNetworkHealthChanged;
        networkMaxHealth.OnValueChanged -= HandleNetworkMaxHealthChanged;
        networkStamina.OnValueChanged -= HandleNetworkStaminaChanged;
        networkMaxStamina.OnValueChanged -= HandleNetworkMaxStaminaChanged;
        networkAlive.OnValueChanged -= HandleNetworkAliveChanged;
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

        StatsManager config = ResolveBalance();
        float recovery = config != null ? config.staminaRecoverSpeed : 0f;
        SetStamina(Mathf.Min(MaxStamina, CurrentStamina + recovery * Time.deltaTime));
    }

    public bool TryConsumeStamina(float amount)
    {
        if (!NetworkAuthority.IsServerOrOffline(this) || amount < 0f || CurrentStamina < amount)
        {
            return false;
        }

        SetStamina(CurrentStamina - amount);
        return true;
    }

    public void RestoreStamina(float amount)
    {
        if (NetworkAuthority.IsServerOrOffline(this) && amount > 0f)
        {
            SetStamina(CurrentStamina + amount);
        }
    }

    public void SetStaminaRecoveryPaused(bool value)
    {
        if (NetworkAuthority.IsServerOrOffline(this))
        {
            staminaRecoveryPaused = value;
        }
    }

    public bool ApplyDamage(int amount)
    {
        if (!NetworkAuthority.IsServerOrOffline(this) || amount <= 0 || !IsAlive)
        {
            return false;
        }

        SetHealth(Mathf.Max(0, CurrentHealth - amount));
        if (CurrentHealth <= 0)
        {
            SetAlive(false);
        }
        return true;
    }

    public void HealToFull()
    {
        if (!NetworkAuthority.IsServerOrOffline(this))
        {
            return;
        }

        SetHealth(MaxHealth);
        SetAlive(true);
    }

    public void ForceDeath()
    {
        if (!NetworkAuthority.IsServerOrOffline(this))
        {
            return;
        }

        SetHealth(0);
        SetAlive(false);
    }

    public void ResetForNewRun()
    {
        if (!NetworkAuthority.IsServerOrOffline(this))
        {
            return;
        }

        if (UseNetworkValues)
        {
            InitializeNetworkState();
        }
        else
        {
            InitializeOfflineState();
            PublishAll();
        }
    }

    /// <summary>Applies permanent run upgrades on the server for this player only.</summary>
    public void ApplyUpgrade(int damageBonus, float moveSpeedBonus, int maxHealthBonus)
    {
        if (!NetworkAuthority.IsServerOrOffline(this)) return;

        damageBonus = Mathf.Max(0, damageBonus);
        moveSpeedBonus = Mathf.Max(0f, moveSpeedBonus);
        maxHealthBonus = Mathf.Max(0, maxHealthBonus);

        if (UseNetworkValues)
        {
            networkDamage.Value += damageBonus;
            networkMoveSpeed.Value += moveSpeedBonus;
            if (maxHealthBonus > 0)
            {
                networkMaxHealth.Value += maxHealthBonus;
                networkHealth.Value = Mathf.Min(
                    networkMaxHealth.Value,
                    networkHealth.Value + maxHealthBonus);
            }
            return;
        }

        offlineDamage += damageBonus;
        offlineMoveSpeed += moveSpeedBonus;
        if (maxHealthBonus > 0)
        {
            offlineMaxHealth += maxHealthBonus;
            offlineHealth = Mathf.Min(offlineMaxHealth, offlineHealth + maxHealthBonus);
            HealthChanged?.Invoke(offlineHealth, offlineMaxHealth);
        }
    }

    private StatsManager ResolveBalance()
    {
        if (balance == null)
        {
            balance = StatsManager.Instance != null
                ? StatsManager.Instance
                : FindFirstObjectByType<StatsManager>();
        }
        return balance;
    }

    private void InitializeOfflineState()
    {
        StatsManager config = ResolveBalance();
        offlineMaxHealth = Mathf.Max(1, config != null ? config.maxHealth : 1);
        offlineHealth = offlineMaxHealth;
        offlineMaxStamina = Mathf.Max(0f, config != null ? config.maxStamina : 100f);
        offlineStamina = offlineMaxStamina;
        offlineDamage = Mathf.Max(0, config != null ? config.damage : 1);
        offlineWeaponRange = Mathf.Max(0f, config != null ? config.weaponRange : 1f);
        offlineMoveSpeed = Mathf.Max(0f, config != null ? config.speed : 1f);
        offlineAttackCooldown = Mathf.Max(0.01f, config != null ? config.cooldown : 0.35f);
        offlineAlive = true;
    }

    private void InitializeNetworkState()
    {
        StatsManager config = ResolveBalance();
        networkMaxHealth.Value = Mathf.Max(1, config != null ? config.maxHealth : 1);
        networkHealth.Value = networkMaxHealth.Value;
        networkMaxStamina.Value = Mathf.Max(0f, config != null ? config.maxStamina : 100f);
        networkStamina.Value = networkMaxStamina.Value;
        networkDamage.Value = Mathf.Max(0, config != null ? config.damage : 1);
        networkWeaponRange.Value = Mathf.Max(0f, config != null ? config.weaponRange : 1f);
        networkMoveSpeed.Value = Mathf.Max(0f, config != null ? config.speed : 1f);
        networkAttackCooldown.Value = Mathf.Max(0.01f, config != null ? config.cooldown : 0.35f);
        networkAlive.Value = true;
        networkInitialized.Value = true;
    }

    private void SetHealth(int value)
    {
        value = Mathf.Clamp(value, 0, MaxHealth);
        if (UseNetworkValues)
        {
            networkHealth.Value = value;
        }
        else if (offlineHealth != value)
        {
            offlineHealth = value;
            HealthChanged?.Invoke(offlineHealth, offlineMaxHealth);
        }
    }

    private void SetStamina(float value)
    {
        value = Mathf.Clamp(value, 0f, MaxStamina);
        if (UseNetworkValues)
        {
            networkStamina.Value = value;
        }
        else if (!Mathf.Approximately(offlineStamina, value))
        {
            offlineStamina = value;
            StaminaChanged?.Invoke(offlineStamina, offlineMaxStamina);
        }
    }

    private void SetAlive(bool value)
    {
        if (UseNetworkValues)
        {
            networkAlive.Value = value;
        }
        else if (offlineAlive != value)
        {
            offlineAlive = value;
            ApplyAlivePresentation(value);
            AliveChanged?.Invoke(value);
        }
    }

    private void HandleNetworkHealthChanged(int previous, int current) =>
        HealthChanged?.Invoke(current, MaxHealth);

    private void HandleNetworkMaxHealthChanged(int previous, int current) =>
        HealthChanged?.Invoke(CurrentHealth, current);

    private void HandleNetworkStaminaChanged(float previous, float current) =>
        StaminaChanged?.Invoke(current, MaxStamina);

    private void HandleNetworkMaxStaminaChanged(float previous, float current) =>
        StaminaChanged?.Invoke(CurrentStamina, current);

    private void HandleNetworkAliveChanged(bool previous, bool current)
    {
        ApplyAlivePresentation(current);
        AliveChanged?.Invoke(current);
    }

    private void PublishAll()
    {
        ApplyAlivePresentation(IsAlive);
        HealthChanged?.Invoke(CurrentHealth, MaxHealth);
        StaminaChanged?.Invoke(CurrentStamina, MaxStamina);
        AliveChanged?.Invoke(IsAlive);
    }

    private void ApplyAlivePresentation(bool alive)
    {
        foreach (Collider playerCollider in GetComponentsInChildren<Collider>(true))
        {
            playerCollider.enabled = alive;
        }

        if (!hideVisualsWhenDead)
        {
            return;
        }

        foreach (Renderer playerRenderer in GetComponentsInChildren<Renderer>(true))
        {
            playerRenderer.enabled = alive;
        }
    }
}
