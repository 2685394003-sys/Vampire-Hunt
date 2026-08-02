using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class BossSpriteFacing : MonoBehaviour
{
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Transform movementRoot;
    [SerializeField] private bool faceCamera = true;
    [SerializeField] private bool invertHorizontal;
    [SerializeField, Min(0f)] private float movementThreshold = 0.001f;
    [SerializeField, Min(0f)] private float teleportIgnoreDistance = 2f;

    private Camera viewCamera;
    private Vector3 previousPosition;
    private bool hasPreviousPosition;

    private void OnEnable()
    {
        spriteRenderer ??= GetComponent<SpriteRenderer>();
        movementRoot ??= transform;
        viewCamera = Camera.main;
        ResetMovementSample();
        ApplyCameraFacing();
    }

    private void LateUpdate()
    {
        ApplyCameraFacing();

        if (!Application.isPlaying || movementRoot == null || spriteRenderer == null)
        {
            ResetMovementSample();
            return;
        }

        Vector3 currentPosition = movementRoot.position;
        if (!hasPreviousPosition)
        {
            previousPosition = currentPosition;
            hasPreviousPosition = true;
            return;
        }

        Vector3 movement = Vector3.ProjectOnPlane(currentPosition - previousPosition, Vector3.up);
        previousPosition = currentPosition;

        float distance = movement.magnitude;
        if (distance <= movementThreshold ||
            (teleportIgnoreDistance > 0f && distance >= teleportIgnoreDistance))
        {
            return;
        }

        viewCamera ??= Camera.main;
        Vector3 screenRight = viewCamera != null
            ? Vector3.ProjectOnPlane(viewCamera.transform.right, Vector3.up).normalized
            : Vector3.right;
        float horizontalMovement = Vector3.Dot(movement, screenRight);
        if (Mathf.Abs(horizontalMovement) <= movementThreshold)
        {
            return;
        }

        bool movingLeftOnScreen = horizontalMovement < 0f;
        spriteRenderer.flipX = invertHorizontal
            ? !movingLeftOnScreen
            : movingLeftOnScreen;
    }

    public void Configure(SpriteRenderer renderer, Transform root)
    {
        spriteRenderer = renderer;
        movementRoot = root != null ? root : transform;
        ResetMovementSample();
        ApplyCameraFacing();
    }

    private void ApplyCameraFacing()
    {
        if (!faceCamera)
        {
            return;
        }

        viewCamera ??= Camera.main;
        if (viewCamera != null)
        {
            transform.rotation = viewCamera.transform.rotation;
        }
    }

    private void ResetMovementSample()
    {
        if (movementRoot != null)
        {
            previousPosition = movementRoot.position;
            hasPreviousPosition = true;
        }
        else
        {
            hasPreviousPosition = false;
        }
    }

    private void OnValidate()
    {
        spriteRenderer ??= GetComponent<SpriteRenderer>();
        movementRoot ??= transform;
        movementThreshold = Mathf.Max(0f, movementThreshold);
        teleportIgnoreDistance = Mathf.Max(0f, teleportIgnoreDistance);
        ApplyCameraFacing();
    }
}
