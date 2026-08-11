using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Reduces the camera color to a fixed resolution, maps it to a finite palette
/// in Lab color space, and restores it with nearest-neighbour sampling.
/// </summary>
public sealed class PixelArtRendererFeature : ScriptableRendererFeature
{
    private const RenderPassEvent PixelationEvent = RenderPassEvent.AfterRenderingTransparents;

    [Header("Pixel Resolution")]
    [SerializeField, Min(1)] private int m_TargetWidth = 320;
    [SerializeField, Min(1)] private int m_TargetHeight = 180;

    [Header("Lab Palette Mapping")]
    [SerializeField] private bool m_EnablePaletteMapping = true;
    [SerializeField] private Shader m_PaletteShader;
    [SerializeField, Min(1)] private int m_GradientPaletteSize = 64;
    [SerializeField] private Gradient[] m_PaletteGradients = CreateDefaultGradients();

    [Header("Cameras")]
    [SerializeField] private bool m_ApplyToSceneView;

    private PixelArtRenderPass m_RenderPass;

    public override void Create()
    {
        Shader paletteShader = m_PaletteShader != null
            ? m_PaletteShader
            : Resources.Load<Shader>("PixelArtPaletteLab");

        if (paletteShader == null)
        {
            paletteShader = Shader.Find("Hidden/PixelArt/PaletteLab");
        }

        m_RenderPass = new PixelArtRenderPass(PixelationEvent, paletteShader);
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (!ShouldRender(in renderingData))
        {
            return;
        }

        renderer.EnqueuePass(m_RenderPass);
    }

    public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
    {
        if (!ShouldRender(in renderingData))
        {
            return;
        }

        m_RenderPass.Setup(
            renderer.cameraColorTargetHandle,
            Mathf.Max(1, m_TargetWidth),
            Mathf.Max(1, m_TargetHeight),
            m_EnablePaletteMapping,
            Mathf.Max(1, m_GradientPaletteSize),
            m_PaletteGradients);
    }

    protected override void Dispose(bool disposing)
    {
        m_RenderPass?.Dispose();
        m_RenderPass = null;
    }

    private bool ShouldRender(in RenderingData renderingData)
    {
        CameraData cameraData = renderingData.cameraData;

        if (!cameraData.resolveFinalTarget)
        {
            return false;
        }

        if (cameraData.cameraType == CameraType.Game)
        {
            return true;
        }

        return m_ApplyToSceneView && cameraData.cameraType == CameraType.SceneView;
    }

    private static Gradient[] CreateDefaultGradients()
    {
        // "Moonlit gothic" palette for the Vampires vs. Vampire Hunters battlefield.
        // Each ramp is a dark -> light value ladder inside a single hue family so
        // the Lab nearest-colour mapping preserves luminance structure. The set is
        // chosen to cover every colour family that shows up in the night scene
        // (sky, moonlight, blood, foliage, earth, firelight, stone) so incoming
        // pixels map onto a same-hue entry instead of drifting to a wrong hue.
        return new[]
        {
            // Night sky / deep shadow (indigo -> pale lilac).
            CreateGradient(Hex("0B0A1A"), Hex("241C3C"), Hex("453A6B"), Hex("7B6FA6"), Hex("B9AFD6")),
            // Moonlight / hunter steel (cold teal -> ice white).
            CreateGradient(Hex("0C171B"), Hex("1E3A42"), Hex("39707C"), Hex("77AEB6"), Hex("CFE6E8")),
            // Vampire blood (near-black maroon -> dusty rose).
            CreateGradient(Hex("1A0608"), Hex("4A0F14"), Hex("8C1F26"), Hex("C24B4E"), Hex("E39B99")),
            // Vampire arcane accent (deep violet -> orchid).
            CreateGradient(Hex("150A22"), Hex("34164F"), Hex("5E2E86"), Hex("9260BE"), Hex("C9A8E4")),
            // Foliage / grass (dark forest -> sage).
            CreateGradient(Hex("0A1408"), Hex("1B3314"), Hex("356026"), Hex("5E8E45"), Hex("A6C285")),
            // Earth / rock / leather (dark umber -> tan).
            CreateGradient(Hex("140D07"), Hex("35230F"), Hex("5E401F"), Hex("8F6A3E"), Hex("C4A277")),
            // Firelight / candle highlight (warm amber -> gold).
            CreateGradient(Hex("1E0E03"), Hex("5A2A08"), Hex("9C5814"), Hex("D68F2E"), Hex("F4CE7A")),
            // Cool neutral stone / bone (kept slightly blue to avoid dead grey).
            CreateGradient(Hex("0E1013"), Hex("262B31"), Hex("4B535C"), Hex("828C96"), Hex("C6CDD4")),
        };
    }

    private static Color Hex(string rgb)
    {
        return ColorUtility.TryParseHtmlString("#" + rgb, out Color color) ? color : Color.magenta;
    }

    private static Gradient CreateGradient(params Color[] colors)
    {
        var colorKeys = new GradientColorKey[colors.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            float time = colors.Length == 1 ? 0f : i / (float)(colors.Length - 1);
            colorKeys[i] = new GradientColorKey(colors[i], time);
        }

        var gradient = new Gradient
        {
            mode = GradientMode.Blend,
            colorSpace = ColorSpace.Gamma
        };
        gradient.SetKeys(
            colorKeys,
            new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 1f)
            });
        return gradient;
    }

    private sealed class PixelArtRenderPass : ScriptableRenderPass
    {
        private const string LowResolutionTextureName = "_PixelArtLowResolutionTexture";
        private const string PaletteTextureName = "_PixelArtPaletteTexture";

        private static readonly int PaletteEntriesId = Shader.PropertyToID("_PaletteEntries");
        private static readonly int PaletteCountId = Shader.PropertyToID("_PaletteCount");

        private readonly ProfilingSampler m_ProfilingSampler =
            new ProfilingSampler("Pixel Art Downsample/Palette/Upsample");
        private readonly Material m_PaletteMaterial;

        private RTHandle m_CameraColor;
        private RTHandle m_LowResolutionTexture;
        private RTHandle m_PaletteTexture;
        private PaletteEntry[] m_PaletteEntries = new PaletteEntry[0];
        private GraphicsBuffer m_PaletteBuffer;
        private int m_TargetWidth;
        private int m_TargetHeight;
        private int m_PaletteCount;
        private int m_PaletteSettingsHash = int.MinValue;
        private bool m_UsePaletteMapping;

        private struct PaletteEntry
        {
            public Vector4 color;
            public Vector4 lab;

            public PaletteEntry(Color sourceColor, Vector4 labColor)
            {
                color = sourceColor;
                lab = labColor;
            }
        }

        public PixelArtRenderPass(RenderPassEvent passEvent, Shader paletteShader)
        {
            renderPassEvent = passEvent;
            m_PaletteMaterial = paletteShader != null
                ? CoreUtils.CreateEngineMaterial(paletteShader)
                : null;
        }

        public void Setup(
            RTHandle cameraColor,
            int targetWidth,
            int targetHeight,
            bool enablePaletteMapping,
            int paletteSize,
            Gradient[] paletteGradients)
        {
            m_CameraColor = cameraColor;
            m_TargetWidth = targetWidth;
            m_TargetHeight = targetHeight;
            BuildGradientPalette(paletteGradients, paletteSize);
            m_UsePaletteMapping = enablePaletteMapping &&
                                  m_PaletteMaterial != null &&
                                  m_PaletteCount > 0;
        }

        private void BuildGradientPalette(Gradient[] gradients, int requestedColorCount)
        {
            int settingsHash = CalculatePaletteSettingsHash(gradients, requestedColorCount);
            if (settingsHash == m_PaletteSettingsHash && m_PaletteBuffer != null)
            {
                return;
            }

            m_PaletteSettingsHash = settingsHash;
            m_PaletteCount = 0;
            if (gradients == null || gradients.Length == 0)
            {
                ReleasePaletteBuffer();
                return;
            }

            int validGradientCount = 0;
            for (int i = 0; i < gradients.Length; i++)
            {
                if (gradients[i] != null)
                {
                    validGradientCount++;
                }
            }

            if (validGradientCount == 0)
            {
                ReleasePaletteBuffer();
                return;
            }

            int colorCount = Mathf.Max(1, requestedColorCount);
            if (m_PaletteEntries.Length != colorCount)
            {
                m_PaletteEntries = new PaletteEntry[colorCount];
            }

            int baseSamplesPerGradient = colorCount / validGradientCount;
            int remainder = colorCount % validGradientCount;
            int validGradientIndex = 0;

            for (int gradientIndex = 0; gradientIndex < gradients.Length; gradientIndex++)
            {
                Gradient gradient = gradients[gradientIndex];
                if (gradient == null)
                {
                    continue;
                }

                int sampleCount = baseSamplesPerGradient +
                                  (validGradientIndex < remainder ? 1 : 0);
                validGradientIndex++;

                if (sampleCount == 0)
                {
                    continue;
                }

                for (int sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                {
                    float time = sampleCount == 1
                        ? 0.5f
                        : sampleIndex / (float)(sampleCount - 1);
                    Color color = gradient.Evaluate(time);
                    m_PaletteEntries[m_PaletteCount] =
                        new PaletteEntry(color, SrgbToLab(color));
                    m_PaletteCount++;
                }
            }

            UploadPaletteBuffer();
        }

        private void UploadPaletteBuffer()
        {
            if (m_PaletteCount == 0)
            {
                ReleasePaletteBuffer();
                return;
            }

            if (m_PaletteBuffer == null || m_PaletteBuffer.count != m_PaletteCount)
            {
                ReleasePaletteBuffer();
                m_PaletteBuffer = new GraphicsBuffer(
                    GraphicsBuffer.Target.Structured,
                    m_PaletteCount,
                    sizeof(float) * 8);
            }

            m_PaletteBuffer.SetData(m_PaletteEntries, 0, 0, m_PaletteCount);
        }

        private static int CalculatePaletteSettingsHash(
            Gradient[] gradients,
            int requestedColorCount)
        {
            unchecked
            {
                int hash = requestedColorCount;
                if (gradients == null)
                {
                    return hash;
                }

                hash = hash * 31 + gradients.Length;
                for (int gradientIndex = 0; gradientIndex < gradients.Length; gradientIndex++)
                {
                    Gradient gradient = gradients[gradientIndex];
                    if (gradient == null)
                    {
                        hash *= 31;
                        continue;
                    }

                    hash = hash * 31 + (int)gradient.mode;
                    hash = hash * 31 + (int)gradient.colorSpace;

                    GradientColorKey[] colorKeys = gradient.colorKeys;
                    hash = hash * 31 + colorKeys.Length;
                    for (int keyIndex = 0; keyIndex < colorKeys.Length; keyIndex++)
                    {
                        hash = hash * 31 + colorKeys[keyIndex].color.GetHashCode();
                        hash = hash * 31 + colorKeys[keyIndex].time.GetHashCode();
                    }

                    GradientAlphaKey[] alphaKeys = gradient.alphaKeys;
                    hash = hash * 31 + alphaKeys.Length;
                    for (int keyIndex = 0; keyIndex < alphaKeys.Length; keyIndex++)
                    {
                        hash = hash * 31 + alphaKeys[keyIndex].alpha.GetHashCode();
                        hash = hash * 31 + alphaKeys[keyIndex].time.GetHashCode();
                    }
                }

                return hash;
            }
        }

        private static Vector4 SrgbToLab(Color color)
        {
            float r = Mathf.Pow(Mathf.Clamp01(color.r), 2.2f);
            float g = Mathf.Pow(Mathf.Clamp01(color.g), 2.2f);
            float b = Mathf.Pow(Mathf.Clamp01(color.b), 2.2f);

            float x = (0.4124564f * r + 0.3575761f * g + 0.1804375f * b) / 0.95047f;
            float y = 0.2126729f * r + 0.7151522f * g + 0.0721750f * b;
            float z = (0.0193339f * r + 0.1191920f * g + 0.9503041f * b) / 1.08883f;

            float fx = LabCurve(x);
            float fy = LabCurve(y);
            float fz = LabCurve(z);

            return new Vector4(
                116f * fy - 16f,
                500f * (fx - fy),
                200f * (fy - fz),
                0f);
        }

        private static float LabCurve(float value)
        {
            return value > 0.008856f
                ? Mathf.Pow(Mathf.Max(value, 0f), 1f / 3f)
                : 7.787f * value + 16f / 116f;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.width = m_TargetWidth;
            descriptor.height = m_TargetHeight;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = 1;
            descriptor.bindMS = false;
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;

            RenderingUtils.ReAllocateIfNeeded(
                ref m_LowResolutionTexture,
                descriptor,
                FilterMode.Point,
                TextureWrapMode.Clamp,
                name: LowResolutionTextureName);

            RenderingUtils.ReAllocateIfNeeded(
                ref m_PaletteTexture,
                descriptor,
                FilterMode.Point,
                TextureWrapMode.Clamp,
                name: PaletteTextureName);

            ConfigureTarget(m_LowResolutionTexture);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (m_CameraColor == null ||
                m_LowResolutionTexture == null ||
                m_PaletteTexture == null)
            {
                return;
            }

            CommandBuffer cmd = CommandBufferPool.Get();

            using (new ProfilingScope(cmd, m_ProfilingSampler))
            {
                // First reduce the fully rendered scene geometry with point sampling.
                Blitter.BlitCameraTexture(cmd, m_CameraColor, m_LowResolutionTexture, bilinear: false);

                RTHandle upsampleSource = m_LowResolutionTexture;
                if (m_UsePaletteMapping)
                {
                    m_PaletteMaterial.SetInt(PaletteCountId, m_PaletteCount);
                    m_PaletteMaterial.SetBuffer(PaletteEntriesId, m_PaletteBuffer);
                    Blitter.BlitCameraTexture(
                        cmd,
                        m_LowResolutionTexture,
                        m_PaletteTexture,
                        m_PaletteMaterial,
                        0);
                    upsampleSource = m_PaletteTexture;
                }

                // Restore the quantized low-resolution image without interpolation.
                Blitter.BlitCameraTexture(cmd, upsampleSource, m_CameraColor, bilinear: false);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            m_LowResolutionTexture?.Release();
            m_LowResolutionTexture = null;
            m_PaletteTexture?.Release();
            m_PaletteTexture = null;
            ReleasePaletteBuffer();
            CoreUtils.Destroy(m_PaletteMaterial);
        }

        private void ReleasePaletteBuffer()
        {
            m_PaletteBuffer?.Release();
            m_PaletteBuffer = null;
        }
    }
}
