using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public sealed class PerspectiveSpriteSorting : MonoBehaviour
{
    [SerializeField] private Transform anchor;
    [SerializeField] private int sortingOffset;

    private SpriteRenderer spriteRenderer;
    private Camera viewCamera;

    public void SetAnchor(Transform newAnchor)
    {
        anchor = newAnchor;
    }

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        viewCamera = Camera.main;
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
