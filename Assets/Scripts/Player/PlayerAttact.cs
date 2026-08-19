using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Compatibility attack presenter/input adapter kept on the shipped prefab.
/// Hit queries, critical rolls, damage application and attack results now live
/// in PlayerCombatService/CombatApplicationService.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerNetworkState))]
public sealed class PlayerAttact : MonoBehaviour
{
    private static readonly int AttackParameter = Animator.StringToHash("Attack");

    // These fields and names are intentionally retained for existing Prefab and
    // AnimationEvent bindings. Runtime damage must never be decided from them.
    public Animator anim;
    public Transform AttackPoint;

    [Header("服务器判定 / Server Hit Timing")]
    [SerializeField, Min(0f)] private float hitDelay = 0.12f;
    [SerializeField, Min(0.01f)] private float attackAnimationDuration = 1.25f;

    [Header("白色剑气 / White Sword Slash VFX")]
    [SerializeField, Min(0.01f)] private float swordSlashDuration = 0.32f;
    [SerializeField, Min(0f)] private float swordSlashHeight = 0.8f;
    [SerializeField, Range(0.05f, 0.5f)] private float swordSlashThicknessRatio = 0.22f;
    [SerializeField, Min(0.01f)] private float swordSlashMinThickness = 0.35f;
    [SerializeField, ColorUsage(true, true)] private Color swordSlashColor = Color.white;

    private PlayerController playerController;
    private Coroutine attackResetCoroutine;

    public bool IsAttackAnimationPlaying { get; private set; }

    private void Awake()
    {
        playerController = GetComponent<PlayerController>();
    }

    /// <summary>Legacy input/UnityEvent entry point; submits attack intent only.</summary>
    [Obsolete("Use PlayerController.RequestAttack or IPlayerCommandGateway.")]
    public void Attack() => playerController?.RequestAttack();

    /// <summary>
    /// Legacy server hook retained for old controller code. It only updates the
    /// local presentation state; authoritative hit resolution is elsewhere.
    /// </summary>
    [Obsolete("Attack execution moved to PlayerCombatService.")]
    public void ServerBeginAttack(int sequence)
    {
        if (sequence <= 0) return;
        SetAttackState(true);
        RestartAttackResetTimer();
    }

    public void PlayAttackPresentation()
    {
        SetAttackState(true);
        RestartAttackResetTimer();
    }

    /// <summary>Old typo is serialized in AnimationEvents; keep it as a presenter hook.</summary>
    [Obsolete("Animation events may only update presentation; damage is server-confirmed by Combat.")]
    public void Attackfalse() => SetAttackState(false);

    /// <summary>
    /// AnimationEvent compatibility entry. It submits an intent and cannot
    /// directly query colliders or mutate an enemy/player health value.
    /// </summary>
    [Obsolete("Animation events cannot apply damage; submit an attack intent instead.")]
    public void DealDamage() => playerController?.SubmitAttackIntentFromAnimation();

    private void SetAttackState(bool attacking)
    {
        IsAttackAnimationPlaying = attacking;
        if (anim != null) anim.SetBool(AttackParameter, attacking);
    }

    private void RestartAttackResetTimer()
    {
        if (attackResetCoroutine != null) StopCoroutine(attackResetCoroutine);
        attackResetCoroutine = StartCoroutine(ResetAttackAfterDelay());
    }

    private IEnumerator ResetAttackAfterDelay()
    {
        yield return new WaitForSeconds(Mathf.Max(0.01f, attackAnimationDuration));
        SetAttackState(false);
        attackResetCoroutine = null;
    }
}
