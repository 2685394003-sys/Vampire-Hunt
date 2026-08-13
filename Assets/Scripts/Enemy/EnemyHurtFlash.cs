using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemyHurtFlash : MonoBehaviour
{
    public Color hurtRed = Color.red;

    private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");

    private readonly List<RendererColorTarget> colorTargets = new();
    private float flashTimer;
    private bool colorsOverridden;

    private sealed class RendererColorTarget
    {
        public Renderer renderer;
        public int materialIndex;
        public int colorProperty;
        public Color originalColor;
        public MaterialPropertyBlock propertyBlock;
    }

    private void Awake()
    {
        CacheRenderers();
    }

    private void OnDisable()
    {
        RestoreOriginalColors();
        flashTimer = 0f;
    }

    private void Update()
    {
        if (flashTimer > 0)
        {
            flashTimer -= Time.deltaTime;
            EnemyStatsConfig stats = EnemyStatsResolver.Resolve(this);
            float frequency = stats != null ? stats.hurtFlashSpeed : 15f;
            bool showHurtColor =
                Mathf.Sin(Time.time * Mathf.PI * 2f * Mathf.Max(0.01f, frequency)) > 0f;
            ApplyFlash(showHurtColor);
        }
        else
        {
            RestoreOriginalColors();
        }
    }

    public void StartHurtFlash()
    {
        if (colorTargets.Count == 0)
            CacheRenderers();
        EnemyStatsConfig stats = EnemyStatsResolver.Resolve(this);
        flashTimer = stats != null ? stats.hurtFlashDuration : 0.3f;
    }

    private void CacheRenderers()
    {
        colorTargets.Clear();
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            for (int i = 0; i < materials.Length; i++)
            {
                Material material = materials[i];
                if (material == null)
                    continue;

                int property = material.HasProperty(BaseColorProperty)
                    ? BaseColorProperty
                    : material.HasProperty(ColorProperty)
                        ? ColorProperty
                        : 0;
                if (property == 0)
                    continue;

                colorTargets.Add(new RendererColorTarget
                {
                    renderer = renderer,
                    materialIndex = i,
                    colorProperty = property,
                    originalColor = material.GetColor(property),
                    propertyBlock = new MaterialPropertyBlock()
                });
            }
        }
    }

    private void ApplyFlash(bool showHurtColor)
    {
        colorsOverridden = true;
        foreach (RendererColorTarget target in colorTargets)
        {
            if (target.renderer == null)
                continue;

            Color color = showHurtColor
                ? new Color(hurtRed.r, hurtRed.g, hurtRed.b, target.originalColor.a)
                : target.originalColor;
            target.renderer.GetPropertyBlock(target.propertyBlock, target.materialIndex);
            target.propertyBlock.SetColor(target.colorProperty, color);
            target.renderer.SetPropertyBlock(target.propertyBlock, target.materialIndex);
        }
    }

    private void RestoreOriginalColors()
    {
        if (!colorsOverridden)
            return;

        foreach (RendererColorTarget target in colorTargets)
        {
            if (target.renderer == null)
                continue;

            target.renderer.GetPropertyBlock(target.propertyBlock, target.materialIndex);
            target.propertyBlock.SetColor(target.colorProperty, target.originalColor);
            target.renderer.SetPropertyBlock(target.propertyBlock, target.materialIndex);
        }
        colorsOverridden = false;
    }
}
