using UnityEngine;

/// <summary>
/// Presentation adapter for the default 3D enemy. Gameplay state stays in
/// <see cref="FlowFieldEnemy"/>; this component only translates that state to
/// the parameters used by the temporary player Animator Controller.
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyAnimationController : MonoBehaviour
{
    private static readonly int MoveParameter = Animator.StringToHash("move");
    private static readonly int AttackParameter = Animator.StringToHash("Attack");

    [SerializeField] private Animator animator;
    [SerializeField, Min(0f)] private float movingBlendValue = 5f;
    [SerializeField, Min(0f)] private float moveDampTime = 0.1f;

    private bool hasMoveParameter;
    private bool hasAttackParameter;

    public Animator Animator => animator;

    private void Awake()
    {
        ResolveAnimator();
        CacheParameters();
    }

    private void OnValidate()
    {
        movingBlendValue = Mathf.Max(0f, movingBlendValue);
        moveDampTime = Mathf.Max(0f, moveDampTime);
        ResolveAnimator();
        CacheParameters();
    }

    public void Configure(Animator targetAnimator)
    {
        animator = targetAnimator;
        if (animator != null)
            animator.applyRootMotion = false;
        CacheParameters();
    }

    public void ApplyState(EnemyState state, bool immediate = false)
    {
        if (animator == null)
        {
            ResolveAnimator();
            CacheParameters();
        }
        if (animator == null)
            return;

        float move = state == EnemyState.isChasing ? movingBlendValue : 0f;
        if (hasMoveParameter)
        {
            if (immediate || moveDampTime <= 0f)
                animator.SetFloat(MoveParameter, move);
            else
                animator.SetFloat(MoveParameter, move, moveDampTime, Time.deltaTime);
        }

        if (hasAttackParameter)
            animator.SetBool(AttackParameter, state == EnemyState.isAttacking);
    }

    private void ResolveAnimator()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);
        if (animator != null)
            animator.applyRootMotion = false;
    }

    private void CacheParameters()
    {
        hasMoveParameter = false;
        hasAttackParameter = false;
        if (animator == null || animator.runtimeAnimatorController == null)
            return;

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.nameHash == MoveParameter &&
                parameter.type == AnimatorControllerParameterType.Float)
            {
                hasMoveParameter = true;
            }
            else if (parameter.nameHash == AttackParameter &&
                     parameter.type == AnimatorControllerParameterType.Bool)
            {
                hasAttackParameter = true;
            }
        }
    }
}
