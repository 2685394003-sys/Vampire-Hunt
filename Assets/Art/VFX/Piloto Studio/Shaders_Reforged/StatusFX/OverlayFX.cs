using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Piloto mesh overlay with per-instance material ownership. Fire and frozen effects may
/// share a renderer; disabling/rebinding one removes only the material it created.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class OverlayFX : MonoBehaviour
{
    public Material overlayMaterial;
    public Renderer targetRenderer;
    public List<ParticleSystem> particleSystems = new();

    private float particleSizeMultiplier = 0.15f;
    public Vector3 rendererTrueForward = Vector3.zero;

    [SerializeField, HideInInspector] private Material m_OwnedMaterial;
    [SerializeField, HideInInspector] private Renderer m_BoundRenderer;

    private void OnEnable()
    {
        EnsureOverlay();
        SyncParticleSystems();
    }

    private void OnValidate() => SyncParticleSystems();
    private void OnDrawGizmosSelected() => SyncParticleSystems();
    private void OnDisable() => ReleaseOverlay();
    private void OnDestroy() => ReleaseOverlay();

    /// <summary>Bind before enabling an instance so particles never start on a stale mesh.</summary>
    public void SetTargetRenderer(Renderer renderer)
    {
        if (targetRenderer != renderer || m_BoundRenderer != renderer) ReleaseOverlay();
        targetRenderer = renderer;
        SyncParticleSystems();
        if (isActiveAndEnabled) EnsureOverlay();
    }

    private void EnsureOverlay()
    {
        if (overlayMaterial == null || targetRenderer == null || m_OwnedMaterial != null) return;
        m_BoundRenderer = targetRenderer;
        m_OwnedMaterial = new Material(overlayMaterial)
        {
            name = overlayMaterial.name + (Application.isPlaying ? " (Runtime)" : " (Preview)"),
            hideFlags = HideFlags.HideAndDontSave
        };
        UpdateBounds(m_OwnedMaterial, m_BoundRenderer);
        // Append and later remove by identity, preserving original slots (including nulls)
        // and materials belonging to other effects. Current enemy meshes have one submesh.
        var materials = new List<Material>(m_BoundRenderer.sharedMaterials) { m_OwnedMaterial };
        m_BoundRenderer.sharedMaterials = materials.ToArray();
    }

    private void ReleaseOverlay()
    {
        if (m_OwnedMaterial == null)
        {
            m_BoundRenderer = null;
            return;
        }
        if (m_BoundRenderer != null)
        {
            var materials = new List<Material>(m_BoundRenderer.sharedMaterials);
            materials.RemoveAll(material => material == m_OwnedMaterial);
            m_BoundRenderer.sharedMaterials = materials.ToArray();
        }
        if (Application.isPlaying) Destroy(m_OwnedMaterial);
        else DestroyImmediate(m_OwnedMaterial);
        m_OwnedMaterial = null;
        m_BoundRenderer = null;
    }
    private void SyncParticleSystems()
    {
        if (targetRenderer == null) return;

        Vector3 worldSize = Vector3.one;
        if (targetRenderer is MeshRenderer mr && mr.TryGetComponent(out MeshFilter mf) && mf.sharedMesh != null)
            worldSize = Vector3.Scale(mf.sharedMesh.bounds.size, mf.transform.lossyScale);
        else if (targetRenderer is SkinnedMeshRenderer smr && smr.sharedMesh != null)
            worldSize = Vector3.Scale(smr.sharedMesh.bounds.size, smr.transform.lossyScale);

        float scale = Mathf.Max(worldSize.x, worldSize.y, worldSize.z) * particleSizeMultiplier;

        foreach (var ps in particleSystems)
        {
            if (!ps) continue;
            var shape = ps.shape;
            shape.enabled = true;

            if (targetRenderer is MeshRenderer meshR)
            {
                shape.shapeType = ParticleSystemShapeType.MeshRenderer;
                shape.meshRenderer = meshR;
            }
            else if (targetRenderer is SkinnedMeshRenderer skinnedR)
            {
                shape.shapeType = ParticleSystemShapeType.SkinnedMeshRenderer;
                shape.skinnedMeshRenderer = skinnedR;
            }

            var main = ps.main;
            main.startSizeMultiplier = scale;
        }
    }

    private void UpdateBounds(Material mat, Renderer rend)
    {
        var b = rend.localBounds;
        mat.SetVector("_LocalBoundsMinimum", b.min);
        mat.SetVector("_LocalBoundsMaximum", b.max);

        if (rend.TryGetComponent(out SkinnedMeshRenderer skinned) &&
            skinned.rootBone != null)
            rendererTrueForward = skinned.rootBone.transform.rotation.eulerAngles;

        if (rendererTrueForward.x <= 90f)
        {
            ShaderKeywordController.SetGradientAxis(mat, GradientAxis.Y);
            ShaderKeywordController.SetUvDirection(mat, UvDirection.X);
        }
        else if (rendererTrueForward.x >= 270f)
        {
            ShaderKeywordController.SetGradientAxis(mat, GradientAxis.Z);
            ShaderKeywordController.SetUvDirection(mat, UvDirection.Z);
            foreach (var ps in particleSystems)
            {
                if (ps == null) continue;
                var vel = ps.velocityOverLifetime;
                if (!vel.enabled) continue;

                if (vel.orbitalX.mode == ParticleSystemCurveMode.Constant &&
                    vel.orbitalY.mode == ParticleSystemCurveMode.Constant &&
                    vel.orbitalZ.mode == ParticleSystemCurveMode.Constant)
                {
                    float combined = Mathf.Max(vel.orbitalX.constant, vel.orbitalY.constant, vel.orbitalZ.constant);
                    vel.orbitalX = new ParticleSystem.MinMaxCurve(0f);
                    vel.orbitalY = new ParticleSystem.MinMaxCurve(0f);
                    vel.orbitalZ = new ParticleSystem.MinMaxCurve(combined);
                }
            }
        }
    }

    public enum GradientAxis { X, Y, Z }
    public enum UvDirection { X, Y, Z }
    public static class ShaderKeywordController
    {
        private static readonly string[] GradientAxisKeywords = { "_GRADIENT_AXIS_X", "_GRADIENT_AXIS_Y", "_GRADIENT_AXIS_Z" };
        private static readonly string[] UvDirectionKeywords = { "_UV_DIRECTION_X", "_UV_DIRECTION_Y", "_UV_DIRECTION_Z" };
        public static void SetGradientAxis(Material mat, GradientAxis axis)
        {
            for (int i = 0; i < GradientAxisKeywords.Length; i++)
                if (i == (int)axis) mat.EnableKeyword(GradientAxisKeywords[i]);
                else mat.DisableKeyword(GradientAxisKeywords[i]);
        }
        public static void SetUvDirection(Material mat, UvDirection uv)
        {
            for (int i = 0; i < UvDirectionKeywords.Length; i++)
                if (i == (int)uv) mat.EnableKeyword(UvDirectionKeywords[i]);
                else mat.DisableKeyword(UvDirectionKeywords[i]);
        }
    }
}
