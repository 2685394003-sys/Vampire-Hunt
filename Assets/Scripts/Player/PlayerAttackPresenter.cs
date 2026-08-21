using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Scripting.APIUpdating;

/// <summary>
/// Player attack presentation adapter kept on the shipped prefab.
///
/// Attack commands, hit queries and damage resolution belong to Player
/// Application/Combat. Animation events on this component only mark visual
/// phases; they never submit a command or mutate gameplay state.
/// </summary>
[DisallowMultipleComponent]
[MovedFrom(true, "", "Assembly-CSharp", "PlayerAttact")]
public sealed class PlayerAttackPresenter : MonoBehaviour
{
    private static readonly int AttackParameter = Animator.StringToHash("Attack");

    [FormerlySerializedAs("anim")]
    [SerializeField] private Animator _animator;

    [Header("动画表现 / Animation Presentation")]
    [FormerlySerializedAs("attackAnimationDuration")]
    [SerializeField, Min(0.01f)] private float _attackAnimationDuration = 1.25f;

    private Coroutine _attackResetCoroutine;

    public bool IsAttackAnimationPlaying { get; private set; }

    private void Awake()
    {
        if (_animator == null) _animator = GetComponentInChildren<Animator>();
    }

    public void PlayAttackPresentation()
    {
        SetAttackState(true);
        RestartAttackResetTimer();
    }

    /// <summary>
    /// AnimationEvent marker for the authored attack impact frame. The
    /// authoritative attack has already been accepted and resolved by Player
    /// Application; this callback intentionally has no gameplay side effect.
    /// </summary>
    public void OnAttackImpact()
    {
        if (!IsAttackAnimationPlaying) SetAttackState(true);
    }

    /// <summary>AnimationEvent marker for the end of the authored attack.</summary>
    public void OnAttackAnimationCompleted()
    {
        if (_attackResetCoroutine != null) StopCoroutine(_attackResetCoroutine);
        _attackResetCoroutine = null;
        SetAttackState(false);
    }

    private void SetAttackState(bool attacking)
    {
        IsAttackAnimationPlaying = attacking;
        if (_animator != null) _animator.SetBool(AttackParameter, attacking);
    }

    private void RestartAttackResetTimer()
    {
        if (_attackResetCoroutine != null) StopCoroutine(_attackResetCoroutine);
        _attackResetCoroutine = StartCoroutine(ResetAttackAfterDelay());
    }

    private IEnumerator ResetAttackAfterDelay()
    {
        yield return new WaitForSeconds(Mathf.Max(0.01f, _attackAnimationDuration));
        SetAttackState(false);
        _attackResetCoroutine = null;
    }
}
