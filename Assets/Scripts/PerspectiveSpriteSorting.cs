using UnityEngine;
using VampireHunt.Bootstrap;

[RequireComponent(typeof(SpriteRenderer))]
public sealed class PerspectiveSpriteSorting : MonoBehaviour, IRuntimeCameraBinding
{
    [SerializeField] private Transform anchor;
    [SerializeField] private int sortingOffset;

    private SpriteRenderer spriteRenderer;
    private Camera viewCamera;

    /// <summary>
    /// Explicit camera injection used by the composition root. Keeping this
    /// setter public preserves compatibility with scene adapters without
    /// making a sprite query a global camera service.
    /// </summary>
    public void SetCamera(Camera camera)
    {
        viewCamera = camera;
    }

    public void BindCamera(Camera camera)
    {
        SetCamera(camera);
    }

    public void SetAnchor(Transform newAnchor)
    {
        anchor = newAnchor;
    }

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    private void LateUpdate()
    {
        if (spriteRenderer == null || viewCamera == null)
        {
            return;
        }

        Vector3 point = anchor != null ? anchor.position : transform.position;
        Vector3 screenPoint = viewCamera.WorldToScreenPoint(point);
        spriteRenderer.sortingOrder = Mathf.RoundToInt(-screenPoint.y * 10f) + sortingOffset;
    }
}
