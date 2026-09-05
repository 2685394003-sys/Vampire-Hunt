using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace VampireHunt.Presentation.Boss
{
    /// <summary>Moves one authored sword-qi particle, then dissolves it at the locked lane endpoint.</summary>
    [DisallowMultipleComponent]
    public sealed class BossChargeSlashSwordQiVfxPresenter : MonoBehaviour
    {
        [SerializeField] private Shader dissolveShader;
        [SerializeField] private Texture2D swordQiTexture;
        [ColorUsage(true, true)] [SerializeField] private Color tint = new Color(2.8f, .025f, .08f, 1f);
        [SerializeField] private float intensity = 5.5f;

        private ParticleSystem m_Particles;
        private Material m_Material;
        private Vector3 m_Start;
        private Vector3 m_End;
        private double m_TravelStartServerTime;
        private float m_TravelDuration = .45f;
        private float m_DissolveDuration = .3f;
        private bool m_Configured;

        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int DissolveId = Shader.PropertyToID("_Dissolve");

        private void Awake()
        {
            Shader shader = dissolveShader != null
                ? dissolveShader
                : Shader.Find("VampireHunt/Boss/SlashDissolveParticle");
            if (shader == null) shader = Shader.Find("VampireHunt/Boss/AdditiveVFX");
            m_Material = new Material(shader) { name = "VH_ChargeSlash_SwordQi_Runtime" };
            if (m_Material.HasProperty(BaseMapId)) m_Material.SetTexture(BaseMapId, swordQiTexture);
            if (m_Material.HasProperty(BaseColorId)) m_Material.SetColor(BaseColorId, tint);
            if (m_Material.HasProperty(IntensityId)) m_Material.SetFloat(IntensityId, intensity);
            if (m_Material.HasProperty(DissolveId)) m_Material.SetFloat(DissolveId, 0f);

            GameObject particleObject = new GameObject("SwordQiParticle");
            particleObject.transform.SetParent(transform, false);
            m_Particles = particleObject.AddComponent<ParticleSystem>();
            // A newly added ParticleSystem starts immediately with Unity's defaults.
            // Duration and other structural settings may only be changed while stopped.
            m_Particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = m_Particles.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 4f;
            main.maxParticles = 1;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = 4f;
            main.startSpeed = 0f;
            main.startColor = Color.white;
            ParticleSystem.EmissionModule emission = m_Particles.emission;
            emission.enabled = false;

            ParticleSystemRenderer renderer = particleObject.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.sharedMaterial = m_Material;
            renderer.sortingOrder = 24;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            m_Particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        public void Configure(
            Vector3 start,
            Vector3 end,
            double travelStartServerTime,
            float travelDuration,
            float dissolveDuration,
            float laneWidth,
            float laneHeight)
        {
            m_Start = start;
            m_End = end;
            m_TravelStartServerTime = travelStartServerTime;
            m_TravelDuration = Mathf.Max(.01f, travelDuration);
            m_DissolveDuration = Mathf.Max(.01f, dissolveDuration);
            transform.position = start;

            ParticleSystem.MainModule main = m_Particles.main;
            main.startLifetime = m_TravelDuration + m_DissolveDuration + .5f;
            main.startSize3D = true;
            main.startSizeX = Mathf.Max(3.5f, laneWidth * 1.75f);
            main.startSizeY = Mathf.Max(1.8f, laneHeight * 1.2f);
            main.startSizeZ = .1f;
            m_Particles.Emit(1);
            m_Configured = true;
            UpdateMotion();
        }

        private void Update()
        {
            if (m_Configured) UpdateMotion();
        }

        private void UpdateMotion()
        {
            double elapsed = ReadServerTime() - m_TravelStartServerTime;
            if (elapsed <= 0d)
            {
                transform.position = m_Start;
                return;
            }

            float travelProgress = Mathf.Clamp01((float)(elapsed / m_TravelDuration));
            transform.position = Vector3.LerpUnclamped(m_Start, m_End, EaseOutCubic(travelProgress));

            float dissolve = elapsed <= m_TravelDuration
                ? 0f
                : Mathf.Clamp01((float)((elapsed - m_TravelDuration) / m_DissolveDuration));
            if (m_Material != null && m_Material.HasProperty(DissolveId))
                m_Material.SetFloat(DissolveId, dissolve);

            if (dissolve >= 1f) Destroy(gameObject);
        }

        private static float EaseOutCubic(float value)
        {
            float inverse = 1f - value;
            return 1f - inverse * inverse * inverse;
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
