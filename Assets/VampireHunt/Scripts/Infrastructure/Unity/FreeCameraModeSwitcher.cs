using System.Collections.Generic;
using Blocks.Gameplay.Core;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

namespace VampireHunt.Infrastructure.Unity
{
    /// <summary>
    /// 自由相机模式切换器（观察模式）。
    ///
    /// 按 R 在「跟随角色」与「自由相机」之间切换。进入自由相机时依次执行：
    ///   1. 关闭 CinemachineBrain —— 把主相机的控制权从 Cinemachine 手里拿回来，
    ///      交给 FreeCameraRig 直接驱动；
    ///   2. 关闭所有玩家输入闸门（IPlayerInputGate）—— 原角色不再响应任何操作；
    ///   3. 冻结整局（仅单人模式）—— 复用 MenuPauseController 的引用计数，
    ///      多人模式不冻结（由它内部判断「本机 Host 且无其它客户端」）。
    /// 退出时按相反顺序恢复，并先把相机放回进入前的位置，避免 Cinemachine 接管时跳变。
    ///
    /// 本组件挂在主相机上，不改动 CoreCameraController、玩家预制体（prefab）或输入资产（InputActions）。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    [RequireComponent(typeof(FreeCameraRig))]
    public class FreeCameraModeSwitcher : MonoBehaviour
    {
        #region 参数

        [Header("切换键")]
        [Tooltip("切换自由相机的按键名（Keyboard 按键，例如 r / p / f1）。")]
        [SerializeField] private string toggleKey = "r";

        [Header("停止时间")]
        [Tooltip("单人模式下进入自由相机时冻结整局。多人模式不会冻结。")]
        [SerializeField] private bool freezeInSinglePlayer = true;

        [Header("调试")]
        [Tooltip("切换时在 Console 打印状态，方便确认是否命中单人分支。")]
        [SerializeField] private bool logStateChanges = true;

        #endregion

        #region 内部状态

        private InputAction m_Toggle;
        private CinemachineBrain m_Brain;
        private FreeCameraRig m_Rig;

        private Vector3 m_SavedPosition;
        private Quaternion m_SavedRotation;

        private readonly List<IPlayerInputGate> m_GateBuffer = new List<IPlayerInputGate>();

        /// <summary>当前是否处于自由相机模式。</summary>
        public bool IsFreeCameraActive { get; private set; }

        #endregion

        #region Unity 生命周期

        private void Awake()
        {
            m_Brain = GetComponent<CinemachineBrain>();
            m_Rig = GetComponent<FreeCameraRig>();

            m_Toggle = new InputAction("FreeCameraToggle", InputActionType.Button, $"<Keyboard>/{toggleKey}");
            m_Toggle.performed += HandleTogglePerformed;

            // 自由相机默认关闭，只由切换键驱动，避免误挂组件后镜头失控。
            m_Rig.enabled = false;
        }

        private void OnEnable()
        {
            m_Toggle.Enable();
        }

        private void OnDisable()
        {
            m_Toggle.Disable();

            // 退出播放/销毁前确保恢复现场，防止把冻结状态留给下一局。
            if (IsFreeCameraActive)
            {
                ExitFreeCamera();
            }
        }

        private void OnDestroy()
        {
            m_Toggle.Dispose();
        }

        #endregion

        #region 开关

        private void HandleTogglePerformed(InputAction.CallbackContext context) => Toggle();

        /// <summary>切换自由相机模式。</summary>
        public void Toggle()
        {
            if (IsFreeCameraActive)
            {
                ExitFreeCamera();
            }
            else
            {
                EnterFreeCamera();
            }
        }

        /// <summary>进入自由相机模式。</summary>
        public void EnterFreeCamera()
        {
            if (IsFreeCameraActive) return;

            // 记录进入前的镜头位姿，退出时归位。
            m_SavedPosition = transform.position;
            m_SavedRotation = transform.rotation;

            // 1. 交出 Cinemachine 的控制权（必须先于 FreeCameraRig 接管）。
            if (m_Brain != null) m_Brain.enabled = false;

            // 2. FreeCameraRig 接管（其 OnEnable 会用当前朝向初始化视角）。
            m_Rig.enabled = true;

            // 3. 屏蔽玩家输入，原角色不再响应操作。
            SetPlayerInputEnabled(false);

            // 4. 单人模式冻结整局；多人模式该调用内部不会生效。
            if (freezeInSinglePlayer) MenuPauseController.RequestFreeCameraPause();

            IsFreeCameraActive = true;

            if (logStateChanges)
            {
                Debug.Log($"[FreeCameraModeSwitcher] 进入自由相机（停止时间={(freezeInSinglePlayer ? "请求" : "关闭")}）", this);
            }
        }

        /// <summary>退出自由相机模式，恢复跟随角色。</summary>
        public void ExitFreeCamera()
        {
            if (!IsFreeCameraActive) return;

            // 先恢复时间与输入，再交还相机，避免恢复过程中还有一帧的冻结/空输入。
            if (freezeInSinglePlayer) MenuPauseController.ReleaseFreeCameraPause();
            SetPlayerInputEnabled(true);

            m_Rig.enabled = false;

            // 归位后再启用 Brain：Cinemachine 会在下一帧直接从虚拟相机接管，基本无跳变。
            transform.SetPositionAndRotation(m_SavedPosition, m_SavedRotation);
            if (m_Brain != null) m_Brain.enabled = true;

            IsFreeCameraActive = false;

            if (logStateChanges)
            {
                Debug.Log("[FreeCameraModeSwitcher] 退出自由相机，恢复跟随角色", this);
            }
        }

        #endregion

        #region 玩家输入闸门

        /// <summary>
        /// 开关场景内所有玩家输入闸门。
        /// 使用接口查找而不是具体类型，因此玩家输入拆分成多少个处理组件都与本组件无关。
        /// </summary>
        private void SetPlayerInputEnabled(bool enabled)
        {
            m_GateBuffer.Clear();

            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);

            foreach (MonoBehaviour behaviour in behaviours)
            {
                if (behaviour is IPlayerInputGate gate)
                {
                    m_GateBuffer.Add(gate);
                }
            }

            foreach (IPlayerInputGate gate in m_GateBuffer)
            {
                gate.SetPlayerInputEnabled(enabled);
            }

            m_GateBuffer.Clear();
        }

        #endregion
    }
}
