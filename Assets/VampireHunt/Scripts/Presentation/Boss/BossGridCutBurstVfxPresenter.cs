using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

namespace VampireHunt.Presentation.Boss
{
    /// <summary>
    /// Composite Grid Cut VFX: twelve slash meshes, a Shuriken spark layer, a short point
    /// light flash, and this component as the timeline/lifetime controller.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossGridCutBurstVfxPresenter : MonoBehaviour
    {
        [SerializeField] private Shader burstShader;
        [SerializeField] private Transform damageStripsRoot;
        [SerializeField] private ParticleSystem impactSparks;
        [SerializeField] private Light impactLight;
        [ColorUsage(true, true)] [SerializeField] private Color lineColor = new Color(7f, .025f, .035f, 1f);
        [ColorUsage(true, true)] [SerializeField] private Color coreColor = new Color(12f, 1.2f, .85f, 1f);
        [SerializeField] private float pulseDuration = .26f;
        [SerializeField] private float stripHeight = .04f;
        [SerializeField] private float flashIntensity = 8f;
        [SerializeField] private int sparksPerStrip = 7;
        [SerializeField] private int sortingOrder = 35;

        private readonly List<Renderer> m_StripRenderers = new List<Renderer>(12);
        private Material m_Material;
        private Material m_ParticleMaterial;
        private double m_FirstHitServerTime;
        private float m_Interval = .75f;
        private int m_Repetitions = 3;
        private int m_EmittedPulses;
        private float m_HalfExtent;
        private float m_LineWidth;
        private int m_LineCount;
        private bool m_Configured;

        private static readonly int ProgressId = Shader.PropertyToID("_PulseProgress");
        private static readonly int LineColorId = Shader.PropertyToID("_LineColor");
        private static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");

        private void Awake()
        {
            Shader shader = burstShader != null
                ? burstShader
                : Shader.Find("VampireHunt/Boss/GridCutBurst");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            m_Material = new Material(shader) { name = "VH_GridCut_Burst_Runtime" };
            if (m_Material.HasProperty(LineColorId)) m_Material.SetColor(LineColorId, lineColor);
            if (m_Material.HasProperty(CoreColorId)) m_Material.SetColor(CoreColorId, coreColor);

            EnsureCompositeChildren();
            ConfigureParticles();
        }

        public void Configure(
            double firstHitServerTime,
            float interval,
            int repetitions,
            float halfExtent,
            int lineCount,
            float lineWidth)
        {
            m_FirstHitServerTime = firstHitServerTime;
            m_Interval = Mathf.Max(.01f, interval);
            m_Repetitions = Mathf.Max(1, repetitions);
            m_EmittedPulses = 0;
            m_HalfExtent = Mathf.Max(.05f, halfExtent);
            m_LineCount = Mathf.Max(1, lineCount);
            m_LineWidth = Mathf.Max(.02f, lineWidth);
            BuildStrips();
            if (impactLight != null)
            {
                impactLight.range = Mathf.Max(4f, m_HalfExtent);
                impactLight.intensity = 0f;
            }

            // Late-joining clients skip expired pulses rather than replaying them together.
            double now = ReadServerTime();
            if (now >= m_FirstHitServerTime)
            {
                m_EmittedPulses = Mathf.Min(
                    m_Repetitions,
                    Mathf.FloorToInt((float)((now - m_FirstHitServerTime) / m_Interval)) + 1);
                double latestHit = m_FirstHitServerTime + (m_EmittedPulses - 1) * m_Interval;
                if (now - latestHit <= pulseDuration) EmitCompositeBurst();
            }

            m_Configured = true;
            UpdateBurst();
        }

        private void EnsureCompositeChildren()
        {
            if (damageStripsRoot == null)
            {
                Transform existing = transform.Find("DamageStrips_MeshMaterial_6x6");
                if (existing != null) damageStripsRoot = existing;
                else
                {
                    GameObject root = new GameObject("DamageStrips_MeshMaterial_6x6");
                    root.transform.SetParent(transform, false);
                    damageStripsRoot = root.transform;
                }
            }

            if (impactSparks == null)
            {
                GameObject particleObject = new GameObject("GridCutBloodSparks_ParticleSystem");
                particleObject.transform.SetParent(transform, false);
                impactSparks = particleObject.AddComponent<ParticleSystem>();
            }

            if (impactLight == null)
            {
                GameObject lightObject = new GameObject("GridCutImpactFlash_PointLight");
                lightObject.transform.SetParent(transform, false);
                lightObject.transform.localPosition = Vector3.up * .6f;
                impactLight = lightObject.AddComponent<Light>();
                impactLight.type = LightType.Point;
                impactLight.color = new Color(1f, .015f, .01f, 1f);
                impactLight.shadows = LightShadows.None;
                impactLight.intensity = 0f;
            }
        }

        private void ConfigureParticles()
        {
            impactSparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = impactSparks.main;
            main.playOnAwake = false;
            main.loop = false;
            main.maxParticles = 320;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(.2f, .55f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(.035f, .14f);
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, .01f, .02f, 1f),
                new Color(1f, .4f, .18f, 1f));
            ParticleSystem.EmissionModule emission = impactSparks.emission;
            emission.enabled = false;
            ParticleSystem.ShapeModule shape = impactSparks.shape;
            shape.enabled = false;

            Shader particleShader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
                                    Shader.Find("Particles/Standard Unlit");
            if (particleShader != null)
            {
                m_ParticleMaterial = new Material(particleShader) { name = "VH_GridCut_Sparks_Runtime" };
                if (m_ParticleMaterial.HasProperty("_BaseColor"))
                    m_ParticleMaterial.SetColor("_BaseColor", new Color(3f, .02f, .03f, 1f));
                impactSparks.GetComponent<ParticleSystemRenderer>().sharedMaterial = m_ParticleMaterial;
            }
            impactSparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        private void BuildStrips()
        {
            m_StripRenderers.Clear();
            for (int i = damageStripsRoot.childCount - 1; i >= 0; i--)
                Destroy(damageStripsRoot.GetChild(i).gameObject);

            float usableHalfExtent = Mathf.Max(0f, m_HalfExtent - m_LineWidth * .5f);
            float fullLength = m_HalfExtent * 2f;
            float safeHeight = Mathf.Max(.008f, stripHeight);
            for (int i = 0; i < m_LineCount; i++)
            {
                float t = m_LineCount == 1 ? .5f : i / (float)(m_LineCount - 1);
                float offset = Mathf.Lerp(-usableHalfExtent, usableHalfExtent, t);
                CreateStrip(
                    string.Format("HorizontalDamageStrip_{0:00}_MeshMaterial", i + 1),
                    new Vector3(0f, 0f, offset),
                    new Vector3(fullLength, safeHeight, m_LineWidth));
                CreateStrip(
                    string.Format("VerticalDamageStrip_{0:00}_MeshMaterial", i + 1),
                    new Vector3(offset, safeHeight * .55f, 0f),
                    new Vector3(m_LineWidth, safeHeight, fullLength));
            }
            SetStripsEnabled(false);
        }

        private void CreateStrip(string objectName, Vector3 localPosition, Vector3 localScale)
        {
            GameObject strip = GameObject.CreatePrimitive(PrimitiveType.Cube);
            strip.name = objectName;
            strip.transform.SetParent(damageStripsRoot, false);
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

        private void Update()
        {
            if (m_Configured) UpdateBurst();
        }

        private void UpdateBurst()
        {
            double now = ReadServerTime();
            while (m_EmittedPulses < m_Repetitions &&
                   now >= m_FirstHitServerTime + m_EmittedPulses * m_Interval)
            {
                m_EmittedPulses++;
                EmitCompositeBurst();
            }

            if (m_EmittedPulses <= 0)
            {
                SetStripsEnabled(false);
                if (impactLight != null) impactLight.intensity = 0f;
                return;
            }

            double latestHit = m_FirstHitServerTime + (m_EmittedPulses - 1) * m_Interval;
            float progress = (float)((now - latestHit) / Mathf.Max(.01f, pulseDuration));
            bool visible = progress >= 0f && progress <= 1f;
            SetStripsEnabled(visible);
            SetProgress(Mathf.Clamp01(progress));
            if (impactLight != null)
            {
                float fade = 1f - Mathf.Clamp01(progress);
                impactLight.intensity = visible ? flashIntensity * fade * fade : 0f;
            }

            double finalEnd = m_FirstHitServerTime + (m_Repetitions - 1) * m_Interval + pulseDuration;
            if (now >= finalEnd) Destroy(gameObject);
        }

        private void EmitCompositeBurst()
        {
            EmitStripSparks();
            if (impactLight != null) impactLight.intensity = flashIntensity;
        }

        private void EmitStripSparks()
        {
            if (impactSparks == null) return;
            int perStrip = Mathf.Max(1, sparksPerStrip);
            float usableHalfExtent = Mathf.Max(0f, m_HalfExtent - m_LineWidth * .5f);
            for (int line = 0; line < m_LineCount; line++)
            {
                float t = m_LineCount == 1 ? .5f : line / (float)(m_LineCount - 1);
                float offset = Mathf.Lerp(-usableHalfExtent, usableHalfExtent, t);
                for (int spark = 0; spark < perStrip; spark++)
                {
                    EmitSpark(new Vector3(Random.Range(-m_HalfExtent, m_HalfExtent), .06f, offset));
                    EmitSpark(new Vector3(offset, .06f, Random.Range(-m_HalfExtent, m_HalfExtent)));
                }
            }
        }

        private void EmitSpark(Vector3 localPosition)
        {
            var emit = new ParticleSystem.EmitParams
            {
                position = transform.TransformPoint(localPosition),
                velocity = new Vector3(
                    Random.Range(-.8f, .8f),
                    Random.Range(1.2f, 3.4f),
                    Random.Range(-.8f, .8f))
            };
            impactSparks.Emit(emit, 1);
        }

        private void SetStripsEnabled(bool enabled)
        {
            for (int i = 0; i < m_StripRenderers.Count; i++)
                if (m_StripRenderers[i] != null) m_StripRenderers[i].enabled = enabled;
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
            if (m_ParticleMaterial != null) Destroy(m_ParticleMaterial);
        }
    }
}
