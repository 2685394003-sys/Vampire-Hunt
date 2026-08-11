using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class VolumeCloud : ScriptableRendererFeature
{
    // 测试切换使用
    public static VolumeCloud Oneself;

    // 分帧渲染的块数
    public enum FrameBlock
    {
        _Off = 1,
        _2x2 = 4,
        _4x4 = 16
    }

    [System.Serializable]
    public class Setting
    {
        // 后处理材质
        public Material CloudMaterial;

        // 渲染队列
        public RenderPassEvent RenderQueue =
            RenderPassEvent.AfterRenderingSkybox;

        // 蓝噪声
        public Texture2D BlueNoiseTex;

        // 分辨率缩放
        [Range(0.1f, 1)]
        public float RTScale = 0.5f;

        // 分帧渲染
        public FrameBlock FrameBlocking = FrameBlock._4x4;

        // 屏蔽相机分辨率宽度（受纹理缩放影响）
        [Range(100, 600)]
        public int ShieldWidth = 400;

        // 是否开启分帧测试
        public bool IsFrameDebug = false;

        // 分帧测试
        [Range(1, 16)]
        public int FrameDebug = 1;
    }

    class VolumeCloudRenderPass : ScriptableRenderPass
    {
        public Setting Set;
        public string passName;

        // 云渲染纹理，通过两张纹理相互迭代完成分帧渲染
        public RenderTexture[] cloudTex;

        // 云纹理宽度
        public int width;

        // 云纹理高度
        public int height;

        // 帧计数
        public int frameCount;

        // 纹理切换
        public int rtSwitch;

        public VolumeCloudRenderPass(Setting set, string name)
        {
            renderPassEvent = set.RenderQueue;
            Set = set;
            passName = name;
            frameCount = 0;
        }

        public override void Execute(
            ScriptableRenderContext context,
            ref RenderingData renderingData)
        {
            if (Set.CloudMaterial == null || Set.BlueNoiseTex == null)
                return;

            CommandBuffer cmd = CommandBufferPool.Get(passName);

            // 新版URP必须在ScriptableRenderPass执行期间获取相机渲染目标
            RTHandle cameraColorTex =
                renderingData.cameraData.renderer.cameraColorTargetHandle;

            // 设置材质参数
            Set.CloudMaterial.SetTexture(
                "_BlueNoiseTex",
                Set.BlueNoiseTex);

            Set.CloudMaterial.SetVector(
                "_BlueNoiseTexUV",
                new Vector4(
                    (float)width / Set.BlueNoiseTex.width,
                    (float)height / Set.BlueNoiseTex.height,
                    0,
                    0));

            Set.CloudMaterial.SetInt("_Width", width - 1);
            Set.CloudMaterial.SetInt("_Height", height - 1);
            Set.CloudMaterial.SetInt("_FrameCount", frameCount);

            if (Set.FrameBlocking == FrameBlock._Off)
            {
                Set.CloudMaterial.EnableKeyword("_OFF");
                Set.CloudMaterial.DisableKeyword("_2X2");
                Set.CloudMaterial.DisableKeyword("_4X4");
            }
            else if (Set.FrameBlocking == FrameBlock._2x2)
            {
                Set.CloudMaterial.DisableKeyword("_OFF");
                Set.CloudMaterial.EnableKeyword("_2X2");
                Set.CloudMaterial.DisableKeyword("_4X4");
            }
            else if (Set.FrameBlocking == FrameBlock._4x4)
            {
                Set.CloudMaterial.DisableKeyword("_OFF");
                Set.CloudMaterial.DisableKeyword("_2X2");
                Set.CloudMaterial.EnableKeyword("_4X4");
            }

            // 不开启分帧渲染时，创建临时渲染纹理
            if (Set.FrameBlocking == FrameBlock._Off)
            {
                RenderTextureDescriptor temporaryDescriptor =
                    new RenderTextureDescriptor(
                        width,
                        height,
                        RenderTextureFormat.ARGB32);

                temporaryDescriptor.depthBufferBits = 0;

                int temporaryTextureID =
                    Shader.PropertyToID("_CloudTex");

                cmd.GetTemporaryRT(
                    temporaryTextureID,
                    temporaryDescriptor);

                cmd.Blit(
                    cameraColorTex,
                    temporaryTextureID,
                    Set.CloudMaterial,
                    0);

                cmd.Blit(
                    temporaryTextureID,
                    cameraColorTex,
                    Set.CloudMaterial,
                    1);

                context.ExecuteCommandBuffer(cmd);

                cmd.ReleaseTemporaryRT(temporaryTextureID);
            }
            else
            {
                // 防止纹理尚未创建时出现空引用
                if (cloudTex == null ||
                    cloudTex.Length < 2 ||
                    cloudTex[0] == null ||
                    cloudTex[1] == null)
                {
                    CommandBufferPool.Release(cmd);
                    return;
                }

                cmd.Blit(
                    cloudTex[rtSwitch % 2],
                    cloudTex[(rtSwitch + 1) % 2],
                    Set.CloudMaterial,
                    0);

                cmd.Blit(
                    cloudTex[(rtSwitch + 1) % 2],
                    cameraColorTex,
                    Set.CloudMaterial,
                    1);

                context.ExecuteCommandBuffer(cmd);
            }

            CommandBufferPool.Release(cmd);
        }
    }

    private VolumeCloudRenderPass cloudPass;

    public Setting Set = new Setting();

    // 游戏窗口使用的云纹理
    private RenderTexture[] _cloudTexGame =
        new RenderTexture[2];

    // Scene窗口使用的云纹理
    private RenderTexture[] _cloudTexSceneView =
        new RenderTexture[2];

    // 上一次纹理分辨率
    private int _widthGame;
    private int _heightGame;
    private int _widthSceneView;
    private int _heightSceneView;

    // 当前帧数
    private int _frameCountGame;
    private int _frameCountSceneView;

    // 纹理切换
    private int _rtSwitchGame;
    private int _rtSwitchSceneView;

    // 上一次分帧测试数值
    private int _frameDebug = 1;

    public override void Create()
    {
        Oneself = this;
        cloudPass = new VolumeCloudRenderPass(Set, name);
    }

    public override void AddRenderPasses(
        ScriptableRenderer renderer,
        ref RenderingData renderingData)
    {
        CameraType cameraType =
            renderingData.cameraData.cameraType;

        bool supportedCamera =
            cameraType == CameraType.Game ||
            cameraType == CameraType.SceneView;

        if (Set.CloudMaterial == null || !supportedCamera)
            return;

        // 云纹理分辨率
        int width = Mathf.Max(
            1,
            (int)(
                renderingData.cameraData
                    .cameraTargetDescriptor.width *
                Set.RTScale));

        int height = Mathf.Max(
            1,
            (int)(
                renderingData.cameraData
                    .cameraTargetDescriptor.height *
                Set.RTScale));

        // 不进行分帧渲染
        if (Set.FrameBlocking == FrameBlock._Off)
        {
            ReleaseCloudTextures(ref _cloudTexGame);
            ReleaseCloudTextures(ref _cloudTexSceneView);

            cloudPass.width = width;
            cloudPass.height = height;

            renderer.EnqueuePass(cloudPass);
            return;
        }

        // 分帧调试
        if (Set.IsFrameDebug)
        {
            if (Set.FrameDebug != _frameDebug)
            {
                ReleaseCloudTextures(ref _cloudTexGame);
                ReleaseCloudTextures(ref _cloudTexSceneView);
            }

            _frameDebug = Set.FrameDebug;

            _frameCountGame %= Set.FrameDebug;
            _frameCountSceneView %= Set.FrameDebug;
        }

        // 游戏视口
        if (cameraType == CameraType.Game)
        {
            // 屏蔽相机右下角低分辨率预览窗口
            if (width < Set.ShieldWidth)
                return;

            EnsureCloudTextures(
                ref _cloudTexGame,
                width,
                height,
                ref _widthGame,
                ref _heightGame);

            cloudPass.cloudTex = _cloudTexGame;
            cloudPass.width = _widthGame;
            cloudPass.height = _heightGame;
            cloudPass.frameCount = _frameCountGame;
            cloudPass.rtSwitch = _rtSwitchGame;

            _rtSwitchGame = (_rtSwitchGame + 1) % 2;

            _frameCountGame =
                (_frameCountGame + 1) %
                (int)Set.FrameBlocking;
        }
        else
        {
            // Scene视口
            EnsureCloudTextures(
                ref _cloudTexSceneView,
                width,
                height,
                ref _widthSceneView,
                ref _heightSceneView);

            cloudPass.cloudTex = _cloudTexSceneView;
            cloudPass.width = _widthSceneView;
            cloudPass.height = _heightSceneView;
            cloudPass.frameCount = _frameCountSceneView;
            cloudPass.rtSwitch = _rtSwitchSceneView;

            _rtSwitchSceneView =
                (_rtSwitchSceneView + 1) % 2;

            _frameCountSceneView =
                (_frameCountSceneView + 1) %
                (int)Set.FrameBlocking;
        }

        renderer.EnqueuePass(cloudPass);
    }

    private static void EnsureCloudTextures(
        ref RenderTexture[] textures,
        int width,
        int height,
        ref int previousWidth,
        ref int previousHeight)
    {
        bool resolutionChanged =
            previousWidth != width ||
            previousHeight != height;

        if (resolutionChanged)
            ReleaseCloudTextures(ref textures);

        if (textures == null || textures.Length != 2)
            textures = new RenderTexture[2];

        for (int i = 0; i < textures.Length; i++)
        {
            if (textures[i] != null)
                continue;

            textures[i] = RenderTexture.GetTemporary(
                width,
                height,
                0,
                RenderTextureFormat.ARGB32);
        }

        previousWidth = width;
        previousHeight = height;
    }

    private static void ReleaseCloudTextures(
        ref RenderTexture[] textures)
    {
        if (textures != null)
        {
            for (int i = 0; i < textures.Length; i++)
            {
                if (textures[i] == null)
                    continue;

                RenderTexture.ReleaseTemporary(textures[i]);
                textures[i] = null;
            }
        }

        textures = new RenderTexture[2];
    }

    protected override void Dispose(bool disposing)
    {
        ReleaseCloudTextures(ref _cloudTexGame);
        ReleaseCloudTextures(ref _cloudTexSceneView);

        if (Oneself == this)
            Oneself = null;

        base.Dispose(disposing);
    }
}