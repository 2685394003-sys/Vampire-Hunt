using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class EnemyHealth : NetworkBehaviour, IGameplayAbilitySystemHost, INetworkPoolLifecycle
{
    [SerializeField] private EnemyStatsConfig stats;

    private readonly NetworkVariable<int> networkHealth = new(
        1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> networkDead = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private NetworkList<NetworkEnemyDebuffState> networkDebuffs;
    private readonly Dictionary<string, int> offlineDebuffReferences =
        new(StringComparer.Ordinal);
    private readonly EnemyInstanceStatModifiers instanceModifiers = new();
    private EnemyHurtFlash hurtFlash;
    private EnemyStatusVfxPresenter statusVfxPresenter;
    private GameplayAbilitySystem abilitySystem;
    private PlayerNetworkState lastDamageDealer;
    private int offlineHealth;
    private bool offlineDead;
    private int cachedMaxHealth;

    public int CurrentHealth => UseNetworkValues ? networkHealth.Value : offlineHealth;
    public bool IsDead => UseNetworkValues ? networkDead.Value : offlineDead;
    public bool IsFrozen => abilitySystem?.Tags.Has("State.Control.Frozen") == true;
    public EnemyStatsConfig Config =>
        stats != null ? stats : stats = EnemyStatsConfig.LoadDefault();
    public EnemyStatusVfxPresenter StatusVfxPresenter => statusVfxPresenter;
    public string GameplayOwnerId =>
        $"{(Config != null ? Config.enemyId : "enemy_missing_config")}:{(IsSpawned ? NetworkObjectId.ToString() : GetEntityId().ToString())}";
    public float GameplayHealthRatio => cachedMaxHealth > 0
        ? Mathf.Clamp01((float)CurrentHealth / cachedMaxHealth)
        : 0f;
    public GameplayAbilitySystem AbilitySystem => abilitySystem;
    private bool UseNetworkValues => NetworkAuthority.IsNetworkActive && IsSpawned;

    private void Awake()
    {
        networkDebuffs = new NetworkList<NetworkEnemyDebuffState>(
            null,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        abilitySystem = new GameplayAbilitySystem(this);

        if (GetComponent<NetworkObject>() == null && !NetworkAuthority.IsNetworkActive)
        {
            gameObject.AddComponent<NetworkObject>();
        }

        hurtFlash = GetComponent<EnemyHurtFlash>();
        statusVfxPresenter = GetComponent<EnemyStatusVfxPresenter>();
        if (statusVfxPresenter == null)
            statusVfxPresenter = gameObject.AddComponent<EnemyStatusVfxPresenter>();
        cachedMaxHealth = GetConfiguredMaxHealth();
        offlineHealth = cachedMaxHealth;
    }

    private void OnEnable() => EnemyRunStats.StatChanged += HandleRunStatChanged;
    private void OnDisable() => EnemyRunStats.StatChanged -= HandleRunStatChanged;

    public override void OnNetworkSpawn()
    {
        networkDebuffs.OnListChanged += HandleDebuffListChanged;
        RebuildDebuffPresentation();

        if (!IsServer) return;

        abilitySystem?.Clear();
        instanceModifiers.Clear();
        networkDebuffs.Clear();
        cachedMaxHealth = GetConfiguredMaxHealth();
        networkHealth.Value = cachedMaxHealth;
        networkDead.Value = false;
        lastDamageDealer = null;
    }

    public override void OnNetworkDespawn()
    {
        networkDebuffs.OnListChanged -= HandleDebuffListChanged;
        statusVfxPresenter?.ClearAll();
    }

    public void OnTakenFromNetworkPool()
    {
        abilitySystem?.Clear();
        instanceModifiers.Clear();
        offlineDebuffReferences.Clear();
        statusVfxPresenter?.ClearAll();
        lastDamageDealer = null;
        cachedMaxHealth = GetConfiguredMaxHealth();
        offlineHealth = cachedMaxHealth;
        offlineDead = false;
    }

    public void OnReturnedToNetworkPool()
    {
        statusVfxPresenter?.ClearAll();
    }

    private void Update()
    {
        if (NetworkAuthority.IsServerOrOffline(this) && !IsDead)
            abilitySystem?.Tick(Time.deltaTime);
    }

    /// <summary>Compatibility entry point for non-player or legacy damage.</summary>
    public void ChangeEnemyHealth(int amount)
    {
        ChangeEnemyHealth(amount, null);
    }

    /// <summary>
    /// Applies damage and remembers its player source. The last damaging player
    /// receives personal rewards when this hit (or a later unattributed hit)
    /// kills it. Scarlet is shared by PlayerNetworkState in multiplayer.
    /// </summary>
    public void ChangeEnemyHealth(int amount, PlayerNetworkState damageDealer)
    {
        ApplyDamage(amount, damageDealer);
    }

    /// <summary>Returns the actual health removed, excluding overkill.</summary>
    public int ApplyDamage(int amount, PlayerNetworkState damageDealer) =>
        ApplyDamage(amount, damageDealer, out _);

    /// <summary>Also captures lethality before network despawn destroys the target.</summary>
    public int ApplyDamage(
        int amount,
        PlayerNetworkState damageDealer,
        out bool killed)
    {
        killed = false;
        if (!NetworkAuthority.IsServerOrOffline(this) || amount <= 0 || IsDead)
        {
            return 0;
        }

        if (damageDealer != null) lastDamageDealer = damageDealer;
        int previousHealth = CurrentHealth;
        SetHealth(Mathf.Max(0, CurrentHealth - amount));
        if (NetworkAuthority.IsNetworkActive)
        {
            PlayHurtFeedbackRpc();
        }
        else
        {
            hurtFlash?.StartHurtFlash();
        }

        int damageDealt = previousHealth - CurrentHealth;
        if (CurrentHealth <= 0)
        {
            killed = true;
            ServerDie();
        }
        return damageDealt;
    }

    private void ServerDie()
    {
        if (IsDead || !NetworkAuthority.IsServerOrOffline(this)) return;

        SetDead(true);
        RewardPlayerDirectly(Config);
        NetworkSpawnUtility.Despawn(gameObject);
    }

    private void RewardPlayerDirectly(EnemyStatsConfig config)
    {
        PlayerNetworkState recipient = lastDamageDealer;
        if (recipient == null ||
            !recipient.IsAlive ||
            !recipient.gameObject.activeInHierarchy)
        {
            recipient = NetworkPlayerRegistry.GetClosestAlive(transform.position);
        }

        if (recipient == null)
        {
            Debug.LogWarning(
                $"[Enemy Reward] '{name}' died without an available player recipient.",
                this);
            return;
        }

        if (config == null) return;
        recipient.AddScarlet(config.redResourceDropAmount);
        recipient.AddCoins(config.coinDropAmount);

        // Existing health packs restored the player directly. With world drops
        // removed, a successful drop roll now performs that recovery immediately.
        if (config.healthPackDropChance > 0f &&
            UnityEngine.Random.value <= config.healthPackDropChance)
        {
            recipient.HealToFull();
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void PlayHurtFeedbackRpc()
    {
        hurtFlash?.StartHurtFlash();
    }

    private void SetHealth(int value)
    {
        if (UseNetworkValues) networkHealth.Value = value;
        else offlineHealth = value;
    }

    private void SetDead(bool value)
    {
        if (UseNetworkValues) networkDead.Value = value;
        else offlineDead = value;
    }

    private int GetConfiguredMaxHealth()
    {
        return GetRoundedStatValue(EnemyStatType.MaxHealth);
    }

    private void HandleRunStatChanged(EnemyStatType statType)
    {
        if (statType != EnemyStatType.MaxHealth ||
            !NetworkAuthority.IsServerOrOffline(this) ||
            IsDead)
        {
            return;
        }

        int nextMaxHealth = GetConfiguredMaxHealth();
        ApplyMaxHealthChange(nextMaxHealth);
    }

    public float GetStatValue(EnemyStatType stat)
    {
        float runValue = EnemyRunStats.GetValue(Config, stat);
        return instanceModifiers.Evaluate(stat, runValue);
    }

    public int GetRoundedStatValue(EnemyStatType stat) =>
        Mathf.RoundToInt(GetStatValue(stat));

    public bool AddGameplayModifier(
        string modifierId,
        string sourceId,
        GameplayAttributeType stat,
        PlayerModifierOperation operation,
        float value)
    {
        if (!NetworkAuthority.IsServerOrOffline(this) ||
            !TryMapAttribute(stat, out EnemyStatType enemyStat))
        {
            return false;
        }

        bool added = instanceModifiers.Add(
            modifierId,
            sourceId,
            enemyStat,
            operation,
            value);
        if (added && enemyStat == EnemyStatType.MaxHealth)
            ApplyMaxHealthChange(GetConfiguredMaxHealth());
        return added;
    }

    public bool RemoveGameplayModifier(string modifierId)
    {
        if (!NetworkAuthority.IsServerOrOffline(this) ||
            !instanceModifiers.Remove(modifierId, out EnemyStatType changedStat))
        {
            return false;
        }

        if (changedStat == EnemyStatType.MaxHealth)
            ApplyMaxHealthChange(GetConfiguredMaxHealth());
        return true;
    }

    public int DamageGameplay(int amount, IGameplayAbilitySystemHost source)
    {
        Vector3 deathPosition = transform.position;
        int combatTextTargetKey = GetEntityId().GetHashCode();
        PlayerNetworkState playerSource = source as PlayerNetworkState;
        int dealt = ApplyDamage(amount, playerSource, out bool killed);
        playerSource?.ReportGameplayEffectDamage(
            dealt,
            deathPosition,
            combatTextTargetKey);
        if (killed) playerSource?.ReportEnemyKilled(deathPosition);
        return dealt;
    }

    public int HealGameplay(int amount)
    {
        if (!NetworkAuthority.IsServerOrOffline(this) || amount <= 0 || IsDead)
            return 0;

        int previous = CurrentHealth;
        SetHealth(Mathf.Min(cachedMaxHealth, previous + amount));
        return CurrentHealth - previous;
    }

    public void AddScarletGameplay(float amount) { }
    public void AddCoinsGameplay(int amount) { }

    public void EmitGameplayCue(in GameplayCueEvent cueEvent)
    {
        if (string.IsNullOrWhiteSpace(cueEvent.CueTag)) return;

        if (NetworkAuthority.IsNetworkActive && IsSpawned)
        {
            if (!IsServer) return;
            if (cueEvent.CueTag.Length > 60)
            {
                Debug.LogWarning(
                    $"[Enemy GAS] Cue tag is too long for network transport: {cueEvent.CueTag}",
                    this);
                return;
            }

            if (cueEvent.Type == GameplayCueEventType.Applied)
                ChangeNetworkDebuffReference(cueEvent.CueTag, 1);
            else if (cueEvent.Type == GameplayCueEventType.Removed)
                ChangeNetworkDebuffReference(cueEvent.CueTag, -1);
            else
                GameplayCuePulseRpc(
                    new FixedString64Bytes(cueEvent.CueTag),
                    cueEvent.Magnitude);
            return;
        }

        ApplyOfflineCue(cueEvent);
    }

    private void ApplyMaxHealthChange(int nextMaxHealth)
    {
        nextMaxHealth = Mathf.Max(1, nextMaxHealth);
        int delta = nextMaxHealth - cachedMaxHealth;
        cachedMaxHealth = nextMaxHealth;
        SetHealth(Mathf.Clamp(
            CurrentHealth + Mathf.Max(0, delta),
            0,
            nextMaxHealth));
    }

    private static bool TryMapAttribute(
        GameplayAttributeType attribute,
        out EnemyStatType stat)
    {
        switch (attribute)
        {
            case GameplayAttributeType.MaxHealth:
                stat = EnemyStatType.MaxHealth;
                return true;
            case GameplayAttributeType.BaseAttack:
                stat = EnemyStatType.Damage;
                return true;
            case GameplayAttributeType.MoveSpeed:
                stat = EnemyStatType.MoveSpeed;
                return true;
            case GameplayAttributeType.AttackRange:
                stat = EnemyStatType.WeaponRange;
                return true;
            case GameplayAttributeType.AttackInterval:
                stat = EnemyStatType.AttackCooldown;
                return true;
            case GameplayAttributeType.KnockbackForce:
                stat = EnemyStatType.KnockbackForce;
                return true;
            default:
                stat = default;
                return false;
        }
    }

    private void ChangeNetworkDebuffReference(string cueTag, int delta)
    {
        for (int index = 0; index < networkDebuffs.Count; index++)
        {
            NetworkEnemyDebuffState entry = networkDebuffs[index];
            if (!string.Equals(entry.CueTag.ToString(), cueTag, StringComparison.Ordinal))
                continue;

            int next = Mathf.Clamp(entry.References + delta, 0, ushort.MaxValue);
            if (next == 0) networkDebuffs.RemoveAt(index);
            else
            {
                entry.References = (ushort)next;
                networkDebuffs[index] = entry;
            }
            return;
        }

        if (delta > 0)
            networkDebuffs.Add(new NetworkEnemyDebuffState(cueTag, (ushort)delta));
    }

    private void ApplyOfflineCue(in GameplayCueEvent cueEvent)
    {
        if (cueEvent.Type == GameplayCueEventType.Executed)
        {
            if (offlineDebuffReferences.TryGetValue(
                    cueEvent.CueTag,
                    out int activeReferences) &&
                activeReferences > 0)
            {
                statusVfxPresenter?.Pulse(cueEvent.CueTag, cueEvent.Magnitude);
            }
            return;
        }

        offlineDebuffReferences.TryGetValue(cueEvent.CueTag, out int references);
        references += cueEvent.Type == GameplayCueEventType.Applied ? 1 : -1;
        if (references <= 0)
        {
            offlineDebuffReferences.Remove(cueEvent.CueTag);
            statusVfxPresenter?.SetStatus(cueEvent.CueTag, false);
        }
        else
        {
            offlineDebuffReferences[cueEvent.CueTag] = references;
            statusVfxPresenter?.SetStatus(cueEvent.CueTag, true);
        }
    }

    private void HandleDebuffListChanged(
        NetworkListEvent<NetworkEnemyDebuffState> changeEvent)
    {
        RebuildDebuffPresentation();
    }

    private void RebuildDebuffPresentation()
    {
        statusVfxPresenter?.ClearAll();
        if (networkDebuffs == null) return;
        for (int index = 0; index < networkDebuffs.Count; index++)
        {
            NetworkEnemyDebuffState entry = networkDebuffs[index];
            if (entry.References > 0)
                statusVfxPresenter?.SetStatus(entry.CueTag.ToString(), true);
        }
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void GameplayCuePulseRpc(FixedString64Bytes cueTag, float magnitude)
    {
        string tag = cueTag.ToString();
        for (int index = 0; index < networkDebuffs.Count; index++)
        {
            NetworkEnemyDebuffState entry = networkDebuffs[index];
            if (entry.References > 0 &&
                string.Equals(entry.CueTag.ToString(), tag, StringComparison.Ordinal))
            {
                statusVfxPresenter?.Pulse(tag, magnitude);
                return;
            }
        }
    }
}
