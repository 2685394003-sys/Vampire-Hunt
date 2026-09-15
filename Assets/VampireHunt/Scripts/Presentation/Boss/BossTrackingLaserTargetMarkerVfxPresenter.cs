using UnityEngine;
using UnityEngine.Rendering;

namespace VampireHunt.Presentation.Boss
{
    /// <summary>Runtime-built red triangle that floats above and points down at the locked player.</summary>
    [DisallowMultipleComponent]
    public sealed class BossTrackingLaserTargetMarkerVfxPresenter : MonoBehaviour
    {
        [SerializeField] private Shader markerShader;
        [ColorUsage(true, true)] [SerializeField] private Color markerColor = new Color(4.5f, .01f, .025f, 1f);
        [SerializeField] private float bobHeight = .12f;
        [SerializeField] private float bobSpeed = 4.5f;
        [SerializeField] private int sortingOrder = 80;

        private Transform m_Target;
        private Vector3 m_Offset = Vector3.up * 2.4f;
        private Material m_Material;

        private static readonly int ColorId = Shader.PropertyToID("_MarkerColor");

        private void Awake()
        {
            Shader shader = markerShader != null
                ? markerShader
                : Shader.Find("VampireHunt/Boss/TrackingLaserTargetMarker");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            m_Material = new Material(shader) { name = "VH_Laser_TargetMarker_Runtime" };
            if (m_Material.HasProperty(ColorId)) m_Material.SetColor(ColorId, markerColor);

            var mesh = new Mesh { name = "VH_Laser_DownwardTriangle" };
            mesh.vertices = new[]
            {
                new Vector3(-.38f, .35f, 0f),
                new Vector3(.38f, .35f, 0f),
                new Vector3(0f, -.4f, 0f)
            };
            mesh.uv = new[] { new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(.5f, 0f) };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.RecalculateBounds();

            MeshFilter filter = gameObject.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = m_Material;
            renderer.sortingOrder = sortingOrder;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        public void Configure(Transform target, Vector3 worldOffset)
        {
            m_Target = target;
            m_Offset = worldOffset;
            UpdatePose();
        }

        public void SetTarget(Transform target)
        {
            if (target != null) m_Target = target;
        }

        private void LateUpdate() => UpdatePose();

        private void UpdatePose()
        {
            if (m_Target == null) return;
            transform.position = m_Target.position + m_Offset +
                                 Vector3.up * (Mathf.Sin(Time.time * bobSpeed) * bobHeight);
            Camera camera = Camera.main;
            if (camera != null)
            {
                Vector3 towardCamera = camera.transform.position - transform.position;
                if (towardCamera.sqrMagnitude > .0001f)
                    transform.rotation = Quaternion.LookRotation(-towardCamera.normalized, Vector3.up);
            }
        }

        private void OnDestroy()
        {
            MeshFilter filter = GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null) Destroy(filter.sharedMesh);
            if (m_Material != null) Destroy(m_Material);
        }
    }
}
