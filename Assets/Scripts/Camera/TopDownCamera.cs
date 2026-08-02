using UnityEngine;

public sealed class TopDownCamera : MonoBehaviour
{
    [SerializeField] private Transform target;

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }

    private void LateUpdate()
    {
        // The camera is an editable child of Player/Camera Rig, so following is
        // handled by the scene hierarchy rather than overriding inspector values.
    }
}
