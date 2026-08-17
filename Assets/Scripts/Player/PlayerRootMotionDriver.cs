using UnityEngine;

/// <summary>
/// Relays yin2 root translation to the server-authoritative player root. Root
/// yaw is intentionally ignored because strafe facing is controlled by aim.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public sealed class PlayerRootMotionDriver : MonoBehaviour
{
    private Animator animator;
    private PlayerController controller;
    private Quaternion baseLocalRotation;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        controller = GetComponentInParent<PlayerController>();
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

        transform.localRotation = baseLocalRotation;
        controller?.ApplyAnimatorRootMotion(animator.deltaPosition);
    }
}
