using UnityEngine;
using VampireHunt.Bootstrap;

/// <summary>
/// Keeps the 2.5D presentation on one editable visual baseline:
/// a perspective camera at 27°/45° and sprite cards parallel to that view.
/// Attach this component to the scene's Main Camera.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class CameraSpriteViewLock : MonoBehaviour, IRuntimeCameraBinding
{
    [Header("相机(局部于相机装备) / Camera (local to its Camera Rig)")]
    [SerializeField] private Vector3 cameraLocalPosition = new(-12.285717f, 9.652815f, -12.285717f);
    [SerializeField] private Vector3 cameraLocalEulerAngles = new(27f, 45f, 0f);
    [SerializeField] private float fieldOfView = 35f;
    [SerializeField] private float nearClipPlane = 0.3f;
    [SerializeField] private float farClipPlane = 100f;

    [Header("精灵卡片 / Sprite Cards")]
    [SerializeField] private Vector3 spriteWorldEulerAngles = new(27f, 45f, 0f);
    [SerializeField] private bool applyEveryFrame = true;

    [Tooltip("Visual roots that are authored by their owning presentation adapter and must not be rotated by this baseline.")]
    [SerializeField] private Transform[] excludedVisualRoots;

    [Tooltip("Optional layer mask for visual roots that own their own orientation.")]
    [SerializeField] private LayerMask excludedVisualLayers;

    private Camera cachedCamera;
    private bool cameraBindingWasExplicit;

    public void BindCamera(Camera camera)
    {
        cameraBindingWasExplicit = true;
        cachedCamera = camera;
        if (isActiveAndEnabled)
        {
            ApplyViewBaseline();
        }
    }

    private void OnEnable() => ApplyViewBaseline();

    private void OnValidate() => ApplyViewBaseline();

    private void LateUpdate()
    {
        if (applyEveryFrame)
        {
            ApplyViewBaseline();
        }
    }

    [ContextMenu("Apply 2.5D View Baseline")]
    public void ApplyViewBaseline()
    {
        if (!cameraBindingWasExplicit)
        {
            cachedCamera ??= GetComponent<Camera>();
        }
        if (cachedCamera == null)
        {
            return;
        }

        transform.localPosition = cameraLocalPosition;
        transform.localRotation = Quaternion.Euler(cameraLocalEulerAngles);
        cachedCamera.orthographic = false;
        cachedCamera.fieldOfView = fieldOfView;
        cachedCamera.nearClipPlane = nearClipPlane;
        cachedCamera.farClipPlane = farClipPlane;

        Quaternion spriteRotation = Quaternion.Euler(spriteWorldEulerAngles);
        foreach (SpriteRenderer sprite in Object.FindObjectsByType<SpriteRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            // Gameplay roots own movement, colliders and/or the camera rig. Their
            // visual must live on a child Sprite Layer, never be rotated here.
            // Ownership is expressed by a visual root/layer binding rather than
            // a concrete Player/Enemy MonoBehaviour dependency.
            if (ShouldSkipSprite(sprite))
            {
                continue;
            }

            sprite.transform.rotation = spriteRotation;
        }
    }

    private bool ShouldSkipSprite(SpriteRenderer sprite)
    {
        if (sprite == null || !sprite.gameObject.scene.IsValid() || sprite.gameObject.name == "Ground")
        {
            return true;
        }

        int layerBit = 1 << sprite.gameObject.layer;
        if ((excludedVisualLayers.value & layerBit) != 0)
        {
            return true;
        }

        if (excludedVisualRoots == null)
        {
            return false;
        }

        for (int index = 0; index < excludedVisualRoots.Length; index++)
        {
            Transform root = excludedVisualRoots[index];
            if (root != null && (sprite.transform == root || sprite.transform.IsChildOf(root)))
            {
                return true;
            }
        }

        return false;
    }
}
