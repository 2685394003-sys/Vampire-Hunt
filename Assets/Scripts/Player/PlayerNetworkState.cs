using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-owned per-player run state. PlayerStatsConfig is the immutable baseline;
/// runtime modifiers are aggregated on the server and only final values replicate.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class PlayerNetworkState : NetworkBehaviour, IPlayerRunStats, IGameplayAbilitySystemHost
{
    public const float BloodPactScarletCost = 100f;

    private static readonly List<PlayerNetworkState> ScarletShareRecipients = new();

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
    private readonly NetworkVariable<float> networkDashSpeedMultiplier = ServerVariable(2f);
    private readonly NetworkVariable<float> networkDashDuration = ServerVariable(0.15f);
    private readonly NetworkVariable<float> networkAttackCooldown = ServerVariable(1f);
    private readonly NetworkVariable<float> networkKnockbackForce = ServerVariable(5f);
    private readonly NetworkVariable<float> networkCritRate = ServerVariable(0.05f);
    private readonly NetworkVariable<float> networkCritDamage = ServerVariable(2f);
    private readonly NetworkVariable<float> networkInvincibleTime = ServerVariable(0.8f);
    private readonly NetworkVariable<float> networkFlashSpeed = ServerVariable(10f);
    private readonly NetworkVariable<float> networkMaxScarlet = ServerVariable(100f);
    private readonly NetworkVariable<float> networkScarlet = ServerVariable(0f);
    private readonly NetworkVariable<int> networkCoins = ServerVariable(0);
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
    private float offlineDashSpeedMultiplier;
    private float offlineDashDuration;
    private float offlineAttackCooldown;
    private float offlineKnockbackForce;
    private float offlineCritRate;
    private float offlineCritDamage;
    private float offlineInvincibleTime;
    private float offlineFlashSpeed;
    private float offlineMaxScarlet;
    private float offlineScarlet;
    private int offlineCoins;
    private bool offlineAlive;
    private bool staminaRecoveryPaused;
    private GameplayAbilitySystem abilitySystem;
    private NetworkList<NetworkBloodPactState> networkBloodPacts;
    private readonly List<NetworkBloodPactState> offlineBloodPacts = new();
    private readonly PlayerStatModifierCollection runModifiers = new();
    private readonly HashSet<PlayerStatType> changedStatsScratch = new();
    private readonly HashSet<string> selectedBloodPacts = new(StringComparer.Ordinal);
    private int generatedModifierSequence;

    public event Action<int, int> HealthChanged;
    public event Action<float, float> StaminaChanged;
    public event Action<float, float> ScarletChanged;
    public event Action<int> CoinsChanged;
    public event Action<bool> AliveChanged;
    public event Action<PlayerStatType> RunStatChanged;
    public event Action BloodPactsChanged;
    public event Action<GameplayCueEvent> GameplayCueRequested;

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
    public float DashSpeedMultiplier => Read(networkDashSpeedMultiplier, offlineDashSpeedMultiplier);
    public float DashDuration => Read(networkDashDuration, offlineDashDuration);
    public float AttackCooldown => Read(networkAttackCooldown, offlineAttackCooldown);
    public float KnockbackForce => Read(networkKnockbackForce, offlineKnockbackForce);
    public float CritRate => Read(networkCritRate, offlineCritRate);
    public float CritDamage => Read(networkCritDamage, offlineCritDamage);
    public float InvincibleTime => Read(networkInvincibleTime, offlineInvincibleTime);
    public float FlashSpeed => Read(networkFlashSpeed, offlineFlashSpeed);
    public float MaxScarlet => Read(networkMaxScarlet, offlineMaxScarlet);
    public float CurrentScarlet => Read(networkScarlet, offlineScarlet);
    public int CurrentCoins => UseNetworkValues ? networkCoins.Value : offlineCoins;
    public bool IsAlive => UseNetworkValues ? networkAlive.Value : offlineAlive;
    public GameplayAbilitySystem AbilitySystem => abilitySystem;
    public string GameplayOwnerId => PlayerId;
    public float GameplayHealthRatio => MaxHealth > 0
        ? Mathf.Clamp01((float)CurrentHealth / MaxHealth)
        : 0f;
    public int ActiveBloodPactCount => UseNetworkValues
        ? networkBloodPacts?.Count ?? 0
        : offlineBloodPacts.Count;

    // Supplemental combat settings stay baseline-only until design adds them
    // to the progression sheet.
    public float KnockbackTime => BaseStats != null ? BaseStats.KnockbackDuration : 0f;
    public float StunTime => BaseStats != null ? BaseStats.StunDuration : 0f;
    public float AttackConeAngle => BaseStats != null ? BaseStats.AttackConeAngle : 110f;
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
        networkBloodPacts = new NetworkList<NetworkBloodPactState>(
            null,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        abilitySystem = new GameplayAbilitySystem(this);
        ResolveBaseStats();
        InitializeOfflineState();
    }

    private void OnEnable() => NetworkPlayerRegistry.Register(this);
    private void OnDisable() => NetworkPlayerRegistry.Unregister(this);

    public override void OnNetworkSpawn()
    {
        NetworkPlayerRegistry.Register(this);
        SubscribeNetworkEvents();
        networkBloodPacts.OnListChanged += HandleBloodPactListChanged;

        if (IsServer && !networkInitialized.Value) InitializeNetworkState();
        PublishAll();
        BloodPactsChanged?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        networkBloodPacts.OnListChanged -= HandleBloodPactListChanged;
        UnsubscribeNetworkEvents();
        NetworkPlayerRegistry.Unregister(this);
    }

    private void Update()
    {
        if (!NetworkAuthority.IsServerOrOffline(this)) return;

        abilitySystem?.Tick(Time.deltaTime);

        changedStatsScratch.Clear();
        if (runModifiers.Tick(Time.deltaTime, changedStatsScratch) > 0)
        {
            foreach (PlayerStatType stat in changedStatsScratch) RecalculateStat(stat);
        }

        if (!IsAlive ||
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
        if (!NetworkAuthority.IsServerOrOffline(this) ||
            amount <= 0f ||
            float.IsNaN(amount) ||
            float.IsInfinity(amount))
        {
            return;
        }

        if (!NetworkAuthority.IsNetworkActive)
        {
            AddScarletDirect(amount);
            return;
        }

        NetworkPlayerRegistry.GetPlayers(ScarletShareRecipients);
        if (ScarletShareRecipients.Count == 0)
        {
            AddScarletDirect(amount);
            return;
        }

        float share = amount / ScarletShareRecipients.Count;
        foreach (PlayerNetworkState recipient in ScarletShareRecipients)
        {
            if (recipient != null && NetworkAuthority.IsServerOrOffline(recipient))
            {
                recipient.AddScarletDirect(share);
            }
        }
    }

    public void AddCoins(int amount)
    {
        if (!NetworkAuthority.IsServerOrOffline(this) || amount <= 0) return;

        long nextValue = (long)CurrentCoins + amount;
        SetCoins((int)Math.Min(int.MaxValue, nextValue));
    }

    public int RestoreHealth(int amount)
    {
        if (!NetworkAuthority.IsServerOrOffline(this) || amount <= 0 || !IsAlive)
            return 0;

        int previous = CurrentHealth;
        SetHealth(Mathf.Min(MaxHealth, previous + amount));
        return CurrentHealth - previous;
    }

    public bool HasBloodPact(string pactId) => GetBloodPactStacks(pactId) > 0;

    public int GetBloodPactStacks(string pactId)
    {
        if (string.IsNullOrWhiteSpace(pactId)) return 0;
        if (UseNetworkValues)
        {
            for (int index = 0; index < networkBloodPacts.Count; index++)
                if (string.Equals(
                        networkBloodPacts[index].PactId.ToString(),
                        pactId,
                        StringComparison.Ordinal))
                    return networkBloodPacts[index].Stacks;
            return 0;
        }

        for (int index = 0; index < offlineBloodPacts.Count; index++)
            if (string.Equals(
                    offlineBloodPacts[index].PactId.ToString(),
                    pactId,
                    StringComparison.Ordinal))
                return offlineBloodPacts[index].Stacks;
        return 0;
    }

    public bool TrySpendCoins(int amount)
    {
        if (!NetworkAuthority.IsServerOrOffline(this) ||
            amount < 0 ||
            CurrentCoins < amount)
        {
            return false;
        }

        SetCoins(CurrentCoins - amount);
        return true;
    }

    /// <summary>
    /// Requests a server-authoritative blood-pact purchase. The server owns the
    /// fixed cost and validates the pact database entry and duplicate state.
    /// </summary>
    public bool RequestBloodPactSelection(string pactId)
    {
        if (string.IsNullOrWhiteSpace(pactId) || pactId.Length > 64)
        {
            return false;
        }

        if (!NetworkAuthority.IsNetworkActive)
        {
            return ServerTrySelectBloodPact(pactId.Trim());
        }

        if (!IsSpawned || !IsOwner)
        {
            return false;
        }

        RequestBloodPactSelectionRpc(new FixedString64Bytes(pactId.Trim()));
        return true;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestBloodPactSelectionRpc(FixedString64Bytes pactId)
    {
        ServerTrySelectBloodPact(pactId.ToString());
    }

    private bool ServerTrySelectBloodPact(string pactId)
    {
        if (!NetworkAuthority.IsServerOrOffline(this) ||
            CurrentScarlet + 0.0001f < BloodPactScarletCost)
        {
            return false;
        }

        BloodPactConfig database = BloodPactConfig.LoadDefault();
        if (database == null ||
            !database.TryGet(pactId, out BloodPactDefinition pact) ||
            !pact.IsPlayerPact ||
            !pact.IsRuntimeImplemented ||
            (!pact.IsRepeatable && selectedBloodPacts.Contains(pactId)) ||
            !TryConsumeScarlet(BloodPactScarletCost))
        {
            return false;
        }

        if (!pact.ApplyEffects(this))
        {
            AddScarletDirect(BloodPactScarletCost);
            return false;
        }

        if (!pact.IsRepeatable)
        {
            selectedBloodPacts.Add(pactId);
        }
        AddOrStackBloodPactSnapshot(pactId);
        return true;
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

    public void ReportAttackHit(
        IGameplayAbilitySystemHost target,
        float damageDealt,
        bool wasCritical,
        Vector3 position,
        int combatTextTargetKey = 0)
    {
        if (!NetworkAuthority.IsServerOrOffline(this) || damageDealt <= 0f) return;

        GameplayEventData hit = new(
            GameplayEventType.AttackHit,
            this,
            target,
            damageDealt,
            wasCritical,
            position);
        abilitySystem.SendEvent(in hit);

        BroadcastDamageText(
            Mathf.Max(1, Mathf.RoundToInt(damageDealt)),
            wasCritical,
            position,
            combatTextTargetKey);

        if (!wasCritical) return;
        GameplayEventData critical = new(
            GameplayEventType.CriticalHit,
            this,
            target,
            damageDealt,
            true,
            position);
        abilitySystem.SendEvent(in critical);
    }

    /// <summary>
    /// Broadcasts server-confirmed effect damage without producing AttackHit or
    /// CriticalHit events. Periodic effects must use this path to avoid retriggering
    /// on-hit abilities from their own damage ticks.
    /// </summary>
    public void ReportGameplayEffectDamage(
        int damageDealt,
        Vector3 position,
        int combatTextTargetKey = 0)
    {
        if (!NetworkAuthority.IsServerOrOffline(this) || damageDealt <= 0) return;
        BroadcastDamageText(
            damageDealt,
            false,
            position,
            combatTextTargetKey);
    }

    private void BroadcastDamageText(
        int damage,
        bool wasCritical,
        Vector3 position,
        int targetKey)
    {
        ulong sourceKey = UseNetworkValues
            ? OwnerClientId
            : unchecked((ulong)(uint)GetEntityId().GetHashCode());

        if (UseNetworkValues)
        {
            ShowDamageTextRpc(damage, wasCritical, position, targetKey, sourceKey);
            return;
        }

        CombatTextService.ShowDamage(damage, wasCritical, position, targetKey, sourceKey);
    }

    [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Unreliable)]
    private void ShowDamageTextRpc(
        int damage,
        bool wasCritical,
        Vector3 position,
        int targetKey,
        ulong sourceKey)
    {
        CombatTextService.ShowDamage(damage, wasCritical, position, targetKey, sourceKey);
    }

    public void ReportEnemyKilled(Vector3 position)
    {
        if (!NetworkAuthority.IsServerOrOffline(this)) return;
        GameplayEventData killed = new(
            GameplayEventType.EnemyKilled,
            this,
            null,
            1f,
            false,
            position);
        abilitySystem.SendEvent(in killed);
    }

    public void ForceDeath()
    {
        if (!NetworkAuthority.IsServerOrOffline(this)) return;
        SetHealth(0);
        SetAlive(false);
    }

    /// <summary>
    /// Rolls one server-authoritative damage value for one hit target. Multi-target
    /// attacks must call this once per unique target so each target rolls crit alone.
    /// </summary>
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

        abilitySystem?.Clear();
        runModifiers.Clear();
        selectedBloodPacts.Clear();
        ClearBloodPactSnapshots();
        generatedModifierSequence = 0;
        staminaRecoveryPaused = false;

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
            !Enum.IsDefined(typeof(PlayerStatType), upgrade.stat) ||
            !Enum.IsDefined(typeof(PlayerStatOperation), upgrade.operation) ||
            float.IsNaN(upgrade.value) ||
            float.IsInfinity(upgrade.value) ||
            (upgrade.operation == PlayerStatOperation.Multiply && upgrade.value < 0f))
        {
            return false;
        }

        string modifierId =
            $"legacy:{++generatedModifierSequence}:{(int)upgrade.stat}";
        PlayerModifierOperation operation = upgrade.operation == PlayerStatOperation.Add
            ? PlayerModifierOperation.Flat
            : PlayerModifierOperation.Multiplicative;
        return AddStatModifier(new PlayerStatModifier(
            modifierId,
            "legacy_upgrade",
            upgrade.stat,
            operation,
            upgrade.value));
    }

    public bool AddStatModifier(PlayerStatModifier modifier)
    {
        if (!NetworkAuthority.IsServerOrOffline(this) ||
            !runModifiers.AddOrStack(modifier, out PlayerStatType changedStat))
        {
            return false;
        }

        RecalculateStat(changedStat);
        return true;
    }

    public bool AddGameplayModifier(
        string modifierId,
        string sourceId,
        GameplayAttributeType stat,
        PlayerModifierOperation operation,
        float value)
    {
        if (!Enum.IsDefined(typeof(PlayerStatType), (int)stat)) return false;
        return AddStatModifier(new PlayerStatModifier(
            modifierId,
            sourceId,
            (PlayerStatType)(int)stat,
            operation,
            value));
    }

    public bool RemoveGameplayModifier(string modifierId) =>
        RemoveStatModifier(modifierId);

    public int DamageGameplay(int amount, IGameplayAbilitySystemHost source)
    {
        int previous = CurrentHealth;
        return ApplyDamage(amount) ? previous - CurrentHealth : 0;
    }

    public int HealGameplay(int amount) => RestoreHealth(amount);
    public void AddScarletGameplay(float amount) => AddScarlet(amount);
    public void AddCoinsGameplay(int amount) => AddCoins(amount);

    public void EmitGameplayCue(in GameplayCueEvent cueEvent)
    {
        if (string.IsNullOrWhiteSpace(cueEvent.CueTag)) return;
        if (NetworkAuthority.IsNetworkActive && IsSpawned)
        {
            if (!IsServer) return;
            if (cueEvent.CueTag.Length > 60)
            {
                Debug.LogWarning($"[GAS] Cue tag is too long for network transport: {cueEvent.CueTag}", this);
                return;
            }
            GameplayCueRpc(
                new FixedString64Bytes(cueEvent.CueTag),
                (byte)cueEvent.Type,
                cueEvent.Magnitude);
            return;
        }

        GameplayCueRequested?.Invoke(cueEvent);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void GameplayCueRpc(FixedString64Bytes cueTag, byte eventType, float magnitude)
    {
        GameplayCueEvent cue = new(
            cueTag.ToString(),
            (GameplayCueEventType)eventType,
            magnitude);
        GameplayCueRequested?.Invoke(cue);
    }

    public bool RemoveStatModifier(string modifierId)
    {
        if (!NetworkAuthority.IsServerOrOffline(this) ||
            !runModifiers.Remove(modifierId, out PlayerStatType changedStat))
        {
            return false;
        }

        RecalculateStat(changedStat);
        return true;
    }

    public int RemoveStatModifiersFromSource(string sourceId)
    {
        if (!NetworkAuthority.IsServerOrOffline(this)) return 0;

        changedStatsScratch.Clear();
        int removed = runModifiers.RemoveBySource(sourceId, changedStatsScratch);
        foreach (PlayerStatType stat in changedStatsScratch) RecalculateStat(stat);
        return removed;
    }

    public bool TryGetStatBreakdown(
        PlayerStatType stat,
        out PlayerStatBreakdown breakdown)
    {
        if (!NetworkAuthority.IsServerOrOffline(this))
        {
            breakdown = default;
            return false;
        }

        breakdown = CalculateStatBreakdown(stat);
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
        offlineDashSpeedMultiplier = config != null ? config.DashSpeedMultiplier : 2f;
        offlineDashDuration = config != null ? config.DashDuration : 0.15f;
        offlineAttackCooldown = config != null ? config.AttackInterval : 1f;
        offlineKnockbackForce = config != null ? config.KnockbackForce : 5f;
        offlineCritRate = config != null ? config.CritRate : 0.05f;
        offlineCritDamage = config != null ? config.CritDamage : 2f;
        offlineInvincibleTime = config != null ? config.InvincibleTime : 0.8f;
        offlineFlashSpeed = config != null ? config.FlashSpeed : 10f;
        offlineMaxScarlet = config != null ? config.MaxScarlet : 100f;
        offlineScarlet = 0f;
        offlineCoins = 0;
        offlineAlive = true;
        offlineBloodPacts.Clear();
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
        networkDashSpeedMultiplier.Value = config != null ? config.DashSpeedMultiplier : 2f;
        networkDashDuration.Value = config != null ? config.DashDuration : 0.15f;
        networkAttackCooldown.Value = config != null ? config.AttackInterval : 1f;
        networkKnockbackForce.Value = config != null ? config.KnockbackForce : 5f;
        networkCritRate.Value = config != null ? config.CritRate : 0.05f;
        networkCritDamage.Value = config != null ? config.CritDamage : 2f;
        networkInvincibleTime.Value = config != null ? config.InvincibleTime : 0.8f;
        networkFlashSpeed.Value = config != null ? config.FlashSpeed : 10f;
        networkMaxScarlet.Value = config != null ? config.MaxScarlet : 100f;
        networkScarlet.Value = 0f;
        networkCoins.Value = 0;
        networkAlive.Value = true;
        networkBloodPacts.Clear();
        networkInitialized.Value = true;
    }

    private float GetBaseStatValue(PlayerStatType stat)
    {
        PlayerStatsConfig config = ResolveBaseStats();
        if (config == null) return 0f;

        return stat switch
        {
            PlayerStatType.MaxHealth => config.MaxHealth,
            PlayerStatType.MaxStamina => config.MaxStamina,
            PlayerStatType.DashStaminaCost => config.DashStaminaCost,
            PlayerStatType.StaminaRecovery => config.StaminaRecoverSpeed,
            PlayerStatType.BaseAttack => config.BaseAttack,
            PlayerStatType.AttackRange => config.AttackRange,
            PlayerStatType.AttackInterval => config.AttackInterval,
            PlayerStatType.KnockbackForce => config.KnockbackForce,
            PlayerStatType.MoveSpeed => config.MoveSpeed,
            PlayerStatType.DashSpeedMultiplier => config.DashSpeedMultiplier,
            PlayerStatType.DashDuration => config.DashDuration,
            PlayerStatType.CritRate => config.CritRate,
            PlayerStatType.CritDamage => config.CritDamage,
            PlayerStatType.InvincibleTime => config.InvincibleTime,
            PlayerStatType.FlashSpeed => config.FlashSpeed,
            PlayerStatType.MaxScarlet => config.MaxScarlet,
            _ => 0f
        };
    }

    private PlayerStatBreakdown CalculateStatBreakdown(PlayerStatType stat)
    {
        GetStatLimits(stat, out float minimum, out float maximum);
        PlayerStatBreakdown result = runModifiers.Evaluate(
            stat,
            GetBaseStatValue(stat),
            minimum,
            maximum);

        if (stat != PlayerStatType.MaxHealth) return result;
        return new PlayerStatBreakdown(
            result.Stat,
            result.BaseValue,
            result.FlatBonus,
            result.AdditivePercent,
            result.MultiplicativeFactor,
            result.UnclampedValue,
            Mathf.Max(1, Mathf.RoundToInt(result.FinalValue)),
            result.ModifierCount);
    }

    private static void GetStatLimits(
        PlayerStatType stat,
        out float minimum,
        out float maximum)
    {
        maximum = float.PositiveInfinity;
        switch (stat)
        {
            case PlayerStatType.MaxHealth:
            case PlayerStatType.CritDamage:
                minimum = 1f;
                break;
            case PlayerStatType.AttackInterval:
            case PlayerStatType.FlashSpeed:
            case PlayerStatType.DashSpeedMultiplier:
            case PlayerStatType.DashDuration:
                minimum = 0.01f;
                break;
            case PlayerStatType.CritRate:
                minimum = 0f;
                maximum = 1f;
                break;
            default:
                minimum = 0f;
                break;
        }
    }

    private void RecalculateStat(PlayerStatType stat)
    {
        PlayerStatBreakdown breakdown = CalculateStatBreakdown(stat);
        SetStatValue(stat, breakdown.FinalValue);

        // In network play the NetworkVariable callback is the single event source.
        if (!UseNetworkValues) RunStatChanged?.Invoke(stat);
    }

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
            case PlayerStatType.DashSpeedMultiplier:
                SetRuntimeFloat(
                    networkDashSpeedMultiplier,
                    ref offlineDashSpeedMultiplier,
                    Mathf.Max(0.01f, value));
                break;
            case PlayerStatType.DashDuration:
                SetRuntimeFloat(
                    networkDashDuration,
                    ref offlineDashDuration,
                    Mathf.Max(0.01f, value));
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
        int previousHealth = CurrentHealth;
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
        NotifyGameplayHealthChanged(previousHealth);
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
    }

    private void SetHealth(int value)
    {
        value = Mathf.Clamp(value, 0, MaxHealth);
        int previous = CurrentHealth;
        if (previous == value) return;
        if (UseNetworkValues) networkHealth.Value = value;
        else
        {
            offlineHealth = value;
            HealthChanged?.Invoke(offlineHealth, offlineMaxHealth);
        }
        NotifyGameplayHealthChanged(previous);
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
        if (float.IsNaN(value)) return;
        value = float.IsPositiveInfinity(value) ? float.MaxValue : Mathf.Max(0f, value);
        if (UseNetworkValues) networkScarlet.Value = value;
        else if (!Mathf.Approximately(offlineScarlet, value))
        {
            offlineScarlet = value;
            ScarletChanged?.Invoke(offlineScarlet, offlineMaxScarlet);
        }
    }

    private void AddScarletDirect(float amount)
    {
        double nextValue = (double)CurrentScarlet + amount;
        SetScarlet(nextValue >= float.MaxValue ? float.MaxValue : (float)nextValue);
    }

    private void SetCoins(int value)
    {
        value = Mathf.Max(0, value);
        if (UseNetworkValues) networkCoins.Value = value;
        else if (offlineCoins != value)
        {
            offlineCoins = value;
            CoinsChanged?.Invoke(offlineCoins);
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

    private void AddOrStackBloodPactSnapshot(string pactId)
    {
        if (string.IsNullOrWhiteSpace(pactId)) return;

        if (UseNetworkValues)
        {
            for (int index = 0; index < networkBloodPacts.Count; index++)
            {
                NetworkBloodPactState state = networkBloodPacts[index];
                if (!string.Equals(state.PactId.ToString(), pactId, StringComparison.Ordinal))
                    continue;
                state.Stacks = (ushort)Math.Min(ushort.MaxValue, state.Stacks + 1);
                networkBloodPacts[index] = state;
                return;
            }
            networkBloodPacts.Add(new NetworkBloodPactState(pactId));
            return;
        }

        for (int index = 0; index < offlineBloodPacts.Count; index++)
        {
            NetworkBloodPactState state = offlineBloodPacts[index];
            if (!string.Equals(state.PactId.ToString(), pactId, StringComparison.Ordinal))
                continue;
            state.Stacks = (ushort)Math.Min(ushort.MaxValue, state.Stacks + 1);
            offlineBloodPacts[index] = state;
            BloodPactsChanged?.Invoke();
            return;
        }
        offlineBloodPacts.Add(new NetworkBloodPactState(pactId));
        BloodPactsChanged?.Invoke();
    }

    private void ClearBloodPactSnapshots()
    {
        if (UseNetworkValues) networkBloodPacts.Clear();
        else
        {
            offlineBloodPacts.Clear();
            BloodPactsChanged?.Invoke();
        }
    }

    private void NotifyGameplayHealthChanged(int previousHealth)
    {
        if (abilitySystem == null || previousHealth == CurrentHealth ||
            !NetworkAuthority.IsServerOrOffline(this))
        {
            return;
        }

        GameplayEventData healthChanged = new(
            GameplayEventType.HealthChanged,
            this,
            this,
            CurrentHealth - previousHealth,
            false,
            transform.position);
        abilitySystem.SendEvent(in healthChanged);
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
        networkCoins.OnValueChanged += HandleNetworkCoinsChanged;
        networkAlive.OnValueChanged += HandleNetworkAliveChanged;
        networkDashStaminaCost.OnValueChanged += HandleDashCostChanged;
        networkStaminaRecovery.OnValueChanged += HandleStaminaRecoveryChanged;
        networkDamage.OnValueChanged += HandleDamageChanged;
        networkWeaponRange.OnValueChanged += HandleAttackRangeChanged;
        networkMoveSpeed.OnValueChanged += HandleMoveSpeedChanged;
        networkDashSpeedMultiplier.OnValueChanged += HandleDashSpeedMultiplierChanged;
        networkDashDuration.OnValueChanged += HandleDashDurationChanged;
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
        networkCoins.OnValueChanged -= HandleNetworkCoinsChanged;
        networkAlive.OnValueChanged -= HandleNetworkAliveChanged;
        networkDashStaminaCost.OnValueChanged -= HandleDashCostChanged;
        networkStaminaRecovery.OnValueChanged -= HandleStaminaRecoveryChanged;
        networkDamage.OnValueChanged -= HandleDamageChanged;
        networkWeaponRange.OnValueChanged -= HandleAttackRangeChanged;
        networkMoveSpeed.OnValueChanged -= HandleMoveSpeedChanged;
        networkDashSpeedMultiplier.OnValueChanged -= HandleDashSpeedMultiplierChanged;
        networkDashDuration.OnValueChanged -= HandleDashDurationChanged;
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
    private void HandleNetworkCoinsChanged(int previous, int current) =>
        CoinsChanged?.Invoke(current);
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

    private void HandleBloodPactListChanged(
        NetworkListEvent<NetworkBloodPactState> change) =>
        BloodPactsChanged?.Invoke();

    private void HandleDashCostChanged(float p, float c) => Notify(PlayerStatType.DashStaminaCost);
    private void HandleStaminaRecoveryChanged(float p, float c) => Notify(PlayerStatType.StaminaRecovery);
    private void HandleDamageChanged(float p, float c) => Notify(PlayerStatType.BaseAttack);
    private void HandleAttackRangeChanged(float p, float c) => Notify(PlayerStatType.AttackRange);
    private void HandleMoveSpeedChanged(float p, float c) => Notify(PlayerStatType.MoveSpeed);
    private void HandleDashSpeedMultiplierChanged(float p, float c) =>
        Notify(PlayerStatType.DashSpeedMultiplier);
    private void HandleDashDurationChanged(float p, float c) => Notify(PlayerStatType.DashDuration);
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
        CoinsChanged?.Invoke(CurrentCoins);
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
