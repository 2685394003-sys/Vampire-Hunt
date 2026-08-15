using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Boss encounter lifecycle states exposed to gameplay callers.
/// </summary>
public enum BossState
{
    Dormant,
    OffscreenIdle,
    Chase,
    Attack,
    PhaseChange,
    Dead
}

/// <summary>
/// Stable command result shared by Player, NPC, UI and scripted encounters.
/// Callers should branch on this value instead of inspecting Boss internals.
/// </summary>
public enum BossCommandResult
{
    Succeeded,
    InvalidArgument,
    NotReady,
    NotPlaying,
    NotAuthority,
    CombatDisabled,
    TargetMissing,
    Busy,
    Invulnerable,
    Dead,
    Rejected
}

/// <summary>
/// Read-only point-in-time data for UI, AI and encounter scripting.
/// </summary>
public readonly struct BossSnapshot
{
    public EntityId EntityId { get; }
    public BossState State { get; }
    public bool CombatEnabled { get; }
    public int CurrentHealth { get; }
    public int MaxHealth { get; }
    public int Phase { get; }
    public bool IsInvulnerable { get; }
    public bool IsDead { get; }
    public float RemainingContractSeconds { get; }
    public Vector3 Position { get; }
    public BossAttackType? LastAttack { get; }

    public float HealthNormalized => MaxHealth > 0
        ? (float)CurrentHealth / MaxHealth
        : 0f;

    public BossSnapshot(
        EntityId entityId,
        BossState state,
        bool combatEnabled,
        int currentHealth,
        int maxHealth,
        int phase,
        bool isInvulnerable,
        bool isDead,
        float remainingContractSeconds,
        Vector3 position,
        BossAttackType? lastAttack)
    {
        EntityId = entityId;
        State = state;
        CombatEnabled = combatEnabled;
        CurrentHealth = currentHealth;
        MaxHealth = maxHealth;
        Phase = phase;
        IsInvulnerable = isInvulnerable;
        IsDead = isDead;
        RemainingContractSeconds = remainingContractSeconds;
        Position = position;
        LastAttack = lastAttack;
    }
}

/// <summary>
/// Public Boss facade. External gameplay code should depend on this interface,
/// not concrete movement, attack, presentation or health implementations.
/// </summary>
public interface IBossController
{
    Transform ActorTransform { get; }
    Transform Target { get; }
    BossState State { get; }
    bool CombatEnabled { get; }
    BossHealth Health { get; }
    BossAttackController Attacks { get; }
    BossSnapshot Snapshot { get; }

    event Action<IBossController, BossState, BossState> StateChanged;
    event Action<IBossController, Transform> TargetChanged;
    event Action<IBossController, bool> CombatEnabledChanged;
    event Action<IBossController, BossSnapshot> SnapshotChanged;
    event Action<IBossController, BossAttackType> AttackStarted;
    event Action<IBossController, BossAttackType> AttackCompleted;
    event Action<IBossController, BossAttackType> AttackCancelled;
    event Action<IBossController> Defeated;

    BossCommandResult AssignTarget(Transform target);
    BossCommandResult ClearTarget();
    BossCommandResult StartCombat();
    BossCommandResult StopCombat();
    BossCommandResult ApplyDamage(int amount, Vector3 damageSource);
    BossCommandResult SetInvulnerable(bool value);
    BossCommandResult TryForceAttack(BossAttackType attackType);
}

/// <summary>
/// Runtime registry for decoupled access from Player, NPC, UI and encounter code.
/// Supports multiple simultaneous bosses and never exposes its mutable storage.
/// </summary>
public static class BossRegistry
{
    private static readonly List<IBossController> Controllers = new();

    public static event Action<IBossController> Registered;
    public static event Action<IBossController> Unregistered;

    public static int Count
    {
        get
        {
            PruneDestroyed();
            return Controllers.Count;
        }
    }

    /// <summary>
    /// Returns the first live, non-defeated Boss registered in the current runtime.
    /// </summary>
    public static bool TryGetPrimary(out IBossController boss)
    {
        PruneDestroyed();
        for (int i = 0; i < Controllers.Count; i++)
        {
            IBossController candidate = Controllers[i];
            if (!candidate.Snapshot.IsDead)
            {
                boss = candidate;
                return true;
            }
        }

        boss = null;
        return false;
    }

    /// <summary>
    /// Returns the nearest live, non-defeated Boss to a world-space position.
    /// </summary>
    public static bool TryGetClosest(Vector3 worldPosition, out IBossController boss)
    {
        PruneDestroyed();
        boss = null;
        float bestDistanceSquared = float.PositiveInfinity;

        for (int i = 0; i < Controllers.Count; i++)
        {
            IBossController candidate = Controllers[i];
            if (candidate.Snapshot.IsDead || candidate.ActorTransform == null)
            {
                continue;
            }

            float distanceSquared =
                (candidate.ActorTransform.position - worldPosition).sqrMagnitude;
            if (distanceSquared >= bestDistanceSquared)
            {
                continue;
            }

            bestDistanceSquared = distanceSquared;
            boss = candidate;
        }

        return boss != null;
    }

    /// <summary>
    /// Copies all live controllers into a caller-owned buffer without allocation.
    /// </summary>
    public static int GetActive(List<IBossController> buffer)
    {
        if (buffer == null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }

        PruneDestroyed();
        buffer.Clear();
        buffer.AddRange(Controllers);
        return buffer.Count;
    }

    internal static void Register(IBossController controller)
    {
        if (!IsAlive(controller) || Controllers.Contains(controller))
        {
            return;
        }

        Controllers.Add(controller);
        Registered?.Invoke(controller);
    }

    internal static void Unregister(IBossController controller)
    {
        if (controller == null || !Controllers.Remove(controller))
        {
            return;
        }

        Unregistered?.Invoke(controller);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        Controllers.Clear();
        Registered = null;
        Unregistered = null;
    }

    private static void PruneDestroyed()
    {
        for (int i = Controllers.Count - 1; i >= 0; i--)
        {
            if (!IsAlive(Controllers[i]))
            {
                Controllers.RemoveAt(i);
            }
        }
    }

    private static bool IsAlive(IBossController controller)
    {
        if (controller == null)
        {
            return false;
        }

        return controller is not UnityEngine.Object unityObject || unityObject != null;
    }
}
