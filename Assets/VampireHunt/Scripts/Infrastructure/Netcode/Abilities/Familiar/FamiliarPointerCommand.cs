using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using VampireHunt.Contracts;

namespace VampireHunt.Infrastructure.Netcode.Abilities.Familiar
{
    /// <summary>
    /// 使魔指针指令源（familiar pointer command）：把两个<b>本地</b>输入整理成服务器也能读到的一份快照 ——
    /// ① 鼠标指在世界坐标的哪个点（pointer point）；② 左键是否正被按住（command held）。
    /// 两种使魔（撞击水滴 161 / 射击僚机 162）共用它，血契「牵丝之契」据此让使魔绕鼠标待机、按左键指派目标。
    /// </summary>
    /// <remarks>
    /// <b>为什么需要它</b>：使魔的状态机与伤害结算<b>只在服务器推进</b>，而鼠标是本机设备 ——
    /// 纯 Server 模式下服务器读到的 <c>Mouse.current</c> 是服务器机器自己的鼠标，是错的。
    /// 所以这里把本地输入整理成瞬时指令，通过 Owner → Server RPC 交给服务器：
    /// <list type="bullet">
    /// <item><b>Host</b>（本项目主要验证方式）：本机既是 owner 又是服务器，直接读本地值，零延迟。</item>
    /// <item><b>Client + Server</b>：客户端按配置频率提交最新指令，服务器只保存最新快照。</item>
    /// <item><b>未联网</b>（没有 NetworkManager / 未 Spawn）：退化成本地直读，使魔照常工作。</item>
    /// </list>
    /// </remarks>
    /// <remarks>
    /// <b>指针点（pointer point）来自 <see cref="ICombatAimSource"/></b>，即 <c>TopDownAimAddon</c>：
    /// 它已经把鼠标射线投到地面并返回世界坐标，玩家武器也用同一个源，因此使魔与玩家看到的「鼠标位置」完全一致。
    /// 命中不到地面时它会退化为「过玩家的水平面」交点，所以指针点几乎总是有效的。
    /// </remarks>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class FamiliarPointerCommand : NetworkBehaviour
    {
        [Header("引用（留空自动从本物体查找）")]
        [Tooltip("瞄准源（aim source）：提供鼠标的世界坐标。留空 = 自动在本物体上找 ICombatAimSource" +
                 "（玩家身上挂的 TopDownAimAddon 就是）。")]
        [SerializeField] private MonoBehaviour aimSourceBehaviour;

        [Header("网络输入")]
        [Tooltip("远端 Owner 向服务器提交指针快照的最高频率。指针是瞬时输入，不向其他客户端广播。")]
        [SerializeField, Range(5f, 30f)] private float networkSendRate = 20f;

        [Header("调试")]
        [Tooltip("在 Console 打印指针与左键状态变化，便于确认链路是否通。")]
        [SerializeField] private bool logToConsole;

        private ICombatAimSource m_AimSource;
        private Vector3 m_LocalPointer;
        private bool m_LocalHasPointer;
        private bool m_LocalHeld;
        private Vector3 m_ServerPointer;
        private bool m_ServerHasPointer;
        private bool m_ServerHeld;
        private Vector3 m_LastSentPointer;
        private bool m_LastSentHasPointer;
        private bool m_LastSentHeld;
        private float m_NextNetworkSendTime;
        private double m_LastServerAcceptTime = double.NegativeInfinity;
        private bool m_LoggedHeld;

        /// <summary>指令键当前是否被按住（服务器读到的权威值）。</summary>
        public bool IsCommandHeld =>
            UsesServerSnapshot ? m_ServerHeld : m_LocalHeld;

        /// <summary>
        /// 取出当前指针的世界坐标（pointer point）。
        /// 返回 false = 指针无效（鼠标没投到地面），调用方应退回「围绕玩家」。
        /// </summary>
        public bool TryGetPointer(out Vector3 worldPoint)
        {
            if (IsLocalOperator)
            {
                worldPoint = m_LocalPointer;
                return m_LocalHasPointer;
            }
            worldPoint = m_ServerPointer;
            return m_ServerHasPointer;
        }

        /// <summary>
        /// 本机是否就是「操作这台电脑的人」：owner，或尚未联网（此时本机即操作者）。
        /// 判断它才能决定该读本地输入还是读网络同步值。
        /// </summary>
        private bool IsLocalOperator => !IsSpawned || IsOwner;

        /// <summary>服务器查看远端玩家时使用 RPC 接收的快照；Host 自己仍直接读取本地输入。</summary>
        private bool UsesServerSnapshot => IsSpawned && IsServer && !IsOwner;

        // ── 生命周期 ──────────────────────────────────────────

        private void Awake()
        {
            if (aimSourceBehaviour is ICombatAimSource assigned)
            {
                m_AimSource = assigned;
                return;
            }
            MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is ICombatAimSource source)
                {
                    m_AimSource = source;
                    return;
                }
            }
        }

        private void Update()
        {
            // 只有操作者本人产生输入；服务器上这份数据来自网络同步，不能拿服务器的鼠标去覆盖。
            if (!IsLocalOperator)
            {
                if (logToConsole && IsServer) LogServerView();
                return;
            }

            // 注意：aim 必须先给默认值 —— 左侧 m_AimSource 为空时 && 短路，out 不会执行，
            // 编译器会判定 aim 可能未赋值（CS0165），下一行读取即报错。
            AimSnapshot aim = default;
            bool hasPointer = m_AimSource != null && m_AimSource.TryGetAim(out aim);
            Vector3 point = hasPointer ? ToVector3(aim.WorldPoint) : Vector3.zero;
            bool held = IsCommandButtonHeld();

            m_LocalHasPointer = hasPointer;
            m_LocalPointer = point;
            m_LocalHeld = held;

            // 指针是输入意图，不是世界持久状态：只交给服务器，不广播给所有观察者。
            if (IsSpawned)
            {
                if (IsServer)
                {
                    ApplyServerSnapshot(point, hasPointer, held);
                }
                else if (ShouldSendSnapshot(point, hasPointer, held))
                {
                    SubmitPointerSnapshotRpc(point, hasPointer, held);
                    RememberSentSnapshot(point, hasPointer, held);
                }
            }

            if (logToConsole && held != m_LoggedHeld)
            {
                m_LoggedHeld = held;
                Debug.Log($"[FamiliarPointer] 左键 {(held ? "按下" : "松开")}  pointer={point} valid={hasPointer}", this);
            }
        }

        /// <summary>
        /// 指令键是否按住：<b>左键</b>。与游戏「按住连发（hold-to-fire）」的口径一致 ——
        /// 按住期间持续生效，而不是只在按下那一帧。
        /// </summary>
        private static bool IsCommandButtonHeld()
        {
            Mouse mouse = Mouse.current;
            return mouse != null && mouse.leftButton.isPressed;
        }

        private void LogServerView()
        {
            // 只在按住状态翻转时打印，避免刷屏。
            bool held = m_ServerHeld;
            if (held == m_LoggedHeld) return;
            m_LoggedHeld = held;
            Debug.Log($"[FamiliarPointer] (server) 左键 {(held ? "按下" : "松开")}  " +
                      $"pointer={m_ServerPointer} valid={m_ServerHasPointer}", this);
        }

        private bool ShouldSendSnapshot(Vector3 point, bool hasPointer, bool held)
        {
            bool stateChanged = hasPointer != m_LastSentHasPointer || held != m_LastSentHeld;
            if (stateChanged) return true;
            if (Time.unscaledTime < m_NextNetworkSendTime) return false;
            if (!hasPointer) return false;
            return (point - m_LastSentPointer).sqrMagnitude > 0.0004f;
        }

        private void RememberSentSnapshot(Vector3 point, bool hasPointer, bool held)
        {
            m_LastSentPointer = point;
            m_LastSentHasPointer = hasPointer;
            m_LastSentHeld = held;
            m_NextNetworkSendTime = Time.unscaledTime + 1f / Mathf.Max(1f, networkSendRate);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner, Delivery = RpcDelivery.Unreliable)]
        private void SubmitPointerSnapshotRpc(Vector3 point, bool hasPointer, bool held)
        {
            if (!IsFinite(point)) return;

            double now = NetworkManager.ServerTime.Time;
            double minimumInterval = 1d / Mathf.Max(1f, networkSendRate);
            bool buttonChanged = held != m_ServerHeld || hasPointer != m_ServerHasPointer;
            if (!buttonChanged && now - m_LastServerAcceptTime < minimumInterval * 0.75d) return;

            m_LastServerAcceptTime = now;
            ApplyServerSnapshot(point, hasPointer, held);
        }

        private void ApplyServerSnapshot(Vector3 point, bool hasPointer, bool held)
        {
            m_ServerPointer = point;
            m_ServerHasPointer = hasPointer;
            m_ServerHeld = held;
        }

        private static bool IsFinite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
            !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        // Float3 的分量是大写属性 X/Y/Z（见 AbilityContracts.cs），不是小写字段。
        private static Vector3 ToVector3(Float3 value) => new Vector3(value.X, value.Y, value.Z);
    }
}
