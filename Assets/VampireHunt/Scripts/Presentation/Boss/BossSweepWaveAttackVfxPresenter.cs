using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using VampireHunt.Contracts;

namespace VampireHunt.Presentation.Boss
{
    /// <summary>Reveals one blood-arc texture across a rectangular ParticleSystem.</summary>
    [DisallowMultipleComponent]
    public sealed class BossSweepWaveAttackVfxPresenter : MonoBehaviour
    {
        [SerializeField] private Shader revealShader;
        [SerializeField] private Texture2D sweepTexture;
        [ColorUsage(true, true)] [SerializeField] private Color tint = new Color(3.2f, .018f, .055f, 1f);
        [SerializeField] private float intensity = 6f;
        [SerializeField] private float holdDuration = .12f;

        private ParticleSystem m_Particles;
        private Material m_Material;
        private double m_StartServerTime;
        private float m_RevealDuration = .28f;
        private bool m_Configured;

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int ReverseId = Shader.PropertyToID("_Reverse");

        private void Awake()
        {
            Shader shader = revealShader != null
                ? revealShader
                : Shader.Find("VampireHunt/Boss/SweepWaveRevealParticle");
            if (shader == null) shader = Shader.Find("VampireHunt/Boss/AdditiveVFX");
            m_Material = new Material(shader) { name = "VH_Sweep_Attack_Runtime" };
            if (m_Material.HasProperty(BaseMapId)) m_Material.SetTexture(BaseMapId, sweepTexture);
            if (m_Material.HasProperty(BaseColorId)) m_Material.SetColor(BaseColorId, tint);
            if (m_Material.HasProperty(IntensityId)) m_Material.SetFloat(IntensityId, intensity);

            GameObject particleObject = new GameObject("RectangularSweepParticle");
            particleObject.transform.SetParent(transform, false);
            m_Particles = particleObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = m_Particles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 2f;
            main.maxParticles = 1;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = 2f;
            main.startSpeed = 0f;
            main.startColor = Color.white;
            ParticleSystem.EmissionModule emission = m_Particles.emission;
            emission.enabled = false;

            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.SetActive(false);
            Mesh quadMesh = quad.GetComponent<MeshFilter>().sharedMesh;
            Destroy(quad);

            ParticleSystemRenderer renderer = particleObject.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.mesh = quadMesh;
            renderer.sharedMaterial = m_Material;
            renderer.sortingOrder = 25;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            m_Particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        public void Configure(
            double startServerTime,
            float revealDuration,
            BossSweepDirection direction,
            float areaWidth,
            float areaHeight)
        {
            m_StartServerTime = startServerTime;
            m_RevealDuration = Mathf.Max(.01f, revealDuration);
            if (m_Material != null && m_Material.HasProperty(ReverseId))
                m_Material.SetFloat(ReverseId, direction == BossSweepDirection.RightToLeft ? 1f : 0f);

            ParticleSystem.MainModule main = m_Particles.main;
            main.startLifetime = m_RevealDuration + Mathf.Max(0f, holdDuration) + .25f;
            main.startSize3D = true;
            main.startSizeX = Mathf.Max(3f, areaWidth * 1.35f);
            main.startSizeY = Mathf.Max(2.2f, areaHeight * 1.2f);
            main.startSizeZ = .1f;
            m_Particles.Emit(1);
            m_Configured = true;
            UpdateReveal();
        }

        private void Update()
        {
            if (m_Configured) UpdateReveal();
        }

        private void UpdateReveal()
        {
            double elapsed = ReadServerTime() - m_StartServerTime;
            float progress = Mathf.Clamp01((float)(elapsed / m_RevealDuration));
            if (m_Material != null && m_Material.HasProperty(ProgressId))
                m_Material.SetFloat(ProgressId, progress);
            if (elapsed >= m_RevealDuration + Mathf.Max(0f, holdDuration)) Destroy(gameObject);
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
