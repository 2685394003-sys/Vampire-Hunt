using UnityEngine;

/// <summary>
/// Compatibility shell for the original 2D chaser.  Target selection and
/// contact/attack rules are now owned by Enemies Application/Domain.
/// </summary>
public sealed class ChasingEnemy : MonoBehaviour
{
    [SerializeField] private float moveSpeed = 2.2f;
    [SerializeField] private float contactRange = 1.15f;
    [SerializeField] private float contactCooldown = 0.7f;

    private FlowFieldEnemy flowFieldEnemy;

    private void Awake()
    {
        flowFieldEnemy = GetComponent<FlowFieldEnemy>();
        if (flowFieldEnemy == null)
            flowFieldEnemy = GetComponentInParent<FlowFieldEnemy>();
    }

    public void SetTarget(Transform newTarget)
    {
        // Retained as an AnimationEvent/authoring entry point. The composed
        // query, not this compatibility method, owns the authoritative target.
    }

    public void ReceiveHit()
    {
        // Hit notifications are consumed by CombatApplicationService ports.
    }
}
