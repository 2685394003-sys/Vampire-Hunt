using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 假云 · 地面云影（Cloud Shadow）· Vampire Hunt。
///
/// 解决的问题：俯视镜头下"天空永远不入镜"（相机 offset (0,10.5,-3.18)、FOV 60、俯角 73.15°，
/// 拉到最平时画面上缘仍留 31°~43° 俯角），所以占位式的"天上飘一层云"在本项目里永久不可见。
/// 唯一在俯瞰视角下可读、且成本极低的"云"，是**投在地面上的云影**。
///
/// 做法：全屏一次 pass，用深度图 + 逆视图投影矩阵把每个像素反投影回世界坐标，
/// 取世界 XZ 当 UV 叠加两层程序化 fbm 噪声得到云掩码，再对画面做乘法压暗。
///
/// 两个通道（同一次 blit 里出，不新增 pass）：
///   ① 云影（Cloud Shadow）：云所在处 → 乘法压暗；
///   ② 云隙透光（Cloud Gap Light）：云缝处 → 加法提亮（阴郁氛围用冷色、低强度）。
///   物理上「云挡光 ⇒ 地面有影」和「云缝透光 ⇒ 地面有光斑」是同一个量的两面，
///   所以两者共用同一层噪声，只是阈值开在两端。
///
/// 为什么不需要遮罩（不像"分层虚化"那样要区分角色/背景）：
///   现实里的云影本来就同时压暗地面、角色和特效，全屏相乘是**物理正确**的，
///   因此不需要任何 layer 遮罩 —— 直接绕开了锐利层遮罩那套工程。
///
/// 为什么多人下天然一致：
///   云图案锚在世界坐标里，各客户端反投影出的图案完全相同。
///   ⚠ 但漂移相位取自各自的时间，可能不同步 —— 属纯视觉差异（与地面雾同样"各客户端各渲一份"），
///     不影响玩法。以后要严格同步，把 m_UseUnscaledTime 换成局内时间即可。
///
/// ⚠ 成立前提：相机只有 yaw + pitch、**没有 roll**。本项目是 Cinemachine 俯视机架，满足。
///
/// ⚠ 与内置 Depth of Field **无冲突**（那是模糊，本件是压暗），但本 Feature 排在景深之后执行，
///   所以景深先糊、云影后压，两者叠加顺序是确定且正确的。
/// </summary>
public sealed class VHCloudShadowFeature : ScriptableRendererFeature
{
    /// <summary>调试视图档位。</summary>
    public enum DebugView
    {
        [InspectorName("关闭")] Off = 0,
        [InspectorName("云掩码灰度")] Mask = 1,
        [InspectorName("世界网格（验证反投影）")] WorldGrid = 2,
        [InspectorName("透光掩码灰度")] GapMask = 3,
        [InspectorName("透光连续值（校准阈值用）")] GapRaw = 4
    }

    private const string kShaderName = "VH/CloudShadow";

    [Header("总开关")]
    [Tooltip("关掉后完全不干预画面，可用来做开关对比。")]
    [SerializeField] private bool m_Enabled = true;

    [Tooltip("着色器（一般留空即可，会自动按名字查找 VH/CloudShadow）。")]
    [SerializeField] private Shader m_Shader;

    [Header("云影")]
    [Tooltip("云影整体浓度 0~1。它是「云掩码」的整体缩放：1 = 云最深处压满 Shadow Color 指定的暗度；0 = 完全关闭（此时整个 pass 会被跳过，零开销）。")]
    [SerializeField, Range(0f, 1f)] private float m_Strength = 0.80f;

    [Tooltip("云图案对比度。4 阶 fbm 的实际动态范围只有约 [0.21,0.79]、中段挤在 0.5 附近，所以必须拉开才看得出云：1.0 = 原始（几乎全黑或全白）；2.2 推荐；调到 3~4 云更成团、间隙更干净。")]
    [SerializeField, Range(1f, 4f)] private float m_Contrast = 2.2f;

    [Tooltip("云块的世界尺度（米）。调小 = 云更碎更多；调大 = 一整片大云。可见地面带只有约 26m × 40m，太大等于看不到图案变化。")]
    [SerializeField, Range(4f, 200f)] private float m_CloudScale = 25f;

    [Tooltip("云漂移速度。⚠ 单位是「噪声 UV/秒」而不是米/秒 —— 换算到世界速度要 × CloudScale：值 0.3 在 CloudScale=30 时 = 世界 9 米/秒（相当快）。\n0 = 静止（会很假）。想让云走得慢，这个值要给到 0.05 以下。")]
    [SerializeField] private Vector2 m_DriftSpeed = new Vector2(1.2f, 0.7f);

    [Tooltip("第二层噪声的混合权重。0 = 单层（图案规则、能看出重复感）；1 = 全第二层（更碎）。")]
    [SerializeField, Range(0f, 1f)] private float m_Layer2Weight = 0.45f;

    [Tooltip("第二层尺度 = 主尺度 × 此值。0.2 ~ 0.5 最自然；接近 1 会出现摩尔纹。")]
    [SerializeField, Range(0.05f, 1f)] private float m_Layer2ScaleRatio = 0.37f;

    [Tooltip("第二层漂移 = 主速度 × 此值。两层速度错开才有翻涌感。")]
    [SerializeField, Range(0f, 4f)] private float m_Layer2SpeedRatio = 1.6f;

    [Tooltip("云覆盖率阈值。调低 = 云更多更密；调高 = 云更稀。\n📊 实测（Python 复刻算法、24 个世界偏移、40x26m 窗口）：在当前 Contrast=2.56 下，阈值 0.559 对应单屏覆盖率 **mean 12.1% / min 0% / max 39%** —— 注意 min 0% 意味着**有些屏幕整屏看不到云影**。\n想让云影更稳定地出现，把阈值往 0.52~0.53 方向降。")]
    [SerializeField, Range(0f, 1f)] private float m_Threshold = 0.50f;

    [Tooltip("云边缘的软硬程度。调小 = 硬边（像贴图）；0.25 ~ 0.5 更柔。")]
    [SerializeField, Range(0.01f, 1f)] private float m_Softness = 0.25f;

    [Tooltip("云影颜色（线性空间）：云最深处会被压到这个颜色。默认偏蓝灰，压暗约 30%（像阴天云影）。⚠ 禁给 >1 的 HDR 亮值，否则 ACES + Bloom 3.0 会整屏发灰。")]
    [SerializeField] private Color m_ShadowColor = new Color(0.62f, 0.67f, 0.78f, 1f);

    [Header("云隙透光（Cloud Gap Light）")]
    [Tooltip("开关。云缝处对画面做加法提亮 —— 物理上和云影是同一个量的两面（云挡光⇒有影，云缝透光⇒有光斑）。\n关掉后只走云影，回到纯压暗。")]
    [SerializeField] private bool m_EnableGapLight = true;

    [Tooltip("透光强度（加法量）0~0.5。\n0.10~0.15 = 阴郁氛围的「云缝漏光」，只提亮一点点；0.25 以上开始像晴天光斑。\n⚠ 上限故意只给 0.5：你已开 ACES + Bloom 3.0，加法给大了会把亮部整个糊开、整屏发灰。")]
    [SerializeField, Range(0f, 0.5f)] private float m_GapLightStrength = 0.12f;

    [Tooltip("光斑覆盖率阈值。实测（Python 复刻算法、24 个世界偏移、40x26m 窗口，G=1.0）：\n  0.58 → 覆盖 24.5%    0.62 → 19.4%    0.64 → 约 17%\n  0.70 → 11.7%    0.74 → 8.7%     0.78 → 6.2%\n参考：你当前云影的单屏覆盖率只有 12.1%（min 0% / max 39%），所以透光取 0.64 左右时「暗底 + 少量亮斑」最平衡。")]
    [SerializeField, Range(0f, 1f)] private float m_GapLightThreshold = 0.64f;

    [Tooltip("光斑边缘的软硬。0.30~0.45 更像「漏下来的光」，小于 0.15 会像贴纸。")]
    [SerializeField, Range(0.01f, 1f)] private float m_GapLightSoftness = 0.22f;

    [Tooltip("额外对比度（相对云影）。\n1.0 = 与云影相同的对比度 —— 推荐起点：云量本身已经过 Contrast 拉伸，这里再乘一层会把掩码压成近似二值、边缘变硬。\n⚠ 实测：这层给到 2.0 时覆盖率会从 17% 飙到 33% 左右，等于把「云缝漏光」变成「半屏提亮」。")]
    [SerializeField, Range(1f, 4f)] private float m_GapLightContrast = 1.0f;

    [Tooltip("沿光方向拉长的倍数。\n1.0 = 严格的物理互补（正好是云影的反相）；\n1.4~2.0 = 刻意的艺术偏移，让光斑与云影不完全重合 —— 否则整片地面会像「印上去的对称图案」。")]
    [SerializeField, Range(1f, 6f)] private float m_GapLightStretch = 1.6f;

    [Tooltip("光的噪声尺度 = 云影尺度 × 此值。\n⚠ 这不是美术风格选项，是**稳定性开关**：可见地面带只有约 31m 宽，云影用 30m 尺度时一帧只采得到 ~1 个噪声团，透光覆盖率会在 0%~80% 之间乱跳（实测：阈值固定时相邻 8 个位置的覆盖率 min 0.0% / max 89.9%）。\n0.4（默认）⇒ 约 2.5 个团/帧，覆盖率稳定；1.0 ⇒ 与云影严格同一层，覆盖率最不稳。")]
    [SerializeField, Range(0.1f, 1f)] private float m_GapLightScaleRatio = 0.4f;

    [Tooltip("光在地面上的投影方向（度）。应与场景平行光的方位角一致，否则光斑和影子的方向会互相打架，看着假。\n当前场景的平行光是 Unity 默认角度，方位角 = 330°。")]
    [SerializeField, Range(0f, 360f)] private float m_GapLightAngleDegrees = 330f;

    [Tooltip("透光颜色。默认冷白偏蓝 —— 阴郁氛围用冷色光。\n想要「晴天阳光」就转暖（如 (0.95, 0.88, 0.72)）。⚠ 同样禁给 >1 的 HDR 亮值。")]
    [SerializeField] private Color m_GapLightColor = new Color(0.70f, 0.78f, 0.90f, 1f);

    [Header("时间")]
    [Tooltip("用不受 timeScale 影响的时间驱动云漂移（推荐）。关掉则用 Time.time，游戏暂停时云会一起停。")]
    [SerializeField] private bool m_UseUnscaledTime = true;

    [Header("调试")]
    [Tooltip("关闭 = 正常画面；\n云掩码灰度 = 黑=无云 白=满云，看云影覆盖率与图案尺度；\n世界网格 = 每 10m 画一条青色线，网格必须贴在地面上并随世界移动（若它跟着屏幕不动，说明反投影接错了）；\n透光掩码灰度 = 黑=无光 白=最亮，单独看光斑形状与拉长方向、以及它是否压在云影上；\n透光连续值 = 未经阈值/软度的原始值，一次截图就能读出整条覆盖率曲线（校准 Threshold 用）。\n⚠ 调试档位会在强度为 0 时也强制跑 pass，方便单独验证。")]
    [SerializeField] private DebugView m_DebugView = DebugView.Off;

    [Tooltip("是否也作用于 Scene 视图。")]
    [SerializeField] private bool m_ApplyToSceneView;

    private Material m_Material;
    private VHCloudShadowPass m_Pass;

    public override void Create()
    {
        m_Pass = new VHCloudShadowPass
        {
            // ⚠ 必须正好是 AfterRenderingPostProcessing（600），不能学内置景深写 -1。
            // 原因（URP 17 源码 UniversalRendererRenderGraph.cs）：
            //   hasPassesAfterPostProcessing = 队列里存在 renderPassEvent ∈ [600,1000) 的 pass
            //   isTargetBackbuffer = resolveFinalTarget && !applyFinalPostProcessing
            //                        && !hasPassesAfterPostProcessing && !hasCaptureActions
            // 若本 pass 落在 599（∈[550,600)），hasPassesAfterPostProcessing 为 false →
            // isTargetBackbuffer 为 true → URP 会在跑后处理前调 SwitchActiveTexturesToBackbuffer()，
            // 把 resourceData.cameraColor 整个切回后备缓冲，本 pass 写回的 destination 被直接丢弃，
            // 表现为「pass 明明跑了，参数全对，画面却毫无变化」。
            // 取 600 后：后处理改为写中间纹理，本 pass 在后处理之后记录与执行，
            // 再由 FinalBlit 把结果呈现到屏幕（FXAA 也在其后执行）。
            //
            // 位置：本 Feature 在 PC_Renderer 的 Renderer Features 列表里**排在 VH Split Depth Of Field 之后**
            // ⇒ 同一个 600 事件下按列表顺序执行 ⇒ 先景深模糊、后云影压暗。
            renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing
        };

        EnsureResources();
    }

    protected override void Dispose(bool disposing)
    {
        m_Pass = null;

        if (m_Material != null)
        {
            CoreUtils.Destroy(m_Material);
            m_Material = null;
        }
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        bool shadowOn    = m_Strength > 0.001f;
        bool gapLightOn  = m_EnableGapLight && m_GapLightStrength > 0.0001f;

        // 调试档位即使两层强度都为 0 也要跑 —— 否则没法单独验证某一层掩码。
        if (!m_Enabled || (!shadowOn && !gapLightOn && m_DebugView == DebugView.Off))
        {
            return;
        }

        EnsureResources();
        if (m_Pass == null || m_Material == null)
        {
            return;
        }

        CameraData cameraData = renderingData.cameraData;
        Camera camera = cameraData.camera;
        if (camera == null)
        {
            return;
        }

        if (cameraData.cameraType == CameraType.Game)
        {
            // 正常路径
        }
        else if (cameraData.cameraType == CameraType.SceneView)
        {
            if (!m_ApplyToSceneView) return;
        }
        else
        {
            return;
        }

        float time = m_UseUnscaledTime ? Time.unscaledTime : Time.time;

        m_Pass.Setup(
            m_Material,
            new Vector4(m_Strength, Mathf.Max(1f, m_CloudScale), m_Threshold, Mathf.Max(0.001f, m_Softness)),
            new Vector4(
                Mathf.Clamp01(m_Layer2Weight),
                Mathf.Clamp(m_Layer2ScaleRatio, 0.05f, 1f),
                Mathf.Max(0f, m_Layer2SpeedRatio),
                (float)(int)m_DebugView),
            new Vector4(m_DriftSpeed.x, m_DriftSpeed.y, time, Mathf.Max(1f, m_Contrast)),
            m_ShadowColor,
            new Vector4(
                gapLightOn ? m_GapLightStrength : 0f,
                Mathf.Clamp01(m_GapLightThreshold),
                Mathf.Max(0.001f, m_GapLightSoftness),
                Mathf.Max(1f, m_GapLightContrast)),
            new Vector4(
                Mathf.Max(1f, m_GapLightStretch),
                // 度 → 弧度。shader 里用 (cosθ, sinθ) 当世界 XZ 平面上的光方向。
                m_GapLightAngleDegrees * Mathf.Deg2Rad,
                Mathf.Clamp(m_GapLightScaleRatio, 0.1f, 1f),
                0f),
            m_GapLightColor);

        renderer.EnqueuePass(m_Pass);
    }

    private void EnsureResources()
    {
        if (m_Material != null)
        {
            return;
        }

        Shader shader = m_Shader != null ? m_Shader : Shader.Find(kShaderName);
        if (shader == null)
        {
            return;
        }

        m_Material = CoreUtils.CreateEngineMaterial(shader);
    }

    /// <summary>
    /// 单次全屏 pass：读相机色 + 深度，写回一张同尺寸新纹理，最后把它设回 cameraColor。
    /// 用 AddRasterRenderPass（而非 UnsafePass）是因为只需要一次 Blit、不中途换渲染目标。
    /// </summary>
    private sealed class VHCloudShadowPass : ScriptableRenderPass
    {
        private static readonly int Params0Id = Shader.PropertyToID("_VH_CloudParams0");
        private static readonly int Params1Id = Shader.PropertyToID("_VH_CloudParams1");
        private static readonly int Params2Id = Shader.PropertyToID("_VH_CloudParams2");
        private static readonly int ColorId = Shader.PropertyToID("_VH_CloudColor");
        private static readonly int SourceSizeId = Shader.PropertyToID("_VH_CloudSourceSize");
        private static readonly int GapParams0Id = Shader.PropertyToID("_VH_GapParams0");
        private static readonly int GapParams1Id = Shader.PropertyToID("_VH_GapParams1");
        private static readonly int GapColorId = Shader.PropertyToID("_VH_GapColor");

        private Material m_Material;
        private Vector4 m_Params0;
        private Vector4 m_Params1;
        private Vector4 m_Params2;
        private Vector4 m_SourceSize;
        private Color m_ShadowColor = Color.white;
        private Vector4 m_GapParams0;
        private Vector4 m_GapParams1;
        private Color m_GapColor = Color.white;

        public VHCloudShadowPass()
        {
            requiresIntermediateTexture = true;
            profilingSampler = new ProfilingSampler("VH Cloud Shadow");
        }

        public void Setup(
            Material material,
            Vector4 cloudParams0,
            Vector4 cloudParams1,
            Vector4 cloudParams2,
            Color shadowColor,
            Vector4 gapParams0,
            Vector4 gapParams1,
            Color gapColor)
        {
            m_Material = material;
            m_Params0 = cloudParams0;
            m_Params1 = cloudParams1;
            m_Params2 = cloudParams2;
            m_ShadowColor = shadowColor;
            m_GapParams0 = gapParams0;
            m_GapParams1 = gapParams1;
            m_GapColor = gapColor;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (m_Material == null)
            {
                return;
            }

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();

            TextureHandle source = resourceData.cameraColor;
            if (!source.IsValid())
            {
                return;
            }

            // 没有深度图就没法反投影出世界坐标（PC_RPAsset 的 Require Depth Texture 必须打开）。
            TextureHandle depth = resourceData.cameraDepthTexture;
            if (!depth.IsValid())
            {
                return;
            }

            RenderTextureDescriptor cameraDescriptor = cameraData.cameraTargetDescriptor;
            int fullWidth = Mathf.Max(1, cameraDescriptor.width);
            int fullHeight = Mathf.Max(1, cameraDescriptor.height);
            m_SourceSize = new Vector4(fullWidth, fullHeight, 1f / fullWidth, 1f / fullHeight);

            TextureDesc destinationDesc = renderGraph.GetTextureDesc(source);
            destinationDesc.name = "_VH_CloudShadowResult";
            destinationDesc.depthBufferBits = DepthBits.None;
            destinationDesc.msaaSamples = MSAASamples.None;
            destinationDesc.clearBuffer = false;
            TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("VH Cloud Shadow", out PassData passData, profilingSampler))
            {
                passData.material = m_Material;
                passData.source = source;
                passData.depth = depth;
                passData.params0 = m_Params0;
                passData.params1 = m_Params1;
                passData.params2 = m_Params2;
                passData.sourceSize = m_SourceSize;
                passData.shadowColor = m_ShadowColor;
                passData.gapParams0 = m_GapParams0;
                passData.gapParams1 = m_GapParams1;
                passData.gapColor = m_GapColor;

                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(depth, AccessFlags.Read);
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    Material material = data.material;

                    material.SetVector(Params0Id, data.params0);
                    material.SetVector(Params1Id, data.params1);
                    material.SetVector(Params2Id, data.params2);
                    material.SetVector(SourceSizeId, data.sourceSize);
                    material.SetColor(ColorId, data.shadowColor);
                    material.SetVector(GapParams0Id, data.gapParams0);
                    material.SetVector(GapParams1Id, data.gapParams1);
                    material.SetColor(GapColorId, data.gapColor);

                    Blitter.BlitTexture(context.cmd, data.source, new Vector4(1f, 1f, 0f, 0f), material, 0);
                });
            }

            // 后续步骤（FXAA / 最终 Blit）从这里取画面
            resourceData.cameraColor = destination;
        }

        private class PassData
        {
            internal Material material;
            internal TextureHandle source;
            internal TextureHandle depth;
            internal Vector4 params0;
            internal Vector4 params1;
            internal Vector4 params2;
            internal Vector4 sourceSize;
            internal Color shadowColor;
            internal Vector4 gapParams0;
            internal Vector4 gapParams1;
            internal Color gapColor;
        }
    }
}
