using UnityEngine;
using VampireHunt.Boss.Contracts;
using System.Collections.Generic;

// Explicit values preserve old AnimationEvent and serialized command values.
public enum BossAttackType
{
    Format1 = 1,
    Format2 = 2,
    Format3 = 3,
    Format4 = 4,
    Format6 = 6
}

/// <summary>
/// Legacy attack component reduced to an input/event adapter. Attack choice,
/// cooldowns, windows, target collection and damage all belong to BossRuntime.
/// </summary>
[DisallowMultipleComponent]
public sealed class BossAttackController : MonoBehaviour
{
    [SerializeField] private BossConfig stats;
    [SerializeField] private Transform player;
    [SerializeField] private Transform meleePoint;
    [SerializeField] private Transform projectileOrigin;
    [SerializeField] private Transform groundIndicator;
    [SerializeField] private Transform vfxRoot;
    [SerializeField] private Rigidbody bossRigidbody;
    [SerializeField] private Animator animator;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private BossGuard leftGuard;
    [SerializeField] private BossGuard rightGuard;

    public bool IsBusy { get; private set; }
    public BossAttackType? LastAttack { get; private set; }
    public string LastAttackName => LastAttack.HasValue ? $"Format{(int)LastAttack.Value}" : "无";
    public int AttacksStarted { get; private set; }
    public Transform MeleePoint => meleePoint;
    public Transform ProjectileOrigin => projectileOrigin;
    public Transform GroundIndicator => groundIndicator;
    public Transform VFXRoot => vfxRoot;

    public event System.Action<BossAttackType> AttackStarted;
    public event System.Action<BossAttackType> AttackCompleted;
    public event System.Action<BossAttackType> AttackCancelled;

    private BossController controller;

    private void Awake()
    {
        stats ??= GetComponent<BossConfig>();
        controller ??= GetComponentInParent<BossController>();
        bossRigidbody ??= GetComponent<Rigidbody>();
        animator ??= GetComponentInChildren<Animator>(true);
        audioSource ??= GetComponent<AudioSource>();
        meleePoint ??= transform.Find("MeleePoint");
        projectileOrigin ??= transform.Find("ProjectileOrigin");
        groundIndicator ??= transform.Find("GroundIndicator");
        vfxRoot ??= transform.Find("VFXRoot");
        FindGuards();
    }

    public void SetPlayer(Transform newPlayer) => player = newPlayer;

    public void ConfigureGuards(BossGuard newLeftGuard, BossGuard newRightGuard)
    {
        leftGuard = newLeftGuard;
        rightGuard = newRightGuard;
    }

    public void ConfigureMounts(
        Transform newMeleePoint,
        Transform newProjectileOrigin,
        Transform newGroundIndicator,
        Transform newVfxRoot,
        Animator newAnimator)
    {
        meleePoint = newMeleePoint;
        projectileOrigin = newProjectileOrigin;
        groundIndicator = newGroundIndicator;
        vfxRoot = newVfxRoot;
        if (newAnimator != null) animator = newAnimator;
    }

    /// <summary>
    /// Reports presentation mount omissions for editor/debug tooling. The
    /// result is informational; no server simulation path consumes it.
    /// </summary>
    public string GetMountConfigurationIssue()
    {
        List<string> missing = new();
        if (meleePoint == null) missing.Add("MeleePoint");
        if (projectileOrigin == null) missing.Add("ProjectileOrigin");
        if (groundIndicator == null) missing.Add("GroundIndicator");
        if (vfxRoot == null) missing.Add("VFXRoot");
        return missing.Count == 0
            ? string.Empty
            : $"以下 Boss 子节点未连接：{string.Join("、", missing)}。这些挂点仅影响表现，不会阻止 Boss 服务器模拟。";
    }

    public bool TryStartAttack(int phase, bool bossVisible, float playerDistance)
    {
        if (!NetworkAuthority.IsServerOrOffline()) return false;
        controller ??= GetComponentInParent<BossController>();
        return controller != null && controller.TryStartAuthoritativeAttack();
    }

    public bool DebugStartAttack(BossAttackType attackType)
    {
        if (!Application.isPlaying || !NetworkAuthority.IsServerOrOffline()) return false;
        return TryForceAttack(attackType, true);
    }

    public bool TryForceAttack(BossAttackType attackType, bool interruptCurrentAttack = false)
    {
        if (!NetworkAuthority.IsServerOrOffline() || !IsSupportedAttack(attackType)) return false;
        controller ??= GetComponentInParent<BossController>();
        return controller != null && controller.TryForceAuthoritativeAttack(attackType, interruptCurrentAttack);
    }

    [ContextMenu("调试/强制攻击 1（近战）")]
    private void DebugFormat1() => DebugStartAttack(BossAttackType.Format1);
    [ContextMenu("调试/强制攻击 2（弹幕）")]
    private void DebugFormat2() => DebugStartAttack(BossAttackType.Format2);
    [ContextMenu("调试/强制攻击 3（十字）")]
    private void DebugFormat3() => DebugStartAttack(BossAttackType.Format3);
    [ContextMenu("调试/强制攻击 4（全屏）")]
    private void DebugFormat4() => DebugStartAttack(BossAttackType.Format4);
    [ContextMenu("调试/强制攻击 6（冲刺）")]
    private void DebugFormat6() => DebugStartAttack(BossAttackType.Format6);

    public void CancelCurrentAttack()
    {
        controller ??= GetComponentInParent<BossController>();
        if (controller != null) controller.CancelAuthoritativeAttack();
        else IsBusy = false;
    }

    internal void NotifyStarted(BossAttackId attackId)
    {
        if (!TryMap(attackId, out BossAttackType legacy)) return;
        IsBusy = true;
        LastAttack = legacy;
        AttacksStarted++;
        AttackStarted?.Invoke(legacy);
    }

    internal void NotifyCompleted(BossAttackId attackId)
    {
        if (!TryMap(attackId, out BossAttackType legacy)) return;
        IsBusy = false;
        LastAttack = legacy;
        AttackCompleted?.Invoke(legacy);
    }

    internal void NotifyCancelled(BossAttackId attackId)
    {
        if (!TryMap(attackId, out BossAttackType legacy)) return;
        IsBusy = false;
        LastAttack = legacy;
        AttackCancelled?.Invoke(legacy);
    }

    internal void SyncSnapshot(VampireHunt.Boss.Contracts.BossSnapshot snapshot)
    {
        IsBusy = snapshot.CurrentAttack != BossAttackId.None;
        if (TryMap(snapshot.CurrentAttack, out BossAttackType legacy)) LastAttack = legacy;
    }

    private void FindGuards()
    {
        foreach (BossGuard guard in GetComponentsInChildren<BossGuard>(true))
        {
            if (guard == null) continue;
            if (guard.Side == BossGuardSide.Left) leftGuard ??= guard;
            else rightGuard ??= guard;
        }
    }

    private static bool IsSupportedAttack(BossAttackType attackType) =>
        attackType is BossAttackType.Format1 or BossAttackType.Format2 or BossAttackType.Format3 or
        BossAttackType.Format4 or BossAttackType.Format6;

    internal static BossAttackId ToDomain(BossAttackType attackType) => attackType switch
    {
        BossAttackType.Format1 => BossAttackId.GuardSweep,
        BossAttackType.Format2 => BossAttackId.RotatingBarrage,
        BossAttackType.Format3 => BossAttackId.CrossSlash,
        BossAttackType.Format4 => BossAttackId.ChargedSlash,
        BossAttackType.Format6 => BossAttackId.RectangleDash,
        _ => BossAttackId.None
    };

    private static bool TryMap(BossAttackId attackId, out BossAttackType attackType)
    {
        attackType = attackId switch
        {
            BossAttackId.GuardSweep => BossAttackType.Format1,
            BossAttackId.RotatingBarrage => BossAttackType.Format2,
            BossAttackId.CrossSlash => BossAttackType.Format3,
            BossAttackId.ChargedSlash => BossAttackType.Format4,
            BossAttackId.RectangleDash => BossAttackType.Format6,
            _ => default
        };
        return attackId != BossAttackId.None;
    }
}
