using UnityEngine;

namespace VampireHunt.Presentation.Combat
{
    /// <summary>
    /// 撞击使魔的<b>拖尾</b>（速度线）。挂在剑的预制体（prefab）上，运行时自动在剑尾挂一个
    /// <see cref="TrailRenderer"/>；<b>冲刺 / 掉头时自动点亮，环绕待机时自动熄灭</b>。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>为什么拖尾是必需品</b>：撞击使魔 22 m/s、单程 0.24 s，且不自转 ——
    /// 剑在屏幕上是「一帧扎进去、下一帧穿出来」。没有拖尾玩家根本看不清剑飞过去了。
    /// </para>
    /// <para>
    /// <b>为什么用「整体拉伸」判断是否冲刺</b>：服务器控制器只在冲刺与掉头时把
    /// <c>localScale.z</c> 乘上 <c>stretchFactor</c>（默认 1.6），待机环绕时回到 1.0。
    /// 也就是说 <c>localScale.z</c> 本身就是「是否发起撞击」的权威信号，
    /// 表现层直接读它 ⇒ <b>零代码改动</b>即可接上，且客户端同样可用（不需要访问服务器状态）。
    /// </para>
    /// <para>
    /// ⚠️ 前提：本组件所在物体必须是<b>场景根物体</b>，或与拉伸物体的缩放同源
    /// （<c>ImpactFamiliarPresenter</c> 用 <c>Instantiate(prefab, pos, rot)</c> 生成，是根物体，符合）。
    /// 若将来把剑挂到别的缩放容器下，请显式指定 <see cref="scaleSource"/>。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BloodTrailVfx : MonoBehaviour
    {
        [Header("挂点")]
        [Tooltip("拖尾发点相对剑的局部坐标。剑尖朝 +Z（Unity），所以尾端是 -Z。中模净长 1.2 m ⇒ -0.55 约在柄头附近。")]
        [SerializeField] private Vector3 tailLocalPosition = new Vector3(0f, 0f, -0.55f);
        [Tooltip("读取拉伸比例的来源。留空 = 用 transform.root（根物体的 localScale.z）。")]
        [SerializeField] private Transform scaleSource;

        [Header("判定")]
        [Tooltip("localScale.z 超过此值即认为处于冲刺/掉头（stretchFactor = 1.6 时的推荐值）。")]
        [SerializeField, Min(1f)] private float engagedScaleThreshold = 1.08f;

        [Header("形态")]
        [Tooltip("拖尾存活时长（秒）。× 冲刺速度 = 拖尾长度：0.14 s × 22 m/s ≈ 3 m。")]
        [SerializeField, Min(0.02f)] private float trailTime = 0.14f;
        [Tooltip("冲刺时的拖尾宽度（米）。")]
        [SerializeField, Min(0f)] private float engagedWidth = 0.22f;
        [Tooltip("待机时是否保留细拖尾（0 = 完全熄灭，避免在玩家身边糊成一圈红）。")]
        [SerializeField, Min(0f)] private float idleWidth = 0f;
        [Tooltip("宽度过渡速度（1/秒）。太大会「啪」地弹出来。")]
        [SerializeField, Min(0.1f)] private float widthRampSpeed = 14f;
        [Tooltip("顶点间最小距离（米）。太小会堆顶点、太大拖尾会变折线。")]
        [SerializeField, Min(0.01f)] private float minVertexDistance = 0.06f;

        [Header("配色")]
        [SerializeField] private Color startColor = new Color(0.85f, 0.06f, 0.05f, 0.85f);
        [SerializeField] private Color endColor = new Color(0.35f, 0.01f, 0.02f, 0f);

        private TrailRenderer m_Trail;
        private Transform m_Source;
        private float m_Width;

        private void Awake()
        {
            EnsureTrail();
        }

        private void OnEnable()
        {
            EnsureTrail();
            if (m_Trail != null)
            {
                m_Trail.Clear();     // 重新生成/回收复用时不要拖着上一次的残影
                m_Trail.emitting = false;
            }
            m_Width = 0f;
        }

        private void LateUpdate()
        {
            if (m_Trail == null) return;
            if (m_Source == null) m_Source = scaleSource != null ? scaleSource : transform.root;

            bool engaged = m_Source != null && m_Source.lossyScale.z > engagedScaleThreshold;
            float target = engaged ? Mathf.Max(engagedWidth, idleWidth) : idleWidth;
            m_Width = Mathf.Lerp(m_Width, target, 1f - Mathf.Exp(-widthRampSpeed * Time.deltaTime));

            m_Trail.emitting = m_Width > 0.005f;
            m_Trail.startWidth = m_Width;
            m_Trail.endWidth = Mathf.Max(m_Width * 0.25f, 0.004f);
            m_Trail.startColor = startColor;
            m_Trail.endColor = endColor;
        }

        /// <summary>外部强制熄灭拖尾（例如死亡/回收时）。</summary>
        public void Stop()
        {
            if (m_Trail == null) return;
            m_Trail.Clear();
            m_Trail.emitting = false;
            m_Width = 0f;
        }

        private void EnsureTrail()
        {
            if (m_Trail != null) return;

            var marker = new GameObject("TrailMarker");
            marker.transform.SetParent(transform, false);
            marker.transform.localPosition = tailLocalPosition;
            marker.transform.localRotation = Quaternion.identity;

            m_Trail = marker.AddComponent<TrailRenderer>();
            m_Trail.sharedMaterial = TrailMaterial;
            m_Trail.time = trailTime;
            m_Trail.minVertexDistance = minVertexDistance;
            m_Trail.autodestruct = false;
            m_Trail.emitting = false;
            m_Trail.alignment = LineAlignment.View;   // 截面始终面向相机 ⇒ 读作「带状速度线」而不是细棍
            m_Trail.numCapVertices = 2;
            m_Trail.numCornerVertices = 2;
            m_Trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_Trail.receiveShadows = false;
            m_Trail.generateLightingData = false;
        }

        private void OnValidate()
        {
            if (m_Trail != null) m_Trail.time = trailTime;
        }

        private static Material s_TrailMaterial;

        /// <summary>
        /// 拖尾材质。必须用<b>吃顶点色</b>的着色器 —— 这条渐隐完全靠 TrailRenderer 的顶点色实现，
        /// URP/Unlit 不吃顶点色（渐变会失效整条一样红）。故对齐
        /// <see cref="LightningChainVisual"/> 的做法，优先 <c>Sprites/Default</c>。
        /// </summary>
        private static Material TrailMaterial
        {
            get
            {
                if (s_TrailMaterial != null) return s_TrailMaterial;
                Shader shader = Shader.Find("Sprites/Default")
                    ?? Shader.Find("Universal Render Pipeline/Unlit")
                    ?? Shader.Find("Unlit/Color")
                    ?? Shader.Find("Standard");
                s_TrailMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                return s_TrailMaterial;
            }
        }
    }
}
