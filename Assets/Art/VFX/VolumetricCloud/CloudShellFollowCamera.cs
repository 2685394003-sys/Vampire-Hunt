using UnityEngine;

public sealed class CloudShellFollowCamera : MonoBehaviour
{
    [SerializeField] private Transform target;

    private void Reset()
    {
        if (Camera.main != null)
            target = Camera.main.transform;
    }

    private void LateUpdate()
    {
        if (target == null && Camera.main != null)
            target = Camera.main.transform;

        if (target != null)
            transform.position = target.position;
    }
}