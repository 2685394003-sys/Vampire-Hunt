using UnityEngine;

/// <summary>
/// Relays yin2 root translation to the server-authoritative player root while
/// keeping attack root yaw on the visual child. This prevents attack animation
/// rotation from fighting the replicated gameplay-facing direction.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public sealed class PlayerRootMotionDriver : MonoBehaviour
{
    [SerializeField, Min(0f)] private float attackVisualTurnSpeed = 600f;
    [SerializeField, Min(0f)] private float visualReturnSpeed = 360f;

    private Animator animator;
    private PlayerController controller;
    private PlayerAttact playerAttack;
    private Quaternion baseLocalRotation;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        controller = GetComponentInParent<PlayerController>();
        playerAttack = GetComponentInParent<PlayerAttact>();
        baseLocalRotation = transform.localRotation;
    }

    private void OnAnimatorMove()
    {
        if (animator == null)
        {
            return;
        }

        if (controller == null)
        {
            controller = GetComponentInParent<PlayerController>();
        }

        ApplyVisualAttackRotation();
        controller?.ApplyAnimatorRootMotion(animator.deltaPosition);
    }

    private void ApplyVisualAttackRotation()
    {
        bool isAttacking = playerAttack != null && playerAttack.IsAttackAnimationPlaying;
        if (isAttacking && !animator.IsInTransition(0))
        {
            Vector3 rotatedForward = Vector3.ProjectOnPlane(
                animator.deltaRotation * Vector3.forward,
                Vector3.up);
            if (rotatedForward.sqrMagnitude <= 0.000001f)
            {
                return;
            }

            float yawDelta = Vector3.SignedAngle(
                Vector3.forward,
                rotatedForward.normalized,
                Vector3.up);
            float maxYawDelta = attackVisualTurnSpeed * Time.fixedDeltaTime;
            if (maxYawDelta > 0f)
            {
                yawDelta = Mathf.Clamp(yawDelta, -maxYawDelta, maxYawDelta);
            }

            transform.localRotation *= Quaternion.AngleAxis(yawDelta, Vector3.up);
            return;
        }

        float returnStep = visualReturnSpeed * Time.fixedDeltaTime;
        transform.localRotation = returnStep <= 0f
            ? baseLocalRotation
            : Quaternion.RotateTowards(transform.localRotation, baseLocalRotation, returnStep);
    }
}
