using UnityEngine;
using UnityEngine.Rendering;

namespace VampireHunt.Presentation.Boss
{
    /// <summary>Runtime-built fixed-size luminous cuboid driven by the tracking presenter.</summary>
    [DisallowMultipleComponent]
    public sealed class BossTrackingLaserBeamVfxPresenter : MonoBehaviour
    {
        [SerializeField] private Shader beamShader;
        [ColorUsage(true, true)] [SerializeField] private Color outerColor = new Color(3.6f, .008f, .025f, .72f);
        [ColorUsage(true, true)] [SerializeField] private Color coreColor = new Color(8f, .3f, .34f, 1f);

        private Material m_Material;
        private Transform m_Beam;
        private Vector3 m_Size = new Vector3(1.2f, 2f, 16f);

        private static readonly int OuterColorId = Shader.PropertyToID("_OuterColor");
        private static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");

        private void Awake()
        {
            Shader shader = beamShader != null
                ? beamShader
                : Shader.Find("VampireHunt/Boss/TrackingLaserBeam");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            m_Material = new Material(shader) { name = "VH_Laser_Beam_Runtime" };
            if (m_Material.HasProperty(OuterColorId)) m_Material.SetColor(OuterColorId, outerColor);
            if (m_Material.HasProperty(CoreColorId)) m_Material.SetColor(CoreColorId, coreColor);

            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "FixedTrackingLaserCuboid";
            cube.transform.SetParent(transform, false);
            if (cube.TryGetComponent(out Collider collider)) Destroy(collider);
            Renderer renderer = cube.GetComponent<Renderer>();
            renderer.sharedMaterial = m_Material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            m_Beam = cube.transform;
        }

        public void Configure(Vector3 size)
        {
            m_Size = new Vector3(
                Mathf.Max(.01f, size.x),
                Mathf.Max(.01f, size.y),
                Mathf.Max(.01f, size.z));
            if (m_Beam != null) m_Beam.localScale = m_Size;
        }

        public void SetPose(Vector3 origin, Vector3 direction)
        {
            Vector3 planar = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (planar.sqrMagnitude <= .0001f) planar = Vector3.forward;
            planar.Normalize();
            transform.SetPositionAndRotation(
                origin + planar * (m_Size.z * .5f),
                Quaternion.LookRotation(planar, Vector3.up));
            if (m_Beam != null) m_Beam.localScale = m_Size;
        }

        private void OnDestroy()
        {
            if (m_Material != null) Destroy(m_Material);
        }
    }
}
