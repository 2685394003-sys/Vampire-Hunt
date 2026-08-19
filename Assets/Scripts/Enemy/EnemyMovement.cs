using UnityEngine;

/// <summary>
/// Legacy 2D movement shell.  The authoritative AI/movement intent is owned
/// by FlowFieldEnemy/EnemyRuntimeController; this component only preserves the
/// old detection-point and animation-event entry points used by ranged assets.
/// </summary>
public class EnemyMovement : MonoBehaviour
{
    public Transform EnemyDetectionPonint;

    private FlowFieldEnemy flowFieldEnemy;

    private void Awake()
    {
        flowFieldEnemy = GetComponent<FlowFieldEnemy>();
        if (flowFieldEnemy == null)
            flowFieldEnemy = GetComponentInParent<FlowFieldEnemy>();
    }

    public void ChangeState(EnemyState state)
    {
        flowFieldEnemy?.ChangeState(state);
    }

    public Transform GetPlayerTarget() => flowFieldEnemy?.CurrentTarget;

    public void SetTarget(Transform target)
    {
        // FlowFieldEnemy resolves the target through the server-side query on
        // its next simulation step. This method remains a no-op bridge for
        // old animation/setup code and never writes domain state directly.
    }
}
