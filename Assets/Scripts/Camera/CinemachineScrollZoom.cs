using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

[DisallowMultipleComponent]
[RequireComponent(typeof(CinemachineFollow))]
public sealed class CinemachineScrollZoom : MonoBehaviour
{
    [Header("位置范围 / Position Range")]
    [SerializeField] private Vector3 zoomedOutOffset = new(0f, 10.5f, -3.18f);
    [SerializeField] private Vector3 zoomedInOffset = new(0f, 3.25f, -4.75f);
    [SerializeField, Range(0f, 1f)] private float initialZoom = 0f;

    [Header("手感 / Feel")]
    [SerializeField, Min(0.0001f)] private float scrollSensitivity = 0.0025f;
    [SerializeField, Min(0f)] private float smoothSpeed = 14f;

    private CinemachineFollow follow;
    private float targetZoom;

    private void Awake()
    {
        follow = GetComponent<CinemachineFollow>();
        targetZoom = Mathf.Clamp01(initialZoom);
        ApplyZoomImmediate();
    }

    private void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (!Mathf.Approximately(scroll, 0f))
            {
                // Unity reports a positive value when scrolling up: zoom towards the player.
                targetZoom = Mathf.Clamp01(targetZoom + scroll * scrollSensitivity);
            }
        }

        Vector3 desiredOffset = Vector3.Lerp(zoomedOutOffset, zoomedInOffset, targetZoom);
        float blend = smoothSpeed <= 0f
            ? 1f
            : 1f - Mathf.Exp(-smoothSpeed * Time.unscaledDeltaTime);
        follow.FollowOffset = Vector3.Lerp(follow.FollowOffset, desiredOffset, blend);
    }

    private void OnValidate()
    {
        initialZoom = Mathf.Clamp01(initialZoom);
        scrollSensitivity = Mathf.Max(0.0001f, scrollSensitivity);
        smoothSpeed = Mathf.Max(0f, smoothSpeed);
    }

    [ContextMenu("Apply Initial Zoom")]
    private void ApplyZoomImmediate()
    {
        follow ??= GetComponent<CinemachineFollow>();
        if (follow != null)
        {
            follow.FollowOffset = Vector3.Lerp(zoomedOutOffset, zoomedInOffset, targetZoom);
        }
    }
}
