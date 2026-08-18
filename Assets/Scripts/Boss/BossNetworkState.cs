using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Replicates the durable, semantic state of the Boss encounter.
/// BossController remains the server-authoritative gameplay coordinator; this
/// component only mirrors its public events to clients and restores late joins.
/// </summary>
[DefaultExecutionOrder(-40)]
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(BossController))]
[RequireComponent(typeof(BossHealth))]
public sealed class BossNetworkState : NetworkBehaviour
{
    private const float GuardPoseSendInterval = 1f / 15f;
    private const int NoAttack = 0;

    private static readonly NetworkObjectReference NullTarget =
        new((NetworkObject)null);

    private readonly NetworkVariable<BossState> networkState = ServerVariable(BossState.Dormant);
    private readonly NetworkVariable<bool> networkCombatEnabled = ServerVariable(false);
    private readonly NetworkVariable<BossEncounterMode> networkEncounterMode =
        ServerVariable(BossEncounterMode.Hunt);
    private readonly NetworkVariable<BossStaggerState> networkStaggerState =
        ServerVariable(BossStaggerState.None);
    private readonly NetworkVariable<NetworkObjectReference> networkTarget = new(
        NullTarget,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> networkActiveAttack = ServerVariable(NoAttack);
    private readonly NetworkVariable<int> networkLastAttack = ServerVariable(NoAttack);
    private readonly NetworkVariable<int> networkAttackSequence = ServerVariable(0);
    private readonly NetworkVariable<double> networkAttackStartTime = ServerVariable(0d);
    private readonly NetworkVariable<int> networkAttackLifecycleCommand = ServerVariable(0);
    private readonly NetworkVariable<double> networkContractEndTime = ServerVariable(0d);
    private readonly NetworkVariable<int> networkLeftGuardHealth = ServerVariable(0);
    private readonly NetworkVariable<int> networkRightGuardHealth = ServerVariable(0);

    private NetworkList<BossBloodPoolNetworkEntry> networkBloodPools;
    private readonly Dictionary<int, GameObject> localBloodPools = new();
    private readonly HashSet<int> activeBloodPoolIds = new();

    private BossController controller;
    private BossConfig config;
    private BossGuard leftGuard;
    private BossGuard rightGuard;
    private bool serverEventsBound;
    private float nextGuardPoseSendTime;
    private float nextTargetResolveTime;
    private int nextBloodPoolId;
    private NetworkObjectReference lastAppliedTarget = NullTarget;
    private bool hasAppliedTarget;

    public int AttackSequence => networkAttackSequence.Value;
    public double AttackStartServerTime => networkAttackStartTime.Value;
    public BossAttackType? ActiveAttack => DecodeAttack(networkActiveAttack.Value);

    private static NetworkVariable<T> ServerVariable<T>(T value) where T : unmanaged =>
        new(value, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private void Awake()
    {
        ResolveReferences();
        networkBloodPools = new NetworkList<BossBloodPoolNetworkEntry>(
            null,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);
    }

    public override void OnNetworkSpawn()
    {
        SubscribeNetworkEvents();
        networkBloodPools.OnListChanged += HandleBloodPoolListChanged;

        leftGuard?.SetNetworkReplicaMode(!IsServer);
        rightGuard?.SetNetworkReplicaMode(!IsServer);

        if (IsServer)
        {
            BindServerEvents();
            CaptureServerState();
        }
        else
        {
            ApplyAllReplicatedState();
            RebuildBloodPoolPresentation();
        }
    }

    public override void OnNetworkDespawn()
    {
        UnbindServerEvents();
        UnsubscribeNetworkEvents();
        if (networkBloodPools != null)
            networkBloodPools.OnListChanged -= HandleBloodPoolListChanged;
        ClearLocalBloodPools();
    }

    private void Update()
    {
        if (!IsSpawned || !NetworkAuthority.IsNetworkActive)
            return;

        if (IsServer)
        {
            if (Time.unscaledTime >= nextGuardPoseSendTime)
            {
                nextGuardPoseSendTime = Time.unscaledTime + GuardPoseSendInterval;
                BroadcastGuardPose();
            }
            return;
        }

        if (Time.unscaledTime >= nextTargetResolveTime)
        {
            nextTargetResolveTime = Time.unscaledTime + 0.25f;
            ApplyReplicatedTarget();
        }

        ApplyReplicatedContractClock();
    }

    /// <summary>Registers a persistent server-owned pool for late-join presentation.</summary>
    public int ServerRegisterBloodPool(Vector3 position)
    {
        if (!IsSpawned || !IsServer || networkBloodPools == null)
            return 0;

        int id = ++nextBloodPoolId;
        networkBloodPools.Add(new BossBloodPoolNetworkEntry(id, position));
        return id;
    }

    public void ServerUnregisterBloodPool(int id)
    {
        if (!IsSpawned || !IsServer || id <= 0 || networkBloodPools == null)
            return;

        for (int index = networkBloodPools.Count - 1; index >= 0; index--)
        {
            if (networkBloodPools[index].Id == id)
            {
                networkBloodPools.RemoveAt(index);
                return;
            }
        }
    }

    private void ResolveReferences()
    {
        controller ??= GetComponent<BossController>();
        config ??= GetComponent<BossConfig>();

        BossGuard[] guards = GetComponentsInChildren<BossGuard>(true);
        foreach (BossGuard guard in guards)
        {
            if (guard == null) continue;
            if (guard.Side == BossGuardSide.Left) leftGuard ??= guard;
            else rightGuard ??= guard;
        }
    }

    private void CaptureServerState()
    {
        if (!IsServer || controller == null) return;

        networkState.Value = controller.State;
        networkCombatEnabled.Value = controller.CombatEnabled;
        networkEncounterMode.Value = controller.EncounterMode;
        networkStaggerState.Value = controller.StaggerState;
        networkTarget.Value = CreateTargetReference(controller.Target);
        networkLeftGuardHealth.Value = leftGuard != null ? leftGuard.CurrentHealth : 0;
        networkRightGuardHealth.Value = rightGuard != null ? rightGuard.CurrentHealth : 0;

        BossAttackType? lastAttack = controller.Attacks != null
            ? controller.Attacks.LastAttack
            : null;
        networkLastAttack.Value = EncodeAttack(lastAttack);

        if (controller.ContractCountdownActive && controller.RemainingContractSeconds > 0f)
            StartContractClock(controller.RemainingContractSeconds);
    }

    private void BindServerEvents()
    {
        if (serverEventsBound || controller == null || !IsServer) return;

        controller.StateChanged += HandleServerStateChanged;
        controller.TargetChanged += HandleServerTargetChanged;
        controller.CombatEnabledChanged += HandleServerCombatChanged;
        controller.EncounterModeChanged += HandleServerEncounterModeChanged;
        controller.StaggerStateChanged += HandleServerStaggerChanged;
        controller.AttackStarted += HandleServerAttackStarted;
        controller.AttackCompleted += HandleServerAttackCompleted;
        controller.AttackCancelled += HandleServerAttackCancelled;
        controller.ContractCountdownChanged += HandleServerContractChanged;
        controller.ContractCountdownExpired += HandleServerContractExpired;
        controller.Defeated += HandleServerDefeated;

        if (leftGuard != null) leftGuard.HealthChanged += HandleServerGuardHealthChanged;
        if (rightGuard != null) rightGuard.HealthChanged += HandleServerGuardHealthChanged;
        serverEventsBound = true;
    }

    private void UnbindServerEvents()
    {
        if (!serverEventsBound || controller == null) return;

        controller.StateChanged -= HandleServerStateChanged;
        controller.TargetChanged -= HandleServerTargetChanged;
        controller.CombatEnabledChanged -= HandleServerCombatChanged;
        controller.EncounterModeChanged -= HandleServerEncounterModeChanged;
        controller.StaggerStateChanged -= HandleServerStaggerChanged;
        controller.AttackStarted -= HandleServerAttackStarted;
        controller.AttackCompleted -= HandleServerAttackCompleted;
        controller.AttackCancelled -= HandleServerAttackCancelled;
        controller.ContractCountdownChanged -= HandleServerContractChanged;
        controller.ContractCountdownExpired -= HandleServerContractExpired;
        controller.Defeated -= HandleServerDefeated;

        if (leftGuard != null) leftGuard.HealthChanged -= HandleServerGuardHealthChanged;
        if (rightGuard != null) rightGuard.HealthChanged -= HandleServerGuardHealthChanged;
        serverEventsBound = false;
    }

    private void SubscribeNetworkEvents()
    {
        networkState.OnValueChanged += HandleReplicatedCoreChanged;
        networkCombatEnabled.OnValueChanged += HandleReplicatedCoreChanged;
        networkEncounterMode.OnValueChanged += HandleReplicatedCoreChanged;
        networkStaggerState.OnValueChanged += HandleReplicatedCoreChanged;
        networkTarget.OnValueChanged += HandleReplicatedTargetChanged;
        networkActiveAttack.OnValueChanged += HandleReplicatedCoreChanged;
        networkLastAttack.OnValueChanged += HandleReplicatedCoreChanged;
        networkAttackLifecycleCommand.OnValueChanged += HandleReplicatedAttackLifecycle;
        networkContractEndTime.OnValueChanged += HandleReplicatedContractChanged;
        networkLeftGuardHealth.OnValueChanged += HandleReplicatedGuardHealthChanged;
        networkRightGuardHealth.OnValueChanged += HandleReplicatedGuardHealthChanged;
    }

    private void UnsubscribeNetworkEvents()
    {
        networkState.OnValueChanged -= HandleReplicatedCoreChanged;
        networkCombatEnabled.OnValueChanged -= HandleReplicatedCoreChanged;
        networkEncounterMode.OnValueChanged -= HandleReplicatedCoreChanged;
        networkStaggerState.OnValueChanged -= HandleReplicatedCoreChanged;
        networkTarget.OnValueChanged -= HandleReplicatedTargetChanged;
        networkActiveAttack.OnValueChanged -= HandleReplicatedCoreChanged;
        networkLastAttack.OnValueChanged -= HandleReplicatedCoreChanged;
        networkAttackLifecycleCommand.OnValueChanged -= HandleReplicatedAttackLifecycle;
        networkContractEndTime.OnValueChanged -= HandleReplicatedContractChanged;
        networkLeftGuardHealth.OnValueChanged -= HandleReplicatedGuardHealthChanged;
        networkRightGuardHealth.OnValueChanged -= HandleReplicatedGuardHealthChanged;
    }

    private void HandleServerStateChanged(IBossController boss, BossState previous, BossState current) =>
        networkState.Value = current;

    private void HandleServerTargetChanged(IBossController boss, Transform target) =>
        networkTarget.Value = CreateTargetReference(target);

    private void HandleServerCombatChanged(IBossController boss, bool enabled) =>
        networkCombatEnabled.Value = enabled;

    private void HandleServerEncounterModeChanged(
        IBossController boss,
        BossEncounterMode previous,
        BossEncounterMode current) => networkEncounterMode.Value = current;

    private void HandleServerStaggerChanged(
        IBossController boss,
        BossStaggerState previous,
        BossStaggerState current) => networkStaggerState.Value = current;

    private void HandleServerAttackStarted(IBossController boss, BossAttackType attack)
    {
        int sequence = networkAttackSequence.Value + 1;
        networkAttackSequence.Value = sequence;
        networkActiveAttack.Value = (int)attack;
        networkLastAttack.Value = (int)attack;
        networkAttackStartTime.Value = ServerNow;
        networkAttackLifecycleCommand.Value = EncodeLifecycle(sequence, attack, BossAttackLifecycle.Started);
    }

    private void HandleServerAttackCompleted(IBossController boss, BossAttackType attack)
    {
        networkActiveAttack.Value = NoAttack;
        networkAttackLifecycleCommand.Value = EncodeLifecycle(
            networkAttackSequence.Value,
            attack,
            BossAttackLifecycle.Completed);
    }

    private void HandleServerAttackCancelled(IBossController boss, BossAttackType attack)
    {
        networkActiveAttack.Value = NoAttack;
        networkAttackLifecycleCommand.Value = EncodeLifecycle(
            networkAttackSequence.Value,
            attack,
            BossAttackLifecycle.Cancelled);
    }

    private void HandleServerContractChanged(float remainingSeconds)
    {
        if (controller == null || !controller.ContractCountdownActive || remainingSeconds <= 0f)
            return;

        if (networkContractEndTime.Value <= ServerNow)
            StartContractClock(remainingSeconds);
    }

    private void HandleServerContractExpired() => networkContractEndTime.Value = 0d;

    private void HandleServerDefeated(IBossController boss)
    {
        networkContractEndTime.Value = 0d;
        networkActiveAttack.Value = NoAttack;
        networkLeftGuardHealth.Value = 0;
        networkRightGuardHealth.Value = 0;
    }

    private void HandleServerGuardHealthChanged(BossGuard guard, int current, int max)
    {
        if (guard == null) return;
        if (guard.Side == BossGuardSide.Left) networkLeftGuardHealth.Value = current;
        else networkRightGuardHealth.Value = current;
    }

    private void StartContractClock(float remainingSeconds)
    {
        float rate = config != null ? Mathf.Max(0.0001f, config.format5CountdownRate) : 1f;
        networkContractEndTime.Value = ServerNow + remainingSeconds / rate;
    }

    private void HandleReplicatedCoreChanged<T>(T previous, T current)
    {
        if (!IsServer) ApplyReplicatedCoreState();
    }

    private void HandleReplicatedTargetChanged(
        NetworkObjectReference previous,
        NetworkObjectReference current)
    {
        if (!IsServer)
        {
            lastAppliedTarget = NullTarget;
            hasAppliedTarget = false;
            ApplyReplicatedTarget();
        }
    }

    private void HandleReplicatedAttackLifecycle(int previous, int current)
    {
        if (IsServer || controller == null || current == 0) return;
        DecodeLifecycle(current, out BossAttackType attack, out BossAttackLifecycle lifecycle);
        controller.ApplyReplicatedAttackLifecycle(attack, lifecycle);
    }

    private void HandleReplicatedContractChanged(double previous, double current)
    {
        if (!IsServer) ApplyReplicatedContractClock();
    }

    private void HandleReplicatedGuardHealthChanged(int previous, int current)
    {
        if (!IsServer) ApplyReplicatedGuardState();
    }

    private void ApplyAllReplicatedState()
    {
        ApplyReplicatedCoreState();
        ApplyReplicatedTarget();
        ApplyReplicatedContractClock();
        ApplyReplicatedGuardState();
    }

    private void ApplyReplicatedCoreState()
    {
        if (controller == null) return;
        controller.ApplyReplicatedCoreState(
            networkState.Value,
            networkCombatEnabled.Value,
            networkEncounterMode.Value,
            networkStaggerState.Value,
            DecodeAttack(networkActiveAttack.Value),
            DecodeAttack(networkLastAttack.Value));
    }

    private void ApplyReplicatedTarget()
    {
        if (controller == null ||
            (hasAppliedTarget && networkTarget.Value.Equals(lastAppliedTarget))) return;

        if (networkTarget.Value.NetworkObjectId == ulong.MaxValue)
        {
            lastAppliedTarget = networkTarget.Value;
            hasAppliedTarget = true;
            controller.ApplyReplicatedTarget(null);
            return;
        }

        if (networkTarget.Value.TryGet(out NetworkObject targetObject))
        {
            lastAppliedTarget = networkTarget.Value;
            hasAppliedTarget = true;
            controller.ApplyReplicatedTarget(targetObject.transform);
        }
    }

    private void ApplyReplicatedContractClock()
    {
        if (controller == null) return;
        double endTime = networkContractEndTime.Value;
        float rate = config != null ? Mathf.Max(0.0001f, config.format5CountdownRate) : 1f;
        float remaining = endTime > 0d
            ? Mathf.Max(0f, (float)((endTime - ServerNow) * rate))
            : 0f;
        controller.ApplyReplicatedContractClock(remaining, endTime > ServerNow);
    }

    private void ApplyReplicatedGuardState()
    {
        bool bossDead = networkState.Value == BossState.Dead;
        leftGuard?.ApplyReplicatedHealth(networkLeftGuardHealth.Value, bossDead);
        rightGuard?.ApplyReplicatedHealth(networkRightGuardHealth.Value, bossDead);
    }

    private void BroadcastGuardPose()
    {
        if (!IsServer || leftGuard == null || rightGuard == null) return;
        SyncGuardPoseRpc(
            leftGuard.transform.position,
            leftGuard.transform.rotation,
            rightGuard.transform.position,
            rightGuard.transform.rotation);
    }

    [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Unreliable)]
    private void SyncGuardPoseRpc(
        Vector3 leftPosition,
        Quaternion leftRotation,
        Vector3 rightPosition,
        Quaternion rightRotation)
    {
        if (IsServer) return;
        leftGuard?.ApplyReplicatedPose(leftPosition, leftRotation);
        rightGuard?.ApplyReplicatedPose(rightPosition, rightRotation);
    }

    private void HandleBloodPoolListChanged(NetworkListEvent<BossBloodPoolNetworkEntry> change) =>
        RebuildBloodPoolPresentation();

    private void RebuildBloodPoolPresentation()
    {
        if (IsServer || networkBloodPools == null) return;

        activeBloodPoolIds.Clear();
        for (int index = 0; index < networkBloodPools.Count; index++)
        {
            BossBloodPoolNetworkEntry entry = networkBloodPools[index];
            activeBloodPoolIds.Add(entry.Id);
            if (localBloodPools.ContainsKey(entry.Id)) continue;

            GameObject poolObject = new($"Boss_Phase_BloodPool_Client_{entry.Id}");
            BossPhaseBloodPool pool = poolObject.AddComponent<BossPhaseBloodPool>();
            pool.InitializeVisual(config, entry.Position);
            localBloodPools.Add(entry.Id, poolObject);
        }

        List<int> removed = null;
        foreach (KeyValuePair<int, GameObject> pair in localBloodPools)
        {
            if (activeBloodPoolIds.Contains(pair.Key)) continue;
            if (pair.Value != null) Destroy(pair.Value);
            removed ??= new List<int>();
            removed.Add(pair.Key);
        }

        if (removed == null) return;
        foreach (int id in removed) localBloodPools.Remove(id);
    }

    private void ClearLocalBloodPools()
    {
        foreach (GameObject pool in localBloodPools.Values)
            if (pool != null) Destroy(pool);
        localBloodPools.Clear();
    }

    private NetworkObjectReference CreateTargetReference(Transform target)
    {
        NetworkObject targetObject = target != null
            ? target.GetComponentInParent<NetworkObject>()
            : null;
        return targetObject != null && targetObject.IsSpawned
            ? new NetworkObjectReference(targetObject)
            : NullTarget;
    }

    private double ServerNow => NetworkManager != null ? NetworkManager.ServerTime.Time : 0d;

    private static int EncodeAttack(BossAttackType? attack) => attack.HasValue ? (int)attack.Value : NoAttack;

    private static BossAttackType? DecodeAttack(int value) =>
        Enum.IsDefined(typeof(BossAttackType), value) ? (BossAttackType)value : null;

    private static int EncodeLifecycle(
        int sequence,
        BossAttackType attack,
        BossAttackLifecycle lifecycle) =>
        (sequence << 8) | (((int)lifecycle & 0x0F) << 4) | ((int)attack & 0x0F);

    private static void DecodeLifecycle(
        int command,
        out BossAttackType attack,
        out BossAttackLifecycle lifecycle)
    {
        attack = (BossAttackType)(command & 0x0F);
        lifecycle = (BossAttackLifecycle)((command >> 4) & 0x0F);
    }
}

public enum BossAttackLifecycle : byte
{
    Started = 1,
    Completed = 2,
    Cancelled = 3
}

public struct BossBloodPoolNetworkEntry : INetworkSerializable, IEquatable<BossBloodPoolNetworkEntry>
{
    public int Id;
    public Vector3 Position;

    public BossBloodPoolNetworkEntry(int id, Vector3 position)
    {
        Id = id;
        Position = position;
    }

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref Id);
        serializer.SerializeValue(ref Position);
    }

    public bool Equals(BossBloodPoolNetworkEntry other) =>
        Id == other.Id && Position == other.Position;
}
