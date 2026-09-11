using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

/// <summary>
/// 径向景深（前后 + 左右）· Vampire Hunt。
///
/// 解决的问题：URP 内置 Depth of Field（Gaussian 模式）只有一组 Start / End，
/// 数学上是一个各向同性的标量对，画面里所有方向共用同一条「由清晰到模糊」的曲线。
/// 而俯视角下「玩家身前可见 8.2m、身后只有 5.7m、左右上缘到 ±13.9m」，
/// 三个方向需要的虚化节奏本来就不同，却没法分开调。
///
/// 本 Feature 把「离玩家多远」拆成两条独立的轴，CoC 取二者较大值：
///   · 深度轴（前后）—— 分界线（玩家脚底那条屏幕水平线）以上走「前带」，
///       以下走「后带」，两组 Start / End 分开填，但 **共用一个强度**。
///   · 横轴（左右）—— 以玩家所在的那条屏幕竖线为原点，按「世界横向米数」虚化。
/// 两条轴都属于「玩家周围多大范围保持清晰」，所以调参口径统一为「米」。
///
/// 使用前提：把 Volume（SampleSceneProfile）里内置的 Depth of Field 关掉（mode = Off），
/// 否则会与本 Feature 叠加成双重虚化。
///
/// 为什么不能用内置的做：等深线在世界里是「z = 常数」的横线，与左右完全无关，
/// 画面中心离玩家 0.07m 的点与画面边缘离玩家 11.5m 的点会被赋予完全相同的模糊值。
/// </summary>
public sealed class VHSplitDepthOfFieldFeature : ScriptableRendererFeature
{
    /// <summary>调试视图档位。</summary>
    public enum DebugView
    {
        [InspectorName("关闭")] Off = 0,
        [InspectorName("中心线（红=前后分界 绿=左右中心）")] CenterLine = 1,
        [InspectorName("CoC 场可视化")] Coc = 2,
        [InspectorName("轴诊断（红=前后 蓝=左右 白=都糊 绿=清晰）")] AxisDiagnostics = 3
    }

    private const string kShaderName = "VH/SplitDepthOfField";
    private const int kDownSample = 2;
    private const float kMaxRadiusClamp = 4f;
    private const float kPlayerSearchInterval = 0.5f;

    [Header("总开关")]
    [Tooltip("关掉后完全不干预画面，可用来做开关对比。")]
    [SerializeField] private bool m_Enabled = true;

    [Tooltip("着色器（一般留空即可，会自动按名字查找 VH/SplitDepthOfField）。")]
    [SerializeField] private Shader m_Shader;

    [Header("中心（玩家脚底）")]
    [Tooltip("自动用本地玩家的脚底位置作为中心（推荐）。多人下每个客户端各看各的玩家。")]
    [SerializeField] private bool m_AutoTrackPlayer = true;

    [Tooltip("找不到玩家时的兜底：中心水平线的屏幕高度。0 = 画面下边缘，1 = 画面上边缘。决定「前后」的原点。")]
    [SerializeField, Range(0f, 1f)] private float m_SplitScreenY = 0.5f;

    [Tooltip("找不到玩家时的兜底：中心竖线的屏幕横坐标。0 = 画面左边缘，1 = 画面右边缘。决定「左右」的原点。")]
    [SerializeField, Range(0f, 1f)] private float m_SplitScreenX = 0.5f;

    [Tooltip("分界线附近的平滑过渡宽度（占屏幕高度的比例）。0 = 硬边，前后两条带衔接处可能出现可见接缝。")]
    [SerializeField, Range(0f, 0.3f)] private float m_SplitSoftness = 0.02f;

    [Tooltip("玩家原点距脚底的高度差（米）。玩家 pivot 不在脚下时用这个补偿。")]
    [SerializeField] private float m_FeetHeightOffset;

    [Tooltip("上下反转（标定开关）。默认已按 D3D12 实机标定为正确方向；换图形 API 后若发现前/后两组效果与预期相反，勾选此项。左右不受影响。")]
    [SerializeField] private bool m_InvertScreenAxis;

    [Header("深度轴 · 前后（两组起止点 + 一个共用强度）")]
    [Tooltip("身前（屏幕上侧）：从玩家脚底往前多少米开始虚化。0 = 紧贴玩家脚底就开始。")]
    [SerializeField] private float m_FrontStart = 0.6f;

    [Tooltip("身前：往前多少米虚化饱和（达到最大模糊）。必须大于 Start。")]
    [SerializeField] private float m_FrontEnd = 2.4f;

    [Tooltip("身后（屏幕下侧）：从玩家脚底往后多少米开始虚化。")]
    [SerializeField] private float m_BackStart = 0.6f;

    [Tooltip("身后：往后多少米虚化饱和。必须大于 Start。")]
    [SerializeField] private float m_BackEnd = 2.4f;

    [Tooltip("前后共用的虚化强度 0~1。一个滑杆控制「身前 + 身后」的整体糊度，两组起止点仍各自独立。")]
    [SerializeField, Range(0f, 1f)] private float m_DepthStrength = 1f;

    [Header("横轴 · 左右")]
    [Tooltip("以玩家所在竖线为原点，横向多少米开始虚化。画面正中一列永久清晰，越往画面左右两侧越糊。")]
    [SerializeField] private float m_LateralStart = 7f;

    [Tooltip("横向多少米虚化饱和。必须大于 Start。可调上限 ≈ 13.9m（画面左右上角就是这个世界距离），填更大等于永远糊不满。")]
    [SerializeField] private float m_LateralEnd = 13f;

    [Tooltip("左右虚化强度 0~1。默认 1 = 生效；拉到 0 即关掉横轴，退化成只做前后两组。")]
    [SerializeField, Range(0f, 1f)] private float m_LateralStrength = 1f;

    [Header("模糊")]
    [Tooltip("最大模糊半径。0.9 ≈ 内置景深的默认观感，往大调更糊。")]
    [SerializeField, Range(0.1f, 3f)] private float m_MaxBlurRadius = 0.9f;

    [Header("调试")]
    [Tooltip("调试视图。CoC 场可视化会把「哪里糊、糊多少」直接画成灰度图（黑=清晰、白=完全模糊），单帧就能判断两条轴的起止点与方向是否正确。")]
    [SerializeField] private DebugView m_DebugView = DebugView.Off;

    [Tooltip("是否也作用于 Scene 视图。")]
    [SerializeField] private bool m_ApplyToSceneView;

    private Material m_Material;
    private VHSplitDofPass m_Pass;

    private Transform m_PlayerTransform;
    private float m_NextPlayerSearchTime;

    public override void Create()
    {
        m_Pass = new VHSplitDofPass
        {
            // ⚠ 必须正好是 AfterRenderingPostProcessing（600），不能学内置景深写 - 1。
            // 原因（URP 17 源码 UniversalRendererRenderGraph.cs:1374/1403）：
            //   hasPassesAfterPostProcessing = 队列里存在 renderPassEvent ∈ [600,1000) 的 pass
            //   isTargetBackbuffer = resolveFinalTarget && !applyFinalPostProcessing
            //                        && !hasPassesAfterPostProcessing && !hasCaptureActions
            // 若本 pass 落在 599（∈[550,600)），hasPassesAfterPostProcessing 为 false →
            // isTargetBackbuffer 为 true → URP 会在跑后处理前调 SwitchActiveTexturesToBackbuffer()，
            // 把 resourceData.cameraColor 整个切回后备缓冲，本 pass 写回的 destination 被直接丢弃，
            // 表现为「pass 明明跑了，画面却毫无变化」。
            // 取 600 后：后处理改为写中间纹理，本 pass 在后处理之后记录与执行，
            // 再由 FinalBlit 把结果呈现到屏幕（附带 FXAA 也在其后执行）。
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
        if (!m_Enabled)
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

        // ---- 中心点 ----
        float splitScreenY = m_SplitScreenY;
        float centerX = m_SplitScreenX;
        float splitViewZ = 0f;
        bool useSampledSplit = true;

        if (m_AutoTrackPlayer && TryGetPlayerFeet(out Vector3 feet))
        {
            // ViewportPoint：y 的 0 = 画面下边缘、1 = 画面上边缘（与平台无关）；
            //                 x 的 0 = 画面左边缘、1 = 画面右边缘。
            Vector3 viewport = camera.WorldToViewportPoint(feet);
            splitScreenY = viewport.y;
            centerX = viewport.x;

            // 相机深度 = 玩家脚底在相机朝向上的投影
            Vector3 cameraPosition = camera.transform.position;
            Vector3 cameraForward = camera.transform.forward;
            splitViewZ = Vector3.Dot(feet - cameraPosition, cameraForward);
            useSampledSplit = false;
        }

        // 屏幕上下方向（已实测标定，见 Assets/Render/VH_SplitDepthOfField.shader 顶部注释）：
        // URP RenderGraph 的 Blitter 全屏三角形在 D3D12 上，shader 里 uv.y = 0 就是画面「下」边缘、
        // uv.y = 1 是画面「上」边缘 —— 与 UnityEngine 视口坐标同向，因此常态不翻转（传 0）。
        // 实测方法：把 m_SplitScreenY 设成 0.25（非对称），诊断视图里那条绿色过渡带
        // 落在画面 25% 高度处即正确、落在 75% 处即上下颠倒。
        // ⚠ 不要用 SystemInfo.graphicsUVStartsAtTop 推：它描述「纹理 V 轴起点是否在顶部」（D3D 为 true），
        // 与 Blitter 实际给出的 texcoord 不是一回事，直接套用会把上下弄反。
        // 横向不需要这套：左右用 |uv.x - 中心x| 取绝对值，天然对称。
        float uvTopDown = m_InvertScreenAxis ? 1f : 0f;

        // 把「离玩家竖线多少屏幕距离」换算成世界横向米数所需的系数。
        // 相机只有 yaw + pitch、没有 roll ⇒ 相机 right 向量始终躺在世界水平面内，
        // 相机空间的横向距离就是玩家的「左右」距离：
        //     ndcX = x_cam / (tanHalfFovX * z_cam)  ⇒  x_cam = ndcX * tanHalfFovX * viewZ
        float tanHalfFovY = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float tanHalfFovX = tanHalfFovY * Mathf.Max(0.0001f, camera.aspect);

        m_Pass.Setup(
            m_Material,
            splitScreenY,
            centerX,
            splitViewZ,
            useSampledSplit,
            uvTopDown,
            m_SplitSoftness,
            m_FrontStart,
            m_FrontEnd,
            m_BackStart,
            m_BackEnd,
            m_DepthStrength,
            m_LateralStart,
            m_LateralEnd,
            m_LateralStrength,
            tanHalfFovX,
            m_MaxBlurRadius,
            (float)(int)m_DebugView,
            kDownSample,
            kMaxRadiusClamp);

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

    /// <summary>取本地玩家脚底的世界坐标。多人下必须是本地玩家，否则景深中心会错位。</summary>
    private bool TryGetPlayerFeet(out Vector3 feet)
    {
        feet = default;

        if (m_PlayerTransform == null || !m_PlayerTransform.gameObject.activeInHierarchy)
        {
            m_PlayerTransform = null;

            if (Time.unscaledTime < m_NextPlayerSearchTime)
            {
                return false;
            }

            m_NextPlayerSearchTime = Time.unscaledTime + kPlayerSearchInterval;

            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsListening)
            {
                return false;
            }

            NetworkClient localClient = networkManager.LocalClient;
            if (localClient == null)
            {
                return false;
            }

            NetworkObject playerObject = localClient.PlayerObject;
            if (playerObject == null || !playerObject.IsSpawned)
            {
                return false;
            }

            m_PlayerTransform = playerObject.transform;
        }

        feet = m_PlayerTransform.position + Vector3.up * m_FeetHeightOffset;
        return true;
    }

    /// <summary>
    /// 一次 RenderGraph pass 内顺序跑 5 个子步骤（CoC → 降采样 → 水平模糊 → 垂直模糊 → 合成），
    /// 与 URP 内置景深同构。用 UnsafePass 是因为需要在 pass 内自行切换渲染目标。
    /// </summary>
    private sealed class VHSplitDofPass : ScriptableRenderPass
    {
        private const int kPassCoC = 0;
        private const int kPassDownsample = 1;
        private const int kPassBlurH = 2;
        private const int kPassBlurV = 3;
        private const int kPassComposite = 4;

        private static readonly int BlurTextureId = Shader.PropertyToID("_VH_BlurTexture");
        private static readonly int FullCoCTextureId = Shader.PropertyToID("_VH_FullCoCTexture");
        private static readonly int SourceSizeId = Shader.PropertyToID("_VH_SourceSize");
        private static readonly int DownSampleScaleFactorId = Shader.PropertyToID("_VH_DownSampleScaleFactor");
        private static readonly int DofParamsId = Shader.PropertyToID("_VH_DofParams");
        private static readonly int FrontBandId = Shader.PropertyToID("_VH_FrontBand");
        private static readonly int BackBandId = Shader.PropertyToID("_VH_BackBand");
        private static readonly int LateralBandId = Shader.PropertyToID("_VH_LateralBand");
        private static readonly int SplitParamsId = Shader.PropertyToID("_VH_SplitParams");
        private static readonly int CenterParamsId = Shader.PropertyToID("_VH_CenterParams");
        private static readonly int CameraDepthTextureId = Shader.PropertyToID("_CameraDepthTexture");

        private Material m_Material;
        private Vector4 m_DofParams;
        private Vector4 m_FrontBand;
        private Vector4 m_BackBand;
        private Vector4 m_LateralBand;
        private Vector4 m_SplitParams;
        private Vector4 m_CenterParams;
        private float m_MaxRadius = 0.9f;
        private float m_MaxRadiusClamp = 4f;
        private int m_DownSample = 2;

        public VHSplitDofPass()
        {
            requiresIntermediateTexture = true;
            profilingSampler = new ProfilingSampler("VH Split Depth Of Field");
        }

        public void Setup(
            Material material,
            float splitScreenY,
            float centerX,
            float splitViewZ,
            bool useSampledSplit,
            float uvTopDown,
            float splitSoftness,
            float frontStart,
            float frontEnd,
            float backStart,
            float backEnd,
            float depthStrength,
            float lateralStart,
            float lateralEnd,
            float lateralStrength,
            float tanHalfFovX,
            float maxBlurRadius,
            float debugMode,
            int downSample,
            float maxRadiusClamp)
        {
            m_Material = material;
            m_DownSample = Mathf.Max(1, downSample);
            m_MaxRadius = maxBlurRadius;
            m_MaxRadiusClamp = maxRadiusClamp;

            m_DofParams = new Vector4(
                splitViewZ,
                m_MaxRadius,
                uvTopDown,
                useSampledSplit ? 1f : 0f);

            m_FrontBand = new Vector4(frontStart, frontEnd, 0f, 0f);
            m_BackBand = new Vector4(backStart, backEnd, 0f, 0f);
            m_LateralBand = new Vector4(lateralStart, lateralEnd, lateralStrength, 0f);
            m_SplitParams = new Vector4(
                Mathf.Clamp01(splitScreenY),
                Mathf.Max(0f, splitSoftness),
                debugMode,
                Mathf.Clamp01(depthStrength));
            m_CenterParams = new Vector4(
                Mathf.Clamp01(centerX),
                Mathf.Max(0.0001f, tanHalfFovX),
                0f,
                0f);
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

            TextureHandle depth = resourceData.cameraDepthTexture;
            if (!depth.IsValid())
            {
                // 没有深度图就无法算景深（PC_RPAsset 的 Require Depth Texture 必须打开）
                return;
            }

            RenderTextureDescriptor cameraDescriptor = cameraData.cameraTargetDescriptor;
            int fullWidth = Mathf.Max(1, cameraDescriptor.width);
            int fullHeight = Mathf.Max(1, cameraDescriptor.height);
            int halfWidth = Mathf.Max(1, fullWidth / m_DownSample);
            int halfHeight = Mathf.Max(1, fullHeight / m_DownSample);

            TextureDesc sourceDesc = renderGraph.GetTextureDesc(source);
            var colorFormat = sourceDesc.colorFormat;

            // A 分辨率无关的基准：与 URP 内置景深一致，按宽度相对 1080p 缩放半径
            m_DofParams.y = Mathf.Min(m_MaxRadius * (halfWidth / 1080f), m_MaxRadiusClamp);

            TextureHandle fullCoC = CreateTexture(
                renderGraph, sourceDesc, "_VH_FullCoCTexture",
                fullWidth, fullHeight,
                UnityEngine.Experimental.Rendering.GraphicsFormat.R16_UNorm);

            TextureHandle ping = CreateTexture(
                renderGraph, sourceDesc, "_VH_PingTexture",
                halfWidth, halfHeight, colorFormat);

            TextureHandle pong = CreateTexture(
                renderGraph, sourceDesc, "_VH_PongTexture",
                halfWidth, halfHeight, colorFormat);

            TextureHandle destination = CreateTexture(
                renderGraph, sourceDesc, "_VH_SplitDepthOfFieldResult",
                fullWidth, fullHeight, colorFormat);

            using (var builder = renderGraph.AddUnsafePass<PassData>("VH Split Depth Of Field", out PassData passData, profilingSampler))
            {
                passData.material = m_Material;
                passData.sourceTexture = source;
                passData.depthTexture = depth;
                passData.fullCoCTexture = fullCoC;
                passData.pingTexture = ping;
                passData.pongTexture = pong;
                passData.destination = destination;

                passData.sourceSize = new Vector4(fullWidth, fullHeight, 1f / fullWidth, 1f / fullHeight);
                passData.downSampleScaleFactor = new Vector4(
                    1f / m_DownSample, 1f / m_DownSample, m_DownSample, m_DownSample);
                passData.dofParams = m_DofParams;
                passData.frontBand = m_FrontBand;
                passData.backBand = m_BackBand;
                passData.lateralBand = m_LateralBand;
                passData.splitParams = m_SplitParams;
                passData.centerParams = m_CenterParams;

                builder.UseTexture(source, AccessFlags.Read);
                builder.UseTexture(depth, AccessFlags.Read);
                builder.UseTexture(fullCoC, AccessFlags.ReadWrite);
                builder.UseTexture(ping, AccessFlags.ReadWrite);
                builder.UseTexture(pong, AccessFlags.ReadWrite);
                builder.UseTexture(destination, AccessFlags.Write);

                builder.SetRenderFunc(static (PassData data, UnsafeGraphContext context) =>
                {
                    CommandBuffer cmd = CommandBufferHelpers.GetNativeCommandBuffer(context.cmd);
                    Material material = data.material;

                    material.SetVector(SourceSizeId, data.sourceSize);
                    material.SetVector(DownSampleScaleFactorId, data.downSampleScaleFactor);
                    material.SetVector(DofParamsId, data.dofParams);
                    material.SetVector(FrontBandId, data.frontBand);
                    material.SetVector(BackBandId, data.backBand);
                    material.SetVector(LateralBandId, data.lateralBand);
                    material.SetVector(SplitParamsId, data.splitParams);
                    material.SetVector(CenterParamsId, data.centerParams);

                    // 1) CoC：全分辨率，写单通道
                    material.SetTexture(CameraDepthTextureId, data.depthTexture);
                    Blitter.BlitCameraTexture(cmd, data.sourceTexture, data.fullCoCTexture, material, kPassCoC);

                    // 2) 降采样到半分辨率
                    Blitter.BlitCameraTexture(cmd, data.sourceTexture, data.pingTexture, material, kPassDownsample);

                    // 3) 水平模糊
                    material.SetTexture(FullCoCTextureId, data.fullCoCTexture);
                    Blitter.BlitCameraTexture(cmd, data.pingTexture, data.pongTexture, material, kPassBlurH);

                    // 4) 垂直模糊
                    Blitter.BlitCameraTexture(cmd, data.pongTexture, data.pingTexture, material, kPassBlurV);

                    // 5) 合成：清晰原图 + 模糊图，按 CoC 混合
                    material.SetTexture(BlurTextureId, data.pingTexture);
                    Blitter.BlitCameraTexture(cmd, data.sourceTexture, data.destination, material, kPassComposite);
                });
            }

            // 后续步骤（FXAA / 最终 Blit）从这里取画面
            resourceData.cameraColor = destination;
        }

        private static TextureHandle CreateTexture(
            RenderGraph renderGraph,
            TextureDesc sourceDesc,
            string name,
            int width,
            int height,
            UnityEngine.Experimental.Rendering.GraphicsFormat format)
        {
            TextureDesc descriptor = sourceDesc;

            descriptor.name = name;
            descriptor.sizeMode = TextureSizeMode.Explicit;
            descriptor.width = width;
            descriptor.height = height;
            descriptor.scale = Vector2.one;
            descriptor.func = null;
            descriptor.colorFormat = format;
            descriptor.depthBufferBits = DepthBits.None;
            descriptor.msaaSamples = MSAASamples.None;
            descriptor.bindTextureMS = false;
            descriptor.useDynamicScale = false;
            descriptor.useDynamicScaleExplicit = false;
            descriptor.useMipMap = false;
            descriptor.autoGenerateMips = false;
            descriptor.filterMode = FilterMode.Bilinear;
            descriptor.wrapMode = TextureWrapMode.Clamp;
            descriptor.clearBuffer = false;

            return renderGraph.CreateTexture(descriptor);
        }

        private class PassData
        {
            internal Material material;
            internal TextureHandle sourceTexture;
            internal TextureHandle depthTexture;
            internal TextureHandle fullCoCTexture;
            internal TextureHandle pingTexture;
            internal TextureHandle pongTexture;
            internal TextureHandle destination;
            internal Vector4 sourceSize;
            internal Vector4 downSampleScaleFactor;
            internal Vector4 dofParams;
            internal Vector4 frontBand;
            internal Vector4 backBand;
            internal Vector4 lateralBand;
            internal Vector4 splitParams;
            internal Vector4 centerParams;
        }
    }
}
