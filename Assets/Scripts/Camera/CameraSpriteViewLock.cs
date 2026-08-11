using UnityEngine;

/// <summary>
/// Keeps the 2.5D presentation on one editable visual baseline:
/// a perspective camera at 27°/45° and sprite cards parallel to that view.
/// Attach this component to the scene's Main Camera.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class CameraSpriteViewLock : MonoBehaviour
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

    private Camera cachedCamera;

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
        cachedCamera ??= GetComponent<Camera>();
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
            if (!sprite.gameObject.scene.IsValid() ||
                sprite.gameObject.name == "Ground" ||
                sprite.TryGetComponent<PlayerController>(out _) ||
                sprite.TryGetComponent<ChasingEnemy>(out _))
            {
                continue;
            }

            sprite.transform.rotation = spriteRotation;
        }
    }
}
