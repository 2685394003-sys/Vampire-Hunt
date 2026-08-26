using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using VampireHunt.Contracts;

namespace VampireHunt.Presentation.Boss
{
    /// <summary>Fills one fixed ground rectangle as a directional red wave.</summary>
    [DisallowMultipleComponent]
    public sealed class BossSweepWaveWarningVfxPresenter : MonoBehaviour
    {
        [SerializeField] private Shader warningShader;
        [ColorUsage(true, true)] [SerializeField] private Color fillColor = new Color(1.7f, .008f, .02f, 1f);
        [ColorUsage(true, true)] [SerializeField] private Color waveColor = new Color(4.5f, .06f, .025f, 1f);
        [SerializeField] private int sortingOrder = 19;

        private Material m_Material;
        private double m_StartServerTime;
        private float m_Duration = .45f;
        private bool m_Configured;

        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int ReverseId = Shader.PropertyToID("_Reverse");
        private static readonly int FillColorId = Shader.PropertyToID("_FillColor");
        private static readonly int WaveColorId = Shader.PropertyToID("_WaveColor");

        private void Awake()
        {
            Shader shader = warningShader != null
                ? warningShader
                : Shader.Find("VampireHunt/Boss/SweepWaveTelegraph");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            m_Material = new Material(shader) { name = "VH_Sweep_Warning_Runtime" };
            if (m_Material.HasProperty(FillColorId)) m_Material.SetColor(FillColorId, fillColor);
            if (m_Material.HasProperty(WaveColorId)) m_Material.SetColor(WaveColorId, waveColor);

            GameObject plane = GameObject.CreatePrimitive(PrimitiveType.Quad);
            plane.name = "DirectionalWaveRectangle";
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

        public void Configure(double startServerTime, float duration, BossSweepDirection direction)
        {
            m_StartServerTime = startServerTime;
            m_Duration = Mathf.Max(.01f, duration);
            if (m_Material != null && m_Material.HasProperty(ReverseId))
                m_Material.SetFloat(ReverseId, direction == BossSweepDirection.RightToLeft ? 1f : 0f);
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
