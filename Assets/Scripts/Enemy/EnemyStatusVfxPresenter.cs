using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cosmetic-only enemy status presenter. Persistent state comes from the
/// replicated debuff list; Execute cues add small pulses without affecting play.
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyStatusVfxPresenter : MonoBehaviour
{
    private sealed class StatusVisual
    {
        public GameObject Root;
        public ParticleSystem[] Particles;
        public Material Material;
    }

    public const string BurningCueTag = "GameplayCue.Status.Burning";
    public const string FrozenCueTag = "GameplayCue.Status.Frozen";

    [Header("全局配置 / Global Config")]
    [Tooltip("通常留空并自动读取 Resources/GameVFX/EnemyStatusVfxConfig。仅在特殊敌人需要覆盖全局表现时赋值。")]
    [SerializeField] private EnemyStatusVfxConfig configOverride;

    private readonly Dictionary<string, StatusVisual> visuals =
        new(StringComparer.Ordinal);

    public EnemyStatusVfxConfig Config =>
        configOverride != null ? configOverride : EnemyStatusVfxConfig.LoadDefault();

    public bool IsStatusVisible(string cueTag) =>
        visuals.TryGetValue(cueTag, out StatusVisual visual) &&
        visual.Root != null && visual.Root.activeSelf;

    public void SetStatus(string cueTag, bool active)
    {
        if (Application.isBatchMode || string.IsNullOrWhiteSpace(cueTag)) return;
        StatusVisual visual = GetOrCreate(cueTag);
        if (visual == null) return;

        if (active)
        {
            visual.Root.SetActive(true);
            PlayParticles(visual.Particles);
        }
        else
        {
            StopParticles(visual.Particles);
            visual.Root.SetActive(false);
        }
    }

    public void Pulse(string cueTag, float magnitude)
    {
        if (Application.isBatchMode || string.IsNullOrWhiteSpace(cueTag)) return;
        StatusVisual visual = GetOrCreate(cueTag);
        if (visual == null) return;

        bool wasInactive = !visual.Root.activeSelf;
        if (wasInactive) visual.Root.SetActive(true);
        int particleCount = Mathf.Clamp(Mathf.RoundToInt(4f + magnitude), 4, 20);
        EmitParticles(visual.Particles, particleCount);
        if (wasInactive) PlayParticles(visual.Particles);
    }

    public void ClearAll()
    {
        foreach (StatusVisual visual in visuals.Values)
        {
            if (visual?.Particles != null) StopParticles(visual.Particles);
            if (visual?.Root != null) visual.Root.SetActive(false);
        }
    }

    private StatusVisual GetOrCreate(string cueTag)
    {
        if (visuals.TryGetValue(cueTag, out StatusVisual existing)) return existing;
        if (!string.Equals(cueTag, BurningCueTag, StringComparison.Ordinal) &&
            !string.Equals(cueTag, FrozenCueTag, StringComparison.Ordinal))
        {
            return null;
        }

        bool burning = string.Equals(cueTag, BurningCueTag, StringComparison.Ordinal);
        string instanceName = burning ? "StatusVFX_Burning" : "StatusVFX_Frozen";
        EnemyStatusVfxConfig config = Config;
        GameObject prefab = config != null
            ? burning ? config.BurningPrefab : config.FrozenPrefab
            : null;
        Vector3 localOffset = config != null
            ? burning ? config.BurningLocalOffset : config.FrozenLocalOffset
            : Vector3.up * 0.9f;
        GameObject root;
        Material material = null;

        if (prefab != null)
        {
            root = Instantiate(prefab, transform, false);
            root.name = instanceName;
            root.transform.localPosition = localOffset;
        }
        else
        {
            root = new GameObject(instanceName);
            root.transform.SetParent(transform, false);
            root.transform.localPosition = localOffset;

            ParticleSystem fallbackParticles = root.AddComponent<ParticleSystem>();
            ConfigureParticles(fallbackParticles, burning);
            material = CreateParticleMaterial(burning);
            if (material != null)
            {
                fallbackParticles
                    .GetComponent<ParticleSystemRenderer>()
                    .sharedMaterial = material;
            }
        }

        // Disable before binding so OverlayFX performs its first material injection
        // only when the status is actually applied, not during prefab construction.
        root.SetActive(false);
        ParticleSystem[] particles = root.GetComponentsInChildren<ParticleSystem>(true);
        OverlayFX[] overlayEffects = root.GetComponentsInChildren<OverlayFX>(true);
        BindOverlayTargets(root.transform, overlayEffects);

        StatusVisual visual = new()
        {
            Root = root,
            Particles = particles,
            Material = material
        };
        visuals.Add(cueTag, visual);
        return visual;
    }

    private void BindOverlayTargets(
        Transform effectRoot,
        OverlayFX[] overlayEffects)
    {
        if (overlayEffects == null || overlayEffects.Length == 0) return;

        Renderer targetRenderer = FindPrimaryMeshRenderer(effectRoot);
        if (targetRenderer == null)
        {
            Debug.LogWarning(
                $"[Enemy Status VFX] '{name}' has OverlayFX but no compatible " +
                "SkinnedMeshRenderer or MeshRenderer target.",
                this);
            return;
        }

        for (int index = 0; index < overlayEffects.Length; index++)
        {
            OverlayFX overlay = overlayEffects[index];
            if (overlay != null) overlay.SetTargetRenderer(targetRenderer);
        }
    }

    private Renderer FindPrimaryMeshRenderer(Transform effectRoot)
    {
        SkinnedMeshRenderer[] skinnedRenderers =
            GetComponentsInChildren<SkinnedMeshRenderer>(true);
        Renderer best = FindLargestRenderer(skinnedRenderers, effectRoot);
        if (best != null) return best;

        MeshRenderer[] meshRenderers = GetComponentsInChildren<MeshRenderer>(true);
        return FindLargestRenderer(meshRenderers, effectRoot);
    }

    private static Renderer FindLargestRenderer<T>(
        T[] candidates,
        Transform excludedRoot)
        where T : Renderer
    {
        Renderer best = null;
        float bestScore = -1f;
        if (candidates == null) return null;

        for (int index = 0; index < candidates.Length; index++)
        {
            Renderer candidate = candidates[index];
            if (candidate == null ||
                (excludedRoot != null && candidate.transform.IsChildOf(excludedRoot)))
            {
                continue;
            }

            float score = candidate.localBounds.size.sqrMagnitude;
            if (score <= bestScore) continue;
            best = candidate;
            bestScore = score;
        }
        return best;
    }

    private static void PlayParticles(ParticleSystem[] particles)
    {
        if (particles == null) return;
        for (int index = 0; index < particles.Length; index++)
        {
            if (particles[index] != null) particles[index].Play(true);
        }
    }

    private static void StopParticles(ParticleSystem[] particles)
    {
        if (particles == null) return;
        for (int index = 0; index < particles.Length; index++)
        {
            if (particles[index] != null)
            {
                particles[index].Stop(
                    true,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }
    }

    private static void EmitParticles(ParticleSystem[] particles, int count)
    {
        if (particles == null) return;
        for (int index = 0; index < particles.Length; index++)
        {
            if (particles[index] != null) particles[index].Emit(count);
        }
    }

    private static void ConfigureParticles(ParticleSystem particles, bool burning)
    {
        ParticleSystem.MainModule main = particles.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = burning ? 72 : 48;
        main.startLifetime = burning
            ? new ParticleSystem.MinMaxCurve(0.45f, 0.85f)
            : new ParticleSystem.MinMaxCurve(0.8f, 1.4f);
        main.startSpeed = burning
            ? new ParticleSystem.MinMaxCurve(0.35f, 0.9f)
            : new ParticleSystem.MinMaxCurve(0.03f, 0.18f);
        main.startSize = burning
            ? new ParticleSystem.MinMaxCurve(0.12f, 0.32f)
            : new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
        main.startColor = burning
            ? new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.18f, 0.01f, 0.9f),
                new Color(1f, 0.82f, 0.08f, 1f))
            : new ParticleSystem.MinMaxGradient(
                new Color(0.2f, 0.75f, 1f, 0.8f),
                new Color(0.85f, 0.98f, 1f, 1f));

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = burning ? 26f : 14f;

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = burning ? 0.48f : 0.58f;
        shape.radiusThickness = burning ? 0.65f : 0.18f;

        ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particles.colorOverLifetime;
        colorOverLifetime.enabled = true;
        Gradient gradient = new();
        if (burning)
        {
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.95f, 0.25f), 0f),
                    new GradientColorKey(new Color(1f, 0.08f, 0f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(1f, 0.12f),
                    new GradientAlphaKey(0f, 1f)
                });
        }
        else
        {
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.85f, 1f, 1f), 0f),
                    new GradientColorKey(new Color(0.05f, 0.45f, 1f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.9f, 0.18f),
                    new GradientAlphaKey(0f, 1f)
                });
        }
        colorOverLifetime.color = gradient;

        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortingOrder = 20;
    }

    private static Material CreateParticleMaterial(bool burning)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit") ??
                        Shader.Find("Particles/Standard Unlit") ??
                        Shader.Find("Sprites/Default");
        if (shader == null) return null;
        Material material = new(shader)
        {
            name = burning ? "Runtime_BurningParticles" : "Runtime_FrozenParticles",
            hideFlags = HideFlags.HideAndDontSave
        };
        return material;
    }

    private void OnDestroy()
    {
        foreach (StatusVisual visual in visuals.Values)
        {
            if (visual?.Material == null) continue;
            if (Application.isPlaying) Destroy(visual.Material);
            else DestroyImmediate(visual.Material);
        }
        visuals.Clear();
    }
}
