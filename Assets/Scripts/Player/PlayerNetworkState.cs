using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;
using VampireHunt.Infrastructure.Input.Contracts;
using VampireHunt.Infrastructure.Netcode.Contracts;
using VampireHunt.Infrastructure.Netcode.Player;
using VampireHunt.Player.Contracts;
using EntityId = VampireHunt.Core.EntityId;

/// <summary>
/// Stable NetworkBehaviour kept on the shipped Player Prefab during migration.
/// It is now only a state/command adapter: authoritative mutations are routed
/// through IPlayerRuntimePort and network variables carry replicated snapshots.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class PlayerNetworkState : PlayerPoseNetworkAdapter, IPlayerRunStats,
    IGameplayAbilitySystemHost, IPlayerRuntimeBinding, IPlayerBloodPactReadModel,
    IPlayerPoseTransport, IPlayerPoseOwnershipBinding
{
    private static readonly EntityIdAllocator CompatibilityIds = new(5000000UL);
    [SerializeField] private PlayerStatsConfig baseStats;
    [SerializeField] private bool hideVisualsWhenDead = true;
    [SerializeField] private ulong logicalPlayerId;
    [NonSerialized] private bool logicalPlayerIdWasGenerated;

    // Network variables are a replication cache only. They are never used as
    // an alternative domain store when a runtime port is bound.
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
    private readonly NetworkVariable<ulong> networkLogicalPlayerId = ServerVariable(0UL);
    private readonly NetworkVariable<uint> networkBloodPactOfferVersion = ServerVariable(0u);
    private readonly NetworkVariable<int> networkBloodPactOfferCost = ServerVariable(0);
    private readonly NetworkVariable<FixedString64Bytes> networkBloodPactOfferChoice0 =
        ServerVariable(new FixedString64Bytes());
    private readonly NetworkVariable<FixedString64Bytes> networkBloodPactOfferChoice1 =
        ServerVariable(new FixedString64Bytes());
    private readonly NetworkVariable<FixedString64Bytes> networkBloodPactOfferChoice2 =
        ServerVariable(new FixedString64Bytes());
    private NetworkList<NetworkBloodPactState> networkBloodPacts;

    private readonly List<NetworkBloodPactState> offlineBloodPacts = new();
    private IPlayerRuntimePort runtime;
    private PlayerSnapshot offlineSnapshot;
    private BloodPactOffer currentOffer;
    private uint commandSequence;
    private bool networkEventsSubscribed;

    public event Action<int, int> HealthChanged;
    public event Action<float, float> StaminaChanged;
    public event Action<float, float> ScarletChanged;
    public event Action<int> CoinsChanged;
    public event Action<bool> AliveChanged;
    public event Action<PlayerStatType> RunStatChanged;
    public event Action BloodPactsChanged;
    public event Action BloodPactOfferChanged;
    public event Action<GameplayCueEvent> GameplayCueRequested;

    public PlayerStatsConfig BaseStats => ResolveBaseStats();
    public string PlayerId => BaseStats != null ? BaseStats.PlayerId : "player_missing_config";
    public EntityId LogicalPlayerId => ResolveLogicalPlayerId();
    public IPlayerRuntimePort RuntimePort => runtime;
    public PlayerSnapshot Snapshot => ReadSnapshot();

    /// <summary>
    /// Returns the server-assigned identity without allocating a compatibility
    /// id on a network client before Bootstrap has replicated the binding.
    /// </summary>
    public bool TryGetAuthoritativeLogicalPlayerId(out EntityId playerId)
    {
        if (runtime != null && runtime.PlayerId.IsValid)
        {
            playerId = runtime.PlayerId;
            return true;
        }

        if (NetworkAuthority.IsNetworkActive)
        {
            if (IsSpawned && EntityId.TryCreate(networkLogicalPlayerId.Value, out playerId))
                return true;
            playerId = EntityId.Invalid;
            return false;
        }

        playerId = LogicalPlayerId;
        return playerId.IsValid;
    }

    public int MaxHealth => ReadSnapshot().MaxHealth;
    public int CurrentHealth => ReadSnapshot().CurrentHealth;
    public float MaxStamina => runtime != null ? runtime.MaxStamina : Read(networkMaxStamina, offlineSnapshot.MaxStamina);
    public float CurrentStamina => runtime != null ? runtime.Stamina : Read(networkStamina, offlineSnapshot.Stamina);
    public float DashStaminaCost => runtime != null ? runtime.Values.DashStaminaCost : Read(networkDashStaminaCost, BaseStatsValue(c => c.DashStaminaCost, 15f));
    public float StaminaRecoverySpeed => runtime != null ? runtime.Values.StaminaRecoveryPerSecond : Read(networkStaminaRecovery, BaseStatsValue(c => c.StaminaRecoverSpeed, 15f));
    public float Damage => runtime != null ? BaseStatsValue(c => c.BaseAttack, 10f) : Read(networkDamage, BaseStatsValue(c => c.BaseAttack, 10f));
    public float WeaponRange => runtime != null ? runtime.Values.AttackRange : Read(networkWeaponRange, BaseStatsValue(c => c.AttackRange, 2f));
    public float MoveSpeed => runtime != null ? runtime.Values.MoveSpeed : Read(networkMoveSpeed, BaseStatsValue(c => c.MoveSpeed, 5f));
    public float DashSpeedMultiplier => runtime != null ? runtime.Values.DashSpeedMultiplier : Read(networkDashSpeedMultiplier, BaseStatsValue(c => c.DashSpeedMultiplier, 2f));
    public float DashDuration => runtime != null ? runtime.Values.DashDuration : Read(networkDashDuration, BaseStatsValue(c => c.DashDuration, 0.15f));
    public float AttackCooldown => runtime != null ? BaseStatsValue(c => c.AttackInterval, 1f) : Read(networkAttackCooldown, BaseStatsValue(c => c.AttackInterval, 1f));
    public float KnockbackForce => runtime != null ? runtime.Values.KnockbackForce : Read(networkKnockbackForce, BaseStatsValue(c => c.KnockbackForce, 5f));
    public float CritRate => runtime != null ? BaseStatsValue(c => c.CritRate, 0.05f) : Read(networkCritRate, BaseStatsValue(c => c.CritRate, 0.05f));
    public float CritDamage => runtime != null ? BaseStatsValue(c => c.CritDamage, 2f) : Read(networkCritDamage, BaseStatsValue(c => c.CritDamage, 2f));
    public float InvincibleTime => runtime != null ? BaseStatsValue(c => c.InvincibleTime, 0.8f) : Read(networkInvincibleTime, BaseStatsValue(c => c.InvincibleTime, 0.8f));
    public float FlashSpeed => Read(networkFlashSpeed, BaseStatsValue(c => c.FlashSpeed, 10f));
    public float MaxScarlet => runtime != null ? BaseStatsValue(c => c.MaxScarlet, 100f) : Read(networkMaxScarlet, BaseStatsValue(c => c.MaxScarlet, 100f));
    public float CurrentScarlet => runtime != null ? runtime.Scarlet : Read(networkScarlet, offlineSnapshot.Scarlet);
    int IPlayerBloodPactReadModel.BloodPactScarlet => Mathf.FloorToInt(CurrentScarlet);
    bool IPlayerBloodPactReadModel.IsBloodPactPlayerAlive => IsAlive;
    public int CurrentCoins => runtime != null ? runtime.Coins : Read(networkCoins, offlineSnapshot.Coins);
    public bool IsAlive => runtime != null ? runtime.IsAlive : !UseNetworkValues ? offlineSnapshot.IsAlive : networkAlive.Value;
    public GameplayAbilitySystem AbilitySystem => null;
    public string GameplayOwnerId => PlayerId;
    public float GameplayHealthRatio => MaxHealth > 0 ? Mathf.Clamp01((float)CurrentHealth / MaxHealth) : 0f;
    public int ActiveBloodPactCount => ReadSnapshot().BloodPacts.Count;
    public float KnockbackTime => runtime != null ? runtime.Values.KnockbackDuration : BaseStatsValue(c => c.KnockbackDuration, 0f);
    public float StunTime => runtime != null ? runtime.Values.StunDuration : BaseStatsValue(c => c.StunDuration, 0f);
    public float AttackConeAngle => runtime != null ? runtime.Values.AttackConeAngle : BaseStatsValue(c => c.AttackConeAngle, 110f);
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
        ResolveBaseStats();
        offlineSnapshot = BuildFallbackSnapshot();
        ApplyConfigToReplicationCache();
    }

    private void OnEnable() => NetworkPlayerRegistry.Register(this);

    private void OnDisable()
    {
        UnsubscribeNetworkEvents();
        NetworkPlayerRegistry.Unregister(this);
    }

    public override void OnNetworkSpawn()
    {
        NetworkPlayerRegistry.Register(this);
        SubscribeNetworkEvents();
        networkBloodPacts.OnListChanged += HandleBloodPactListChanged;
        if (IsServer && !networkInitialized.Value) ApplyConfigToReplicationCache();
        if (!IsServer) HandleNetworkOfferChanged();
        CaptureRuntimeSnapshot();
        PublishAll();
    }

    public override void OnNetworkDespawn()
    {
        if (networkBloodPacts != null) networkBloodPacts.OnListChanged -= HandleBloodPactListChanged;
        UnsubscribeNetworkEvents();
        NetworkPlayerRegistry.Unregister(this);
    }

    /// <summary>Binds the application endpoint supplied by the composition root.</summary>
    public bool TryBind(IPlayerRuntimePort next)
    {
        // A prefab with no serialized logical id receives a temporary
        // compatibility id when Awake builds its fallback snapshot. Bootstrap
        // must still be able to replace that temporary value with the server's
        // allocated runtime id; an explicitly serialized id remains a hard
        // compatibility check.
        if (next == null ||
            (logicalPlayerId != 0UL && !logicalPlayerIdWasGenerated && next.PlayerId != LogicalPlayerId))
            return false;
        if (runtime != null) runtime.SnapshotChanged -= HandleRuntimeSnapshotChanged;
        runtime = next;
        BindPoseRuntime(next);
        logicalPlayerId = next.PlayerId.Value;
        logicalPlayerIdWasGenerated = false;
        if (UseNetworkValues && IsServer) networkLogicalPlayerId.Value = logicalPlayerId;
        runtime.SnapshotChanged += HandleRuntimeSnapshotChanged;
        currentOffer = default;
        CaptureRuntimeSnapshot();
        PublishAll();
        return true;
    }

    /// <summary>Injects the server-side sender/owner directory.</summary>
    public void ConfigurePoseOwnership(INetworkCommandOwnership ownership) => BindPoseOwnership(ownership);

    /// <summary>
    /// OwnerMovementMotor transport. Offline validation enters the same
    /// endpoint directly; network clients send only the versioned wire pose.
    /// </summary>
    public void SubmitPose(EntityId playerId, MovementPose pose)
    {
        if (NetworkAuthority.IsNetworkActive && playerId != LogicalPlayerId) return;
        SubmitOwnerPose(playerId, pose);
    }

    public bool TryGetBloodPactOffer(out BloodPactOffer offer)
    {
        if (runtime != null && runtime.TryGetBloodPactOffer(out offer))
        {
            currentOffer = offer;
            return true;
        }

        if (UseNetworkValues && TryReadNetworkOffer(out offer))
        {
            currentOffer = offer;
            return true;
        }

        // An offer is single-use. Do not leave a stale UI offer alive after a
        // successful selection or a server-side invalidation.
        currentOffer = default;
        offer = default;
        return false;
    }

    public bool RequestBloodPactOffer()
    {
        if (runtime != null && NetworkAuthority.IsServerOrOffline(this))
        {
            if (!runtime.TryCreateBloodPactOffer(out BloodPactOffer offer)) return false;
            currentOffer = offer;
            CaptureOffer();
            BloodPactOfferChanged?.Invoke();
            return true;
        }

        if (!UseNetworkValues || !IsOwner || !IsSpawned) return false;
        RequestBloodPactOfferRpc();
        return true;
    }

    [Obsolete("Submit a SelectBloodPactCommand through IPlayerCommandGateway; this method only preserves the UnityEvent entry point.")]
    public bool RequestBloodPactSelection(string pactId)
    {
        if (string.IsNullOrWhiteSpace(pactId) || pactId.Length > 64 || !IsAlive)
            return false;

        // A network client may only have the replicated offer cache. It still
        // needs to be able to submit the selection intent without a local
        // runtime endpoint; the server will validate it against its offer.
        if (runtime == null && (!UseNetworkValues || !TryReadNetworkOffer(out BloodPactOffer replicatedOffer)))
            return false;

        BloodPactId selection = new(pactId);
        uint offerVersion = currentOffer.OfferVersion;
        if (runtime != null && runtime.TryGetBloodPactOffer(out BloodPactOffer offer))
            offerVersion = offer.OfferVersion;
        else if (UseNetworkValues && TryReadNetworkOffer(out BloodPactOffer networkOffer))
        {
            currentOffer = networkOffer;
            offerVersion = networkOffer.OfferVersion;
        }
        if (offerVersion == 0U) return false;

        uint sequence = NextCommandSequence();
        SelectBloodPactCommand command = new(LogicalPlayerId, selection, offerVersion, sequence);
        if (!NetworkAuthority.IsNetworkActive)
            return runtime != null && runtime.SelectBloodPact(command).Accepted;
        if (!IsSpawned || !IsOwner) return false;
        SubmitBloodPactRpc(new FixedString64Bytes(selection.Value), offerVersion, sequence);
        return true;
    }

    public bool HasBloodPact(string pactId) => GetBloodPactStacks(pactId) > 0;

    public int GetBloodPactStacks(string pactId)
    {
        if (string.IsNullOrWhiteSpace(pactId)) return 0;
        BloodPactId id = new(pactId);
        if (runtime != null && runtime.TryGetBloodPactStacks(id, out int stacks)) return stacks;
        IReadOnlyList<BloodPactStack> pacts = ReadSnapshot().BloodPacts;
        for (int i = 0; i < pacts.Count; i++) if (pacts[i].Id == id) return pacts[i].Stacks;
        return 0;
    }

    public CommandResult RequestAttackIntent(Vector3 aimAt)
    {
        if (!IsAlive || !NetworkAuthority.IsOwnerOrOffline(this))
            return new CommandResult(CommandResultStatus.Rejected, "Player runtime is unavailable");
        WorldPosition aim = ToWorldPosition(aimAt);
        uint sequence = NextCommandSequence();
        if (!NetworkAuthority.IsNetworkActive)
        {
            if (runtime == null) return new CommandResult(CommandResultStatus.Rejected, "Player runtime is unavailable");
            return runtime.SubmitAttack(new AttackCommand(LogicalPlayerId, aim, sequence));
        }
        if (!IsSpawned || !IsOwner) return new CommandResult(CommandResultStatus.Unauthorized);
        SubmitAttackRpc(aimAt, sequence);
        return CommandResult.Accept(sequence);
    }

    public CommandResult RequestDashIntent(Vector3 direction)
    {
        if (!IsAlive || !NetworkAuthority.IsOwnerOrOffline(this))
            return new CommandResult(CommandResultStatus.Rejected, "Player runtime is unavailable");
        MoveVector move = new(direction.x, 0f, direction.z);
        uint sequence = NextCommandSequence();
        if (!NetworkAuthority.IsNetworkActive)
        {
            if (runtime == null) return new CommandResult(CommandResultStatus.Rejected, "Player runtime is unavailable");
            return runtime.SubmitDash(new DashCommand(LogicalPlayerId, move, sequence));
        }
        if (!IsSpawned || !IsOwner) return new CommandResult(CommandResultStatus.Unauthorized);
        SubmitDashRpc(direction, sequence);
        return CommandResult.Accept(sequence);
    }

    public bool ApplyDamage(int amount) =>
        ApplyDamage(new PlayerDamageCommand(LogicalPlayerId, LogicalPlayerId, amount, ToWorldPosition(transform.position)));

    public bool ApplyDamage(PlayerDamageCommand command)
    {
        if (!NetworkAuthority.IsServerOrOffline(this) || runtime == null) return false;
        PlayerDamageResult result = runtime.ApplyDamage(command);
        CaptureRuntimeSnapshot();
        return result.Accepted && result.AppliedDamage > 0;
    }

    public int RestoreHealth(int amount)
    {
        if (!NetworkAuthority.IsServerOrOffline(this) || runtime == null) return 0;
        int result = runtime.ApplyHealing(amount);
        CaptureRuntimeSnapshot();
        return result;
    }

    public void HealToFull()
    {
        if (!NetworkAuthority.IsServerOrOffline(this) || runtime == null) return;
        runtime.RestoreToFull();
        CaptureRuntimeSnapshot();
    }

    public void ForceDeath()
    {
        if (!NetworkAuthority.IsServerOrOffline(this) || runtime == null) return;
        runtime.ForceDeath(default);
        CaptureRuntimeSnapshot();
    }

    public bool TryConsumeStamina(float amount) =>
        NetworkAuthority.IsServerOrOffline(this) && runtime != null && runtime.TryConsumeStamina(amount);

    public void RestoreStamina(float amount)
    {
        if (NetworkAuthority.IsServerOrOffline(this)) runtime?.RestoreStamina(amount);
    }

    public void SetStaminaRecoveryPaused(bool value)
    {
        // Retained as an AnimationEvent/legacy entry point. Recovery windows
        // are now represented by PlayerMobilityState and server tick state.
    }

    public bool TryConsumeScarlet(float amount)
    {
        if (!NetworkAuthority.IsServerOrOffline(this) || amount < 0f || amount > int.MaxValue || runtime == null)
            return false;
        return runtime.TrySpendScarlet(Mathf.RoundToInt(amount));
    }

    public void AddScarlet(float amount)
    {
        if (!NetworkAuthority.IsServerOrOffline(this) || amount <= 0f || amount > int.MaxValue || runtime == null)
            return;
        runtime.AddScarlet(Mathf.RoundToInt(amount));
    }

    public void AddCoins(int amount)
    {
        if (NetworkAuthority.IsServerOrOffline(this)) runtime?.AddCoins(amount);
    }

    public bool TrySpendCoins(int amount) =>
        NetworkAuthority.IsServerOrOffline(this) && runtime != null && runtime.TrySpendCoins(amount);

    public bool ResetForNewRun() =>
        NetworkAuthority.IsServerOrOffline(this) && runtime != null && runtime.ResetForNewRun();

    void IPlayerRunStats.ResetForNewRun() => ResetForNewRun();

    public bool ApplyRunUpgrade(PlayerStatUpgrade upgrade)
    {
        if (!NetworkAuthority.IsServerOrOffline(this) || runtime == null ||
            !Enum.IsDefined(typeof(PlayerStatType), upgrade.stat) ||
            !Enum.IsDefined(typeof(PlayerStatOperation), upgrade.operation)) return false;
        byte operation = upgrade.operation == PlayerStatOperation.Add ? (byte)0 : (byte)2;
        return runtime.TryApplyModifier(new PlayerModifierCommand(
            $"legacy:{++commandSequence}:{(int)upgrade.stat}",
            "legacy_upgrade",
            (ushort)upgrade.stat,
            operation,
            upgrade.value));
    }

    public bool AddStatModifier(PlayerStatModifier modifier)
    {
        if (!NetworkAuthority.IsServerOrOffline(this) || runtime == null) return false;
        byte operation = modifier.operation switch
        {
            PlayerModifierOperation.Flat => 0,
            PlayerModifierOperation.AdditivePercent => 1,
            PlayerModifierOperation.Multiplicative => 2,
            _ => 255
        };
        if (operation == 255) return false;
        return runtime.TryApplyModifier(new PlayerModifierCommand(
            modifier.modifierId,
            modifier.sourceId,
            (ushort)modifier.stat,
            operation,
            modifier.value,
            modifier.stacks,
            modifier.maxStacks,
            modifier.durationSeconds));
    }

    public bool AddGameplayModifier(
        string modifierId,
        string sourceId,
        GameplayAttributeType stat,
        PlayerModifierOperation operation,
        float value)
    {
        return AddStatModifier(new PlayerStatModifier(
            modifierId,
            sourceId,
            (PlayerStatType)(int)stat,
            operation,
            value));
    }

    public bool RemoveGameplayModifier(string modifierId) => RemoveStatModifier(modifierId);
    public bool RemoveStatModifier(string modifierId) =>
        NetworkAuthority.IsServerOrOffline(this) && runtime != null && runtime.RemoveModifier(modifierId);

    public int RemoveStatModifiersFromSource(string sourceId) =>
        NetworkAuthority.IsServerOrOffline(this) ? runtime?.RemoveModifiersFromSource(sourceId) ?? 0 : 0;

    public bool TryGetStatBreakdown(PlayerStatType stat, out PlayerStatBreakdown breakdown)
    {
        // Read-only compatibility path. The authoritative modifier collection
        // remains inside Player Application; old components cannot mutate it.
        breakdown = default;
        return false;
    }

    public void ApplyUpgrade(int damageBonus, float moveSpeedBonus, int maxHealthBonus)
    {
        ApplyRunUpgrade(new PlayerStatUpgrade(PlayerStatType.BaseAttack, damageBonus));
        ApplyRunUpgrade(new PlayerStatUpgrade(PlayerStatType.MoveSpeed, moveSpeedBonus));
        ApplyRunUpgrade(new PlayerStatUpgrade(PlayerStatType.MaxHealth, maxHealthBonus));
    }

    public int DamageGameplay(int amount, IGameplayAbilitySystemHost source)
    {
        EntityId sourceId = source is PlayerNetworkState player ? player.LogicalPlayerId : LogicalPlayerId;
        int previous = CurrentHealth;
        ApplyDamage(new PlayerDamageCommand(sourceId, LogicalPlayerId, amount, ToWorldPosition(transform.position)));
        return Math.Max(0, previous - CurrentHealth);
    }

    public int HealGameplay(int amount) => RestoreHealth(amount);
    public void AddScarletGameplay(float amount) => AddScarlet(amount);
    public void AddCoinsGameplay(int amount) => AddCoins(amount);

    [Obsolete("Gameplay cues are emitted by Player Application and consumed by Presenters.")]
    public void EmitGameplayCue(in GameplayCueEvent cueEvent) => GameplayCueRequested?.Invoke(cueEvent);

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void SubmitAttackRpc(Vector3 aimAt, uint sequence)
    {
        if (runtime == null) return;
        runtime.SubmitAttack(new AttackCommand(LogicalPlayerId, ToWorldPosition(aimAt), sequence));
        CaptureRuntimeSnapshot();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void SubmitDashRpc(Vector3 direction, uint sequence)
    {
        if (runtime == null) return;
        runtime.SubmitDash(new DashCommand(LogicalPlayerId, new MoveVector(direction.x, 0f, direction.z), sequence));
        CaptureRuntimeSnapshot();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void SubmitBloodPactRpc(FixedString64Bytes selection, uint offerVersion, uint sequence)
    {
        if (runtime == null) return;
        runtime.SelectBloodPact(new SelectBloodPactCommand(
            LogicalPlayerId,
            new BloodPactId(selection.ToString()),
            offerVersion,
            sequence));
        CaptureRuntimeSnapshot();
        CaptureOffer();
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
    private void RequestBloodPactOfferRpc()
    {
        if (runtime == null || !runtime.TryCreateBloodPactOffer(out BloodPactOffer offer)) return;
        currentOffer = offer;
        CaptureOffer();
        BloodPactOfferChanged?.Invoke();
    }

    private void HandleRuntimeSnapshotChanged(PlayerSnapshot next)
    {
        offlineSnapshot = next;
        CaptureRuntimeSnapshot();
        PublishAll();
    }

    private void CaptureRuntimeSnapshot()
    {
        if (runtime == null) return;
        offlineSnapshot = runtime.Snapshot;
        if (!UseNetworkValues || !IsServer) return;
        PlayerSnapshot state = runtime.Snapshot;
        networkMaxHealth.Value = state.MaxHealth;
        networkHealth.Value = state.CurrentHealth;
        networkMaxStamina.Value = state.MaxStamina;
        networkStamina.Value = state.Stamina;
        networkScarlet.Value = state.Scarlet;
        networkCoins.Value = state.Coins;
        networkAlive.Value = state.IsAlive;
        SyncBloodPacts(state.BloodPacts);
        networkInitialized.Value = true;
        CaptureOffer();
    }

    private void CaptureOffer()
    {
        if (!UseNetworkValues || !IsServer) return;

        BloodPactOffer offer = default;
        if (runtime != null) runtime.TryGetBloodPactOffer(out offer);
        networkBloodPactOfferVersion.Value = offer.IsValid ? offer.OfferVersion : 0u;
        networkBloodPactOfferCost.Value = offer.IsValid ? offer.Cost : 0;
        networkBloodPactOfferChoice0.Value = OfferChoice(offer, 0);
        networkBloodPactOfferChoice1.Value = OfferChoice(offer, 1);
        networkBloodPactOfferChoice2.Value = OfferChoice(offer, 2);
    }

    private static FixedString64Bytes OfferChoice(BloodPactOffer offer, int index) =>
        offer.IsValid && index < offer.Choices.Count
            ? new FixedString64Bytes(offer.Choices[index].Value)
            : new FixedString64Bytes();

    private bool TryReadNetworkOffer(out BloodPactOffer offer)
    {
        if (networkBloodPactOfferVersion.Value == 0u)
        {
            offer = default;
            return false;
        }

        List<BloodPactId> choices = new(3);
        AddNetworkOfferChoice(choices, networkBloodPactOfferChoice0.Value);
        AddNetworkOfferChoice(choices, networkBloodPactOfferChoice1.Value);
        AddNetworkOfferChoice(choices, networkBloodPactOfferChoice2.Value);
        offer = new BloodPactOffer(
            LogicalPlayerId,
            networkBloodPactOfferVersion.Value,
            networkBloodPactOfferCost.Value,
            choices);
        return offer.IsValid;
    }

    private static void AddNetworkOfferChoice(List<BloodPactId> choices, FixedString64Bytes value)
    {
        string text = value.ToString();
        if (!string.IsNullOrWhiteSpace(text)) choices.Add(new BloodPactId(text));
    }

    private void SyncBloodPacts(IReadOnlyList<BloodPactStack> pacts)
    {
        if (networkBloodPacts == null) return;
        networkBloodPacts.Clear();
        for (int i = 0; i < pacts.Count; i++)
            networkBloodPacts.Add(new NetworkBloodPactState(pacts[i].Id.Value, (ushort)Mathf.Clamp(pacts[i].Stacks, 0, ushort.MaxValue)));
    }

    private PlayerSnapshot ReadSnapshot()
    {
        if (runtime != null) return runtime.Snapshot;
        if (!UseNetworkValues) return offlineSnapshot;
        List<BloodPactStack> pacts = new(networkBloodPacts?.Count ?? 0);
        if (networkBloodPacts != null)
            for (int i = 0; i < networkBloodPacts.Count; i++)
                pacts.Add(new BloodPactStack(new BloodPactId(networkBloodPacts[i].PactId.ToString()), networkBloodPacts[i].Stacks));
        return new PlayerSnapshot(
            LogicalPlayerId,
            networkHealth.Value,
            networkMaxHealth.Value,
            networkAlive.Value,
            false,
            networkStamina.Value,
            networkMaxStamina.Value,
            Mathf.Max(0, Mathf.RoundToInt(networkScarlet.Value)),
            networkCoins.Value,
            1,
            0,
            commandSequence,
            false,
            0d,
            pacts);
    }

    private void PublishAll()
    {
        PlayerSnapshot state = ReadSnapshot();
        HealthChanged?.Invoke(state.CurrentHealth, state.MaxHealth);
        StaminaChanged?.Invoke(state.Stamina, state.MaxStamina);
        ScarletChanged?.Invoke(state.Scarlet, MaxScarlet);
        CoinsChanged?.Invoke(state.Coins);
        AliveChanged?.Invoke(state.IsAlive);
        BloodPactsChanged?.Invoke();
    }

    private void ApplyConfigToReplicationCache()
    {
        PlayerStatsConfig config = ResolveBaseStats();
        if (config == null) return;
        offlineSnapshot = new PlayerSnapshot(
            LogicalPlayerId,
            Mathf.Max(1, config.MaxHealth),
            Mathf.Max(1, config.MaxHealth),
            true,
            false,
            Mathf.Max(0f, config.MaxStamina),
            Mathf.Max(0f, config.MaxStamina),
            0,
            0,
            1,
            0,
            commandSequence,
            false,
            0d,
            offlineSnapshot.BloodPacts);

        if (!UseNetworkValues || !IsServer) return;
        networkMaxHealth.Value = offlineSnapshot.MaxHealth;
        networkHealth.Value = offlineSnapshot.CurrentHealth;
        networkMaxStamina.Value = offlineSnapshot.MaxStamina;
        networkStamina.Value = offlineSnapshot.Stamina;
        networkDashStaminaCost.Value = config.DashStaminaCost;
        networkStaminaRecovery.Value = config.StaminaRecoverSpeed;
        networkDamage.Value = config.BaseAttack;
        networkWeaponRange.Value = config.AttackRange;
        networkMoveSpeed.Value = config.MoveSpeed;
        networkDashSpeedMultiplier.Value = config.DashSpeedMultiplier;
        networkDashDuration.Value = config.DashDuration;
        networkAttackCooldown.Value = config.AttackInterval;
        networkKnockbackForce.Value = config.KnockbackForce;
        networkCritRate.Value = config.CritRate;
        networkCritDamage.Value = config.CritDamage;
        networkInvincibleTime.Value = config.InvincibleTime;
        networkFlashSpeed.Value = config.FlashSpeed;
        networkMaxScarlet.Value = config.MaxScarlet;
        networkScarlet.Value = 0f;
        networkCoins.Value = 0;
        networkAlive.Value = true;
        networkInitialized.Value = true;
    }

    private PlayerSnapshot BuildFallbackSnapshot() => new(
        LogicalPlayerId,
        Mathf.RoundToInt(BaseStatsValue(c => c.MaxHealth, 1)),
        Mathf.RoundToInt(BaseStatsValue(c => c.MaxHealth, 1)),
        true,
        false,
        BaseStatsValue(c => c.MaxStamina, 100f),
        BaseStatsValue(c => c.MaxStamina, 100f),
        0,
        0,
        1,
        0,
        0,
        false,
        0d,
        Array.Empty<BloodPactStack>());

    private EntityId ResolveLogicalPlayerId()
    {
        if (runtime != null && runtime.PlayerId.IsValid) return runtime.PlayerId;
        if (UseNetworkValues && EntityId.TryCreate(networkLogicalPlayerId.Value, out EntityId replicatedId))
            return replicatedId;
        if (logicalPlayerId != 0UL) return new EntityId(logicalPlayerId);
        ulong fallback = IsSpawned ? NetworkObjectId : CompatibilityIds.Allocate().Value;
        logicalPlayerId = fallback == 0UL ? CompatibilityIds.Allocate().Value : fallback;
        logicalPlayerIdWasGenerated = true;
        return new EntityId(logicalPlayerId);
    }

    private uint NextCommandSequence() => commandSequence == uint.MaxValue ? 1U : ++commandSequence;
    private PlayerStatsConfig ResolveBaseStats()
    {
        if (baseStats == null) baseStats = PlayerStatsConfig.LoadDefault();
        return baseStats;
    }

    private float BaseStatsValue(Func<PlayerStatsConfig, float> selector, float fallback)
    {
        PlayerStatsConfig config = ResolveBaseStats();
        return config == null ? fallback : selector(config);
    }

    private float Read(NetworkVariable<float> networkValue, float offlineValue) =>
        UseNetworkValues ? networkValue.Value : offlineValue;

    private int Read(NetworkVariable<int> networkValue, int offlineValue) =>
        UseNetworkValues ? networkValue.Value : offlineValue;

    private static WorldPosition ToWorldPosition(Vector3 value)
    {
        if (float.IsNaN(value.x) || float.IsInfinity(value.x) || float.IsNaN(value.y) ||
            float.IsInfinity(value.y) || float.IsNaN(value.z) || float.IsInfinity(value.z))
            return WorldPosition.Origin;
        return new WorldPosition(value.x, value.y, value.z);
    }

    private void SubscribeNetworkEvents()
    {
        if (networkEventsSubscribed) return;
        networkEventsSubscribed = true;
        networkHealth.OnValueChanged += HandleNetworkHealthChanged;
        networkMaxHealth.OnValueChanged += HandleNetworkMaxHealthChanged;
        networkStamina.OnValueChanged += HandleNetworkStaminaChanged;
        networkMaxStamina.OnValueChanged += HandleNetworkMaxStaminaChanged;
        networkScarlet.OnValueChanged += HandleNetworkScarletChanged;
        networkCoins.OnValueChanged += HandleNetworkCoinsChanged;
        networkAlive.OnValueChanged += HandleNetworkAliveChanged;
        networkBloodPactOfferVersion.OnValueChanged += HandleNetworkOfferVersionChanged;
        networkBloodPactOfferCost.OnValueChanged += HandleNetworkOfferCostChanged;
        networkBloodPactOfferChoice0.OnValueChanged += HandleNetworkOfferChoiceChanged;
        networkBloodPactOfferChoice1.OnValueChanged += HandleNetworkOfferChoiceChanged;
        networkBloodPactOfferChoice2.OnValueChanged += HandleNetworkOfferChoiceChanged;
    }

    private void UnsubscribeNetworkEvents()
    {
        if (!networkEventsSubscribed) return;
        networkEventsSubscribed = false;
        networkHealth.OnValueChanged -= HandleNetworkHealthChanged;
        networkMaxHealth.OnValueChanged -= HandleNetworkMaxHealthChanged;
        networkStamina.OnValueChanged -= HandleNetworkStaminaChanged;
        networkMaxStamina.OnValueChanged -= HandleNetworkMaxStaminaChanged;
        networkScarlet.OnValueChanged -= HandleNetworkScarletChanged;
        networkCoins.OnValueChanged -= HandleNetworkCoinsChanged;
        networkAlive.OnValueChanged -= HandleNetworkAliveChanged;
        networkBloodPactOfferVersion.OnValueChanged -= HandleNetworkOfferVersionChanged;
        networkBloodPactOfferCost.OnValueChanged -= HandleNetworkOfferCostChanged;
        networkBloodPactOfferChoice0.OnValueChanged -= HandleNetworkOfferChoiceChanged;
        networkBloodPactOfferChoice1.OnValueChanged -= HandleNetworkOfferChoiceChanged;
        networkBloodPactOfferChoice2.OnValueChanged -= HandleNetworkOfferChoiceChanged;
    }

    private void HandleNetworkHealthChanged(int previous, int current) => HealthChanged?.Invoke(current, MaxHealth);
    private void HandleNetworkMaxHealthChanged(int previous, int current) => HealthChanged?.Invoke(CurrentHealth, current);
    private void HandleNetworkStaminaChanged(float previous, float current) => StaminaChanged?.Invoke(current, MaxStamina);
    private void HandleNetworkMaxStaminaChanged(float previous, float current) => StaminaChanged?.Invoke(CurrentStamina, current);
    private void HandleNetworkScarletChanged(float previous, float current) => ScarletChanged?.Invoke(current, MaxScarlet);
    private void HandleNetworkCoinsChanged(int previous, int current) => CoinsChanged?.Invoke(current);
    private void HandleNetworkAliveChanged(bool previous, bool current) => AliveChanged?.Invoke(current);
    private void HandleNetworkOfferVersionChanged(uint previous, uint current) => HandleNetworkOfferChanged();
    private void HandleNetworkOfferCostChanged(int previous, int current) => HandleNetworkOfferChanged();
    private void HandleNetworkOfferChoiceChanged(FixedString64Bytes previous, FixedString64Bytes current) =>
        HandleNetworkOfferChanged();

    private void HandleNetworkOfferChanged()
    {
        if (runtime == null && TryReadNetworkOffer(out BloodPactOffer offer))
            currentOffer = offer;
        else if (runtime == null)
            currentOffer = default;
        BloodPactOfferChanged?.Invoke();
    }

    private void HandleBloodPactListChanged(NetworkListEvent<NetworkBloodPactState> change) => BloodPactsChanged?.Invoke();
}
