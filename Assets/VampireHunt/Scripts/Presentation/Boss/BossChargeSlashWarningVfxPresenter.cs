using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace VampireHunt.Presentation.Boss
{
    /// <summary>Client-only fixed rectangle whose red intensity follows the server telegraph clock.</summary>
    [DisallowMultipleComponent]
    public sealed class BossChargeSlashWarningVfxPresenter : MonoBehaviour
    {
        [SerializeField] private Shader warningShader;
        [ColorUsage(true, true)] [SerializeField] private Color fillColor = new Color(1.6f, 0.01f, 0.025f, 1f);
        [ColorUsage(true, true)] [SerializeField] private Color edgeColor = new Color(4f, 0.08f, 0.04f, 1f);
        [SerializeField] private int sortingOrder = 18;

        private Material m_Material;
        private double m_StartServerTime;
        private float m_Duration = 1f;
        private bool m_Configured;

        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int FillColorId = Shader.PropertyToID("_FillColor");
        private static readonly int EdgeColorId = Shader.PropertyToID("_EdgeColor");

        private void Awake()
        {
            Shader shader = warningShader != null
                ? warningShader
                : Shader.Find("VampireHunt/Boss/ChargeSlashTelegraph");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");

            m_Material = new Material(shader) { name = "VH_ChargeSlash_Warning_Runtime" };
            if (m_Material.HasProperty(FillColorId)) m_Material.SetColor(FillColorId, fillColor);
            if (m_Material.HasProperty(EdgeColorId)) m_Material.SetColor(EdgeColorId, edgeColor);
            if (m_Material.HasProperty(ProgressId)) m_Material.SetFloat(ProgressId, 0f);

            GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Quad);
            plane.name = "FixedRectangleTelegraph";
            plane.transform.SetParent(transform, false);
            plane.transform.localPosition = Vector3.zero;
            plane.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            plane.transform.localScale = Vector3.one;
            if (plane.TryGetComponent(out Collider collider)) Destroy(collider);

            Renderer renderer = plane.GetComponent<Renderer>();
            renderer.sharedMaterial = m_Material;
            renderer.sortingOrder = sortingOrder;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        public void Configure(double startServerTime, float duration)
        {
            m_StartServerTime = startServerTime;
            m_Duration = Mathf.Max(.01f, duration);
            m_Configured = true;
            UpdateProgress();
        }

        private void Update()
        {
            if (m_Configured) UpdateProgress();
        }

        private void UpdateProgress()
        {
            float progress = Mathf.Clamp01((float)((ReadServerTime() - m_StartServerTime) / m_Duration));
            if (m_Material != null && m_Material.HasProperty(ProgressId))
                m_Material.SetFloat(ProgressId, progress);
        }

        private static double ReadServerTime()
        {
            NetworkManager manager = NetworkManager.Singleton;
            return manager != null && manager.IsListening
                ? manager.ServerTime.Time
                : Time.unscaledTimeAsDouble;
        }

        private void OnDestroy()
        {
            if (m_Material != null) Destroy(m_Material);
        }
    }
}
