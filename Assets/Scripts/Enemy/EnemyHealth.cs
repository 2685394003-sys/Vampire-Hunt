using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;
using VampireHunt.Enemies.Application;
using VampireHunt.Enemies.Authoring;
using VampireHunt.Enemies.Contracts;
using VampireHunt.Navigation.Contracts;
using EntityId = VampireHunt.Core.EntityId;

/// <summary>
/// Serialized/network compatibility shell for the enemy prefab.
///
/// The authoritative life and death state lives in the injected IEnemyRuntime;
/// NetworkVariables and the legacy methods below are only projection and
/// transition adapters kept for existing Prefab/AnimationEvent references.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class EnemyHealth : NetworkBehaviour, IGameplayAbilitySystemHost,
    INetworkPoolLifecycle, IEnemyPoolLifecycle, IDamageReceiver, IHealingReceiver,
    IEnemyRuntimeBinding, IEnemyLifetimeBinding
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
    private GameplayAbilitySystem legacyAbilitySystem;
    private IEnemyRuntime runtime;
    private IEnemyLifetimePort lifetime;
    private EnemySpec boundSpec;
    private int offlineHealth;
    private bool offlineDead;
    private int cachedMaxHealth;

    public int CurrentHealth => UseNetworkValues ? networkHealth.Value :
        runtime != null && runtime.IsSpawned ? runtime.Snapshot.Health : offlineHealth;
    public bool IsDead => UseNetworkValues ? networkDead.Value :
        runtime != null && runtime.IsSpawned ? !runtime.Snapshot.IsAlive : offlineDead;
    public bool IsFrozen => legacyAbilitySystem?.Tags.Has("State.Control.Frozen") == true;
    public EnemyStatsConfig Config =>
        stats != null ? stats : stats = EnemyStatsConfig.LoadDefault();
    public EnemyStatusVfxPresenter StatusVfxPresenter => statusVfxPresenter;
    public IEnemyRuntime Runtime => runtime;
    public string GameplayOwnerId =>
        $"{(Config != null ? Config.enemyId : "enemy_missing_config")}:{(runtime != null ? runtime.Id.ToString() : "unspawned")}";
    public float GameplayHealthRatio => cachedMaxHealth > 0
        ? Mathf.Clamp01((float)CurrentHealth / cachedMaxHealth)
        : 0f;
    public GameplayAbilitySystem AbilitySystem => legacyAbilitySystem;
    private bool UseNetworkValues => NetworkAuthority.IsNetworkActive && IsSpawned;

    private void Awake()
    {
        networkDebuffs = new NetworkList<NetworkEnemyDebuffState>(
            null,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
        legacyAbilitySystem = new GameplayAbilitySystem(this);

        if (GetComponent<NetworkObject>() == null && !NetworkAuthority.IsNetworkActive)
            gameObject.AddComponent<NetworkObject>();

        hurtFlash = GetComponent<EnemyHurtFlash>();
        statusVfxPresenter = GetComponent<EnemyStatusVfxPresenter>();
        if (statusVfxPresenter == null)
            statusVfxPresenter = gameObject.AddComponent<EnemyStatusVfxPresenter>();

        cachedMaxHealth = GetConfiguredMaxHealth();
        offlineHealth = cachedMaxHealth;
        PublishRuntimePosition();
    }

    private void OnEnable() => EnemyRunStats.StatChanged += HandleRunStatChanged;
    private void OnDisable() => EnemyRunStats.StatChanged -= HandleRunStatChanged;

    public override void OnNetworkSpawn()
    {
        networkDebuffs.OnListChanged += HandleDebuffListChanged;
        RebuildDebuffPresentation();

        PublishRuntimePosition();
        if (!IsServer) return;

        legacyAbilitySystem?.Clear();
        instanceModifiers.Clear();
        networkDebuffs.Clear();
        if (runtime != null) ResetRuntimeForCurrentLife(false);
    }

    public override void OnNetworkDespawn()
    {
        networkDebuffs.OnListChanged -= HandleDebuffListChanged;
        statusVfxPresenter?.ClearAll();
    }

    public void OnTakenFromNetworkPool()
    {
        if (runtime != null) ResetRuntimeForCurrentLife(true);
    }

    public void OnReturnedToNetworkPool()
    {
        legacyAbilitySystem?.Clear();
        instanceModifiers.Clear();
        offlineDebuffReferences.Clear();
        statusVfxPresenter?.ClearAll();
        runtime?.ResetForDespawn();
        offlineHealth = 0;
        offlineDead = true;
    }

    public void OnTakenFromEnemyPool() => OnTakenFromNetworkPool();
    public void OnReturnedToEnemyPool() => OnReturnedToNetworkPool();

    private void Update()
    {
        PublishRuntimePosition();
        // Legacy GAS remains a compatibility adapter for already-authored
        // effects. New gameplay effects use VampireHunt.Abilities through the
        // composition root and do not depend on this component.
        if (NetworkAuthority.IsServerOrOffline(this) && !IsDead)
            legacyAbilitySystem?.Tick(Time.deltaTime);
    }

    /// <summary>Legacy AnimationEvent/Player entry point.</summary>
    [Obsolete("Use CombatApplicationService through the Enemy Contracts adapter.")]
    public void ChangeEnemyHealth(int amount) => ChangeEnemyHealth(amount, null);

    /// <summary>Compatibility overload; only source identity is extracted.</summary>
    [Obsolete("Use CombatApplicationService through the Enemy Contracts adapter.")]
    public void ChangeEnemyHealth(int amount, PlayerNetworkState damageDealer) =>
        ApplyDamage(amount, damageDealer, out _);

    /// <summary>Returns the actual health removed, excluding overkill.</summary>
    [Obsolete("Use IDamageReceiver through CombatApplicationService.")]
    public int ApplyDamage(int amount, PlayerNetworkState damageDealer) =>
        ApplyDamage(amount, damageDealer, out _);

    /// <summary>Legacy bridge used by PlayerAttact while it is migrated.</summary>
    [Obsolete("Use IDamageReceiver through CombatApplicationService.")]
    public int ApplyDamage(int amount, PlayerNetworkState damageDealer, out bool killed)
    {
        killed = false;
        if (!NetworkAuthority.IsServerOrOffline(this) || amount <= 0 || IsDead)
            return 0;

        EnsureRuntime();
        EntityId sourceId = ResolveSourceId(damageDealer);
        ResolvedDamage damage = new ResolvedDamage(
            sourceId,
            runtime.Id,
            amount,
            false,
            new HitContext(ToWorldPosition(transform.position)));
        DamageResult result = ApplyAuthoritativeDamage(in damage, sourceId, out killed);
        return result.AppliedDamage;
    }

    /// <summary>Combat capability consumed by CombatApplicationService.</summary>
    public DamageResult ApplyDamage(in ResolvedDamage damage)
    {
        if (!NetworkAuthority.IsServerOrOffline(this) || IsDead)
            return DamageResult.NoDamage(Math.Max(0, damage.FinalDamage), damage.Hit.Position);

        EnsureRuntime();
        return ApplyAuthoritativeDamage(in damage, damage.SourceId, out _);
    }

    public int ApplyHealing(int amount)
    {
        if (!NetworkAuthority.IsServerOrOffline(this) || amount <= 0 || IsDead)
            return 0;

        EnsureRuntime();
        int applied = runtime.ApplyHealing(amount);
        PublishRuntimeState();
        return applied;
    }

    private DamageResult ApplyAuthoritativeDamage(
        in ResolvedDamage damage,
        EntityId killerId,
        out bool killed)
    {
        killed = false;
        DamageResult result = runtime.ApplyDamage(in damage);
        PublishRuntimeState();
        if (result.AppliedDamage <= 0) return result;

        if (NetworkAuthority.IsNetworkActive)
            PlayHurtFeedbackRpc();
        else
            hurtFlash?.StartHurtFlash();

        if (!result.WasKilled) return result;

        killed = true;
        SetDead(true);
        // DeathService is the only reward/death transition. The default
        // compatibility controller has a no-op recipient until Bootstrap
        // supplies the Player reward adapter.
        runtime.SettleDeath(killerId);
        if (lifetime != null && runtime.Id.IsValid)
            lifetime.Release(runtime.Id);
        else
            NetworkSpawnUtility.Despawn(gameObject);
        return result;
    }

    /// <summary>
    /// Allows Bootstrap/Integration to provide the already-composed runtime
    /// facade (and therefore the authoritative reward/event ports).
    /// </summary>
    public bool TryBind(IEnemyRuntime value)
    {
        return TryBind(value, null);
    }

    public bool TryBind(IEnemyLifetimePort value)
    {
        if (value == null) return false;
        lifetime = value;
        return true;
    }

    /// <summary>
    /// Binds a composed runtime and the immutable archetype it owns. The
    /// archetype is retained across pooled lives; legacy EnemyStatsConfig is
    /// only used when Bootstrap did not provide one.
    /// </summary>
    public bool TryBind(IEnemyRuntime value, EnemySpec spec)
    {
        if (value == null) return false;
        if (ReferenceEquals(runtime, value))
        {
            boundSpec = spec ?? boundSpec;
            return true;
        }
        runtime?.ResetForDespawn();
        runtime = value;
        boundSpec = spec;
        if (!runtime.IsSpawned)
            runtime.ResetForSpawn(boundSpec ?? BuildSpec());
        PublishRuntimeState();
        return true;
    }

    /// <summary>
    /// Compatibility overload for existing Assembly-CSharp callers. New
    /// composition code should use IEnemyRuntimeBinding.TryBind instead.
    /// </summary>
    public void BindRuntime(EnemyRuntimeController value)
    {
        if (!TryBind(value)) throw new ArgumentNullException(nameof(value));
    }

    public float GetStatValue(EnemyStatType stat)
    {
        float runValue = EnemyRunStats.GetValue(Config, stat);
        return instanceModifiers.Evaluate(stat, runValue);
    }

    public int GetRoundedStatValue(EnemyStatType stat) => Mathf.RoundToInt(GetStatValue(stat));

    public bool AddGameplayModifier(
        string modifierId,
        string sourceId,
        GameplayAttributeType stat,
        PlayerModifierOperation operation,
        float value)
    {
        if (!NetworkAuthority.IsServerOrOffline(this) ||
            !TryMapAttribute(stat, out EnemyStatType enemyStat))
            return false;

        bool added = instanceModifiers.Add(modifierId, sourceId, enemyStat, operation, value);
        if (added && enemyStat == EnemyStatType.MaxHealth)
            cachedMaxHealth = GetConfiguredMaxHealth();
        return added;
    }

    public bool RemoveGameplayModifier(string modifierId)
    {
        if (!NetworkAuthority.IsServerOrOffline(this) ||
            !instanceModifiers.Remove(modifierId, out _))
            return false;

        cachedMaxHealth = GetConfiguredMaxHealth();
        return true;
    }

    public int DamageGameplay(int amount, IGameplayAbilitySystemHost source)
    {
        EntityId sourceId = source is Component component
            ? EnemyLegacyEntityIds.Resolve(component.gameObject)
            : EnemyLegacyEntityIds.Allocate();
        int dealt = ApplyDamage(amount, null, out bool killed, sourceId);
        return dealt;
    }

    private int ApplyDamage(
        int amount,
        PlayerNetworkState damageDealer,
        out bool killed,
        EntityId explicitSourceId)
    {
        killed = false;
        if (!NetworkAuthority.IsServerOrOffline(this) || amount <= 0 || IsDead)
            return 0;

        EnsureRuntime();
        ResolvedDamage damage = new ResolvedDamage(
            explicitSourceId,
            runtime.Id,
            amount,
            false,
            new HitContext(ToWorldPosition(transform.position)));
        return ApplyAuthoritativeDamage(in damage, explicitSourceId, out killed).AppliedDamage;
    }

    public int HealGameplay(int amount) => ApplyHealing(amount);

    public void AddScarletGameplay(float amount) { }
    public void AddCoinsGameplay(int amount) { }

    public void EmitGameplayCue(in GameplayCueEvent cueEvent)
    {
        if (string.IsNullOrWhiteSpace(cueEvent.CueTag)) return;

        if (NetworkAuthority.IsNetworkActive && IsSpawned)
        {
            if (!IsServer) return;
            if (cueEvent.CueTag.Length > 60) return;

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

    private void EnsureRuntime()
    {
        if (runtime != null) return;
        // Explicit transitional fallback for scenes that have not yet been
        // bound by Bootstrap. The composition root is the intended path.
        FlowFieldManager manager = FindFirstObjectByType<FlowFieldManager>();
        INavigationField navigation = manager != null
            ? new LegacyFlowFieldNavigation(manager)
            : new AlwaysWalkableNavigation();
        runtime = new EnemyRuntimeController(navigation, EnemyLegacyEntityIds.Source);
        runtime.ResetForSpawn(BuildSpec());
    }

    private void ResetRuntimeForCurrentLife(bool fromPool)
    {
        EnemySpec spec = boundSpec ?? BuildSpec();
        if (!runtime.IsSpawned || fromPool)
            runtime.ResetForSpawn(spec);
        PublishRuntimeState();
        PublishRuntimePosition();
    }

    private EnemySpec BuildSpec()
    {
        EnemyStatsConfig config = Config;
        int maxHealth = Mathf.Max(1, GetRoundedStatValue(EnemyStatType.MaxHealth));
        int damage = Mathf.Max(0, GetRoundedStatValue(EnemyStatType.Damage));
        float moveSpeed = Mathf.Max(0f, GetStatValue(EnemyStatType.MoveSpeed));
        float range = Mathf.Max(0f, GetStatValue(EnemyStatType.WeaponRange));
        float cooldown = Mathf.Max(0f, GetStatValue(EnemyStatType.AttackCooldown));
        RewardGrant reward = config == null
            ? new RewardGrant(0, 0, 0)
            : new RewardGrant(config.redResourceDropAmount, config.coinDropAmount, 0);
        return EnemySpecFactory.Create(
            config == null ? "enemy_missing_config" : config.enemyId,
            maxHealth,
            moveSpeed,
            damage,
            range,
            cooldown,
            config != null && config.isRanged ? EnemyAttackType.Ranged : EnemyAttackType.Melee,
            reward);
    }

    private EntityId ResolveSourceId(PlayerNetworkState damageDealer)
    {
        if (damageDealer != null)
        {
            EntityId candidate = EnemyLegacyEntityIds.Resolve(damageDealer.gameObject);
            if (candidate.IsValid) return candidate;
        }

        return EnemyLegacyEntityIds.Allocate();
    }

    private void PublishRuntimePosition() =>
        runtime?.SetPosition(ToWorldPosition(transform.position));

    private void PublishRuntimeState()
    {
        if (runtime == null || !runtime.IsSpawned) return;
        EnemySnapshot snapshot = runtime.Snapshot;
        cachedMaxHealth = Mathf.Max(1, snapshot.MaxHealth);
        if (UseNetworkValues)
        {
            networkHealth.Value = snapshot.Health;
            networkDead.Value = !snapshot.IsAlive;
        }
        else
        {
            offlineHealth = snapshot.Health;
            offlineDead = !snapshot.IsAlive;
        }
    }

    private void SetDead(bool value)
    {
        if (UseNetworkValues) networkDead.Value = value;
        else offlineDead = value;
    }

    private int GetConfiguredMaxHealth() => Mathf.Max(1, GetRoundedStatValue(EnemyStatType.MaxHealth));

    private void HandleRunStatChanged(EnemyStatType statType)
    {
        if (statType != EnemyStatType.MaxHealth ||
            !NetworkAuthority.IsServerOrOffline(this) || IsDead)
            return;

        cachedMaxHealth = GetConfiguredMaxHealth();
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
            if (offlineDebuffReferences.TryGetValue(cueEvent.CueTag, out int references) && references > 0)
                statusVfxPresenter?.Pulse(cueEvent.CueTag, cueEvent.Magnitude);
            return;
        }

        offlineDebuffReferences.TryGetValue(cueEvent.CueTag, out int activeReferences);
        activeReferences += cueEvent.Type == GameplayCueEventType.Applied ? 1 : -1;
        if (activeReferences <= 0)
        {
            offlineDebuffReferences.Remove(cueEvent.CueTag);
            statusVfxPresenter?.SetStatus(cueEvent.CueTag, false);
        }
        else
        {
            offlineDebuffReferences[cueEvent.CueTag] = activeReferences;
            statusVfxPresenter?.SetStatus(cueEvent.CueTag, true);
        }
    }

    private void HandleDebuffListChanged(NetworkListEvent<NetworkEnemyDebuffState> changeEvent) =>
        RebuildDebuffPresentation();

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
    private void PlayHurtFeedbackRpc() => hurtFlash?.StartHurtFlash();

    [Rpc(SendTo.ClientsAndHost)]
    private void GameplayCuePulseRpc(FixedString64Bytes cueTag, float magnitude)
    {
        string tag = cueTag.ToString();
        for (int index = 0; index < networkDebuffs.Count; index++)
        {
            NetworkEnemyDebuffState entry = networkDebuffs[index];
            if (entry.References > 0 && string.Equals(entry.CueTag.ToString(), tag, StringComparison.Ordinal))
            {
                statusVfxPresenter?.Pulse(tag, magnitude);
                return;
            }
        }
    }

    private static WorldPosition ToWorldPosition(Vector3 position) =>
        new WorldPosition(position.x, position.y, position.z);

    private sealed class AlwaysWalkableNavigation : INavigationField
    {
        public VampireHunt.Navigation.Domain.Direction SampleDirection(WorldPosition position, WorldPosition target) =>
            VampireHunt.Navigation.Domain.Direction.None;
        public bool IsWalkable(WorldPosition position) => true;
        public WorldPosition TryFindRecovery(WorldPosition position) => position;
    }
}
