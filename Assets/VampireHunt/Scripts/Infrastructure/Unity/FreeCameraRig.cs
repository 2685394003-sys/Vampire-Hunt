using UnityEngine;
using UnityEngine.InputSystem;

namespace VampireHunt.Infrastructure.Unity
{
    /// <summary>
    /// 自由飞行相机（Fly Cam）。
    ///
    /// 职责单一：读取自身输入 → 驱动挂载的 Transform。
    /// 不引用任何游戏系统，也不关心「谁在什么时机启用它」——启用即接管，禁用即停手。
    /// 因此它可以被任意系统复用（观察模式、回放、编辑器工具），不会产生反向依赖。
    ///
    /// 操作方式：
    ///   W / A / S / D  沿当前水平朝向平移（只在水平面移动，不受俯仰角影响）
    ///   空格           升高
    ///   Ctrl           下降
    ///   Shift          加速
    ///   鼠标右键       按住拖动转视角（可关闭）
    ///   滚轮           调整速度倍率
    ///
    /// 注意：本组件使用 Time.unscaledDeltaTime。
    ///       单人「停止时间」（Time.timeScale = 0）下相机依然可以自由移动，
    ///       若改用 Time.deltaTime 则会被冻住 —— 这是本功能最容易踩的坑。
    /// </summary>
    public class FreeCameraRig : MonoBehaviour
    {
        #region 参数

        [Header("速度")]
        [Tooltip("水平移动速度（米/秒）。")]
        [SerializeField] private float moveSpeed = 15f;
        [Tooltip("升降速度（米/秒）。")]
        [SerializeField] private float verticalSpeed = 10f;
        [Tooltip("按住 Shift 时的速度倍数。")]
        [SerializeField] private float boostMultiplier = 3f;
        [Tooltip("滚轮可调的速度倍率下限。")]
        [SerializeField] private float minSpeedMultiplier = 0.2f;
        [Tooltip("滚轮可调的速度倍率上限。")]
        [SerializeField] private float maxSpeedMultiplier = 10f;

        [Header("视角")]
        [Tooltip("是否允许按住鼠标右键拖动转视角。")]
        [SerializeField] private bool allowMouseLook = true;
        [Tooltip("鼠标灵敏度。")]
        [SerializeField] private float lookSensitivity = 0.15f;
        [Tooltip("俯仰角限制（度），防止镜头翻过头。")]
        [SerializeField] private float pitchLimit = 89f;

        #endregion

        #region 内部状态

        private InputAction m_Move;
        private InputAction m_Vertical;
        private InputAction m_Boost;
        private InputAction m_LookHold;
        private InputAction m_LookDelta;
        private InputAction m_Scroll;

        private float m_Yaw;
        private float m_Pitch;
        private float m_SpeedMultiplier = 1f;

        #endregion

        #region Unity 生命周期

        private void Awake()
        {
            // 输入全部在代码内定义，不依赖 InputActions 资产，避免与玩家的 Action Map 互相污染。
            m_Move = new InputAction("FreeCamMove", InputActionType.Value);
            m_Move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");

            m_Vertical = new InputAction("FreeCamVertical", InputActionType.Value);
            m_Vertical.AddCompositeBinding("1DAxis")
                .With("Positive", "<Keyboard>/space")
                .With("Negative", "<Keyboard>/leftCtrl")
                .With("Negative", "<Keyboard>/rightCtrl");

            m_Boost = new InputAction("FreeCamBoost", InputActionType.Button, "<Keyboard>/leftShift");

            m_LookHold = new InputAction("FreeCamLookHold", InputActionType.Button, "<Mouse>/rightButton");
            m_LookDelta = new InputAction("FreeCamLookDelta", InputActionType.Value, "<Mouse>/delta");
            m_Scroll = new InputAction("FreeCamScroll", InputActionType.Value, "<Mouse>/scroll/y");
        }

        private void OnEnable()
        {
            if (m_Move == null) return;

            // 接管瞬间用当前朝向初始化角度，避免镜头跳变。
            Vector3 euler = transform.eulerAngles;
            m_Yaw = euler.y;
            m_Pitch = euler.x > 180f ? euler.x - 360f : euler.x;

            m_Move.Enable();
            m_Vertical.Enable();
            m_Boost.Enable();
            m_LookHold.Enable();
            m_LookDelta.Enable();
            m_Scroll.Enable();
        }

        private void OnDisable()
        {
            if (m_Move == null) return;

            m_Move.Disable();
            m_Vertical.Disable();
            m_Boost.Disable();
            m_LookHold.Disable();
            m_LookDelta.Disable();
            m_Scroll.Disable();
        }

        private void OnDestroy()
        {
            if (m_Move == null) return;

            m_Move.Dispose();
            m_Vertical.Dispose();
            m_Boost.Dispose();
            m_LookHold.Dispose();
            m_LookDelta.Dispose();
            m_Scroll.Dispose();
        }

        private void LateUpdate()
        {
            // 用 unscaledDeltaTime：单人停止时间（timeScale = 0）下相机仍需可动。
            float deltaTime = Time.unscaledDeltaTime;
            if (deltaTime <= 0f) return;

            UpdateLook();
            UpdateSpeed();
            UpdateMove(deltaTime);
        }

        #endregion

        #region 移动与视角

        private void UpdateLook()
        {
            if (!allowMouseLook) return;
            if (!m_LookHold.IsPressed()) return;

            Vector2 delta = m_LookDelta.ReadValue<Vector2>() * lookSensitivity;
            m_Yaw += delta.x;
            m_Pitch = Mathf.Clamp(m_Pitch - delta.y, -pitchLimit, pitchLimit);

            transform.rotation = Quaternion.Euler(m_Pitch, m_Yaw, 0f);
        }

        private void UpdateSpeed()
        {
            float scroll = m_Scroll.ReadValue<float>();
            if (Mathf.Abs(scroll) < 0.01f) return;

            m_SpeedMultiplier = Mathf.Clamp(
                m_SpeedMultiplier * (1f + scroll * 0.1f),
                minSpeedMultiplier,
                maxSpeedMultiplier);
        }

        private void UpdateMove(float deltaTime)
        {
            Vector2 move = m_Move.ReadValue<Vector2>();
            float vertical = m_Vertical.ReadValue<float>();

            if (move.sqrMagnitude < 0.0001f && Mathf.Abs(vertical) < 0.0001f) return;

            float multiplier = m_SpeedMultiplier * (m_Boost.IsPressed() ? boostMultiplier : 1f);

            // 只取偏航角构造水平基准：俯视时按 W 不会因为俯仰角而减速或上飘。
            Quaternion flatRotation = Quaternion.Euler(0f, m_Yaw, 0f);
            Vector3 direction = flatRotation * new Vector3(move.x, 0f, move.y);
            if (direction.sqrMagnitude > 1f) direction.Normalize();

            Vector3 offset = direction * (moveSpeed * multiplier * deltaTime);
            offset += Vector3.up * (vertical * verticalSpeed * multiplier * deltaTime);

            transform.position += offset;
        }

        #endregion
    }
}
