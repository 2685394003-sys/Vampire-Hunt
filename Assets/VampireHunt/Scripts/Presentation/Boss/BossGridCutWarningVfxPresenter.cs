using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace VampireHunt.Presentation.Boss
{
    /// <summary>
    /// Builds six horizontal and six vertical warning meshes. The meshes are real strips,
    /// not a procedural grid texture, and use the exact dimensions consumed by hit logic.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossGridCutWarningVfxPresenter : MonoBehaviour
    {
        [SerializeField] private Shader warningShader;
        [SerializeField] private Transform warningStripsRoot;
        [ColorUsage(true, true)] [SerializeField] private Color lineColor = new Color(3.4f, .008f, .018f, 1f);
        [ColorUsage(true, true)] [SerializeField] private Color hotColor = new Color(8f, .18f, .12f, 1f);
        [SerializeField] private float stripHeight = .025f;
        [SerializeField] private int sortingOrder = 22;

        private readonly List<Renderer> m_StripRenderers = new List<Renderer>(12);
        private Material m_Material;
        private double m_StartServerTime;
        private float m_FirstChargeDuration = .75f;
        private float m_RepeatInterval = .75f;
        private int m_Repetitions = 3;
        private bool m_Configured;

        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int LineColorId = Shader.PropertyToID("_LineColor");
        private static readonly int HotColorId = Shader.PropertyToID("_HotColor");

        private void Awake()
        {
            Shader shader = warningShader != null
                ? warningShader
                : Shader.Find("VampireHunt/Boss/GridCutWarning");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            m_Material = new Material(shader) { name = "VH_GridCut_Warning_Runtime" };
            if (m_Material.HasProperty(LineColorId)) m_Material.SetColor(LineColorId, lineColor);
            if (m_Material.HasProperty(HotColorId)) m_Material.SetColor(HotColorId, hotColor);

            if (warningStripsRoot == null)
            {
                Transform existing = transform.Find("WarningStrips_MeshMaterial_6x6");
                if (existing != null) warningStripsRoot = existing;
                else
                {
                    GameObject root = new GameObject("WarningStrips_MeshMaterial_6x6");
                    root.transform.SetParent(transform, false);
                    warningStripsRoot = root.transform;
                }
            }
        }

        public void Configure(
            double startServerTime,
            float firstChargeDuration,
            float repeatInterval,
            int repetitions,
            float halfExtent,
            int lineCount,
            float lineWidth)
        {
            m_StartServerTime = startServerTime;
            m_FirstChargeDuration = Mathf.Max(.01f, firstChargeDuration);
            m_RepeatInterval = Mathf.Max(.01f, repeatInterval);
            m_Repetitions = Mathf.Max(1, repetitions);
            BuildStrips(
                Mathf.Max(.05f, halfExtent),
                Mathf.Max(1, lineCount),
                Mathf.Max(.02f, lineWidth));
            m_Configured = true;
            UpdateProgress();
        }

        private void BuildStrips(float halfExtent, int lineCount, float lineWidth)
        {
            ClearStrips();
            float usableHalfExtent = Mathf.Max(0f, halfExtent - lineWidth * .5f);
            float fullLength = halfExtent * 2f;
            float safeHeight = Mathf.Max(.005f, stripHeight);

            for (int i = 0; i < lineCount; i++)
            {
                float t = lineCount == 1 ? .5f : i / (float)(lineCount - 1);
                float offset = Mathf.Lerp(-usableHalfExtent, usableHalfExtent, t);
                CreateStrip(
                    string.Format("HorizontalStrip_{0:00}_MeshMaterial", i + 1),
                    new Vector3(0f, 0f, offset),
                    new Vector3(fullLength, safeHeight, lineWidth));
                CreateStrip(
                    string.Format("VerticalStrip_{0:00}_MeshMaterial", i + 1),
                    new Vector3(offset, safeHeight * .55f, 0f),
                    new Vector3(lineWidth, safeHeight, fullLength));
            }
        }

        private void CreateStrip(string objectName, Vector3 localPosition, Vector3 localScale)
        {
            GameObject strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            strip.name = objectName;
            strip.transform.SetParent(warningStripsRoot, false);
            strip.transform.localPosition = localPosition;
            strip.transform.localScale = localScale;
            if (strip.TryGetComponent(out Collider collider))
            {
                collider.enabled = false;
                Destroy(collider);
            }

            Renderer renderer = strip.GetComponent<Renderer>();
            renderer.sharedMaterial = m_Material;
            renderer.sortingOrder = sortingOrder;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            m_StripRenderers.Add(renderer);
        }

        private void ClearStrips()
        {
            m_StripRenderers.Clear();
            if (warningStripsRoot == null) return;
            for (int i = warningStripsRoot.childCount - 1; i >= 0; i--)
                Destroy(warningStripsRoot.GetChild(i).gameObject);
        }

        private void Update()
        {
            if (m_Configured) UpdateProgress();
        }

        private void UpdateProgress()
        {
            double elapsed = ReadServerTime() - m_StartServerTime;
            if (elapsed < 0d)
            {
                SetProgress(0f);
                return;
            }

            double totalDuration = m_FirstChargeDuration + m_RepeatInterval * (m_Repetitions - 1);
            if (elapsed >= totalDuration)
            {
                Destroy(gameObject);
                return;
            }

            float progress;
            if (elapsed < m_FirstChargeDuration)
            {
                progress = (float)(elapsed / m_FirstChargeDuration);
            }
            else
            {
                double repeatedElapsed = elapsed - m_FirstChargeDuration;
                progress = (float)((repeatedElapsed % m_RepeatInterval) / m_RepeatInterval);
            }
            SetProgress(Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress)));
        }

        private void SetProgress(float value)
        {
            if (m_Material != null && m_Material.HasProperty(ProgressId))
                m_Material.SetFloat(ProgressId, value);
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
