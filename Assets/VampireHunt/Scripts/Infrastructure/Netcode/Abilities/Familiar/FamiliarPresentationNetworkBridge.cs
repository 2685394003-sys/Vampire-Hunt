using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;

namespace VampireHunt.Infrastructure.Netcode.Abilities.Familiar
{
    internal struct FamiliarPoseNetworkState : INetworkSerializable, IEquatable<FamiliarPoseNetworkState>
    {
        public int Index;
        public Vector3 Position;
        public Vector3 Facing;
        public Vector3 Scale;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Index);
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Facing);
            serializer.SerializeValue(ref Scale);
        }

        public bool Equals(FamiliarPoseNetworkState other) =>
            Index == other.Index && Position.Equals(other.Position) && Facing.Equals(other.Facing) &&
            Scale.Equals(other.Scale);
    }

    internal struct GunnerFamiliarShotNetworkCue : INetworkSerializable, IEquatable<GunnerFamiliarShotNetworkCue>
    {
        public byte WeaponId;
        public Vector3 Origin;
        public Vector3 End;
        public Vector3 Direction;
        public float TracerSpeed;
        public float TracerScale;
        public bool ShowImpactOnArrival;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref WeaponId);
            serializer.SerializeValue(ref Origin);
            serializer.SerializeValue(ref End);
            serializer.SerializeValue(ref Direction);
            serializer.SerializeValue(ref TracerSpeed);
            serializer.SerializeValue(ref TracerScale);
            serializer.SerializeValue(ref ShowImpactOnArrival);
        }

        public bool Equals(GunnerFamiliarShotNetworkCue other) =>
            WeaponId == other.WeaponId && Origin.Equals(other.Origin) && End.Equals(other.End) &&
            Direction.Equals(other.Direction) && TracerSpeed.Equals(other.TracerSpeed) &&
            TracerScale.Equals(other.TracerScale) && ShowImpactOnArrival == other.ShowImpactOnArrival;
    }

    /// <summary>无托管数组分配的姿态批次，避免每秒多次 RPC 在客户端制造 GC。</summary>
    internal struct FamiliarPoseNetworkBatch : INetworkSerializable
    {
        public FixedList4096Bytes<FamiliarPoseNetworkState> Poses;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            int count = Poses.Length;
            serializer.SerializeValue(ref count);
            if (serializer.IsReader)
            {
                Poses.Clear();
                for (int i = 0; i < count; i++)
                {
                    FamiliarPoseNetworkState pose = default;
                    serializer.SerializeValue(ref pose);
                    Poses.Add(pose);
                }
                return;
            }

            for (int i = 0; i < count; i++)
            {
                FamiliarPoseNetworkState pose = Poses[i];
                serializer.SerializeValue(ref pose);
            }
        }
    }

    /// <summary>
    /// 使魔表现专用网络适配器。玩法控制器只把姿态和开火结果发布到本组件；本组件负责：
    /// 1) 用服务器写入的数量状态支持中途加入；2) 以低频、不可靠批量快照同步连续姿态；
    /// 3) 把瞬时开火表现广播给远端客户端。它不参与索敌、状态机、命中或伤害。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class FamiliarPresentationNetworkBridge : NetworkBehaviour,
        IImpactFamiliarPresentationPublisher,
        IGunnerFamiliarPresentationPublisher
    {
        private const int DefaultMaxFamiliarCount = 16;

        [Header("Client Presentation")]
        [Tooltip("实现 IImpactFamiliarPresentationSink 的纯表现组件。留空时自动从同物体查找。")]
        [SerializeField] private MonoBehaviour impactPresenter;
        [Tooltip("实现 IGunnerFamiliarPresentationSink 的纯表现组件。留空时自动从同物体查找。")]
        [SerializeField] private MonoBehaviour gunnerPresenter;

        [Header("Replication")]
        [Tooltip("连续姿态每秒发送次数。开火事件单独发送，不受此值影响。")]
        [SerializeField, Range(5f, 30f)] private float poseSnapshotsPerSecond = 15f;
        [Tooltip("单类使魔允许同步的最大数量，防止异常配置制造超大网络包。")]
        [SerializeField, Min(1)] private int maxFamiliarCount = DefaultMaxFamiliarCount;

        private readonly NetworkVariable<int> m_ImpactCount = new NetworkVariable<int>(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<int> m_GunnerCount = new NetworkVariable<int>(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private IImpactFamiliarPresentationSink m_ImpactSink;
        private IGunnerFamiliarPresentationSink m_GunnerSink;
        private FamiliarPoseNetworkState[] m_ImpactPoses = Array.Empty<FamiliarPoseNetworkState>();
        private FamiliarPoseNetworkState[] m_GunnerPoses = Array.Empty<FamiliarPoseNetworkState>();
        private bool m_ImpactDirty;
        private bool m_GunnerDirty;
        private bool m_HasPendingImpactCount;
        private bool m_HasPendingGunnerCount;
        private int m_PendingImpactCount;
        private int m_PendingGunnerCount;
        private double m_NextSnapshotTime;
        private uint m_ImpactSnapshotSequence;
        private uint m_GunnerSnapshotSequence;
        private uint m_LastReceivedImpactSequence;
        private uint m_LastReceivedGunnerSequence;
        private NetworkObject m_NetworkObject;

        private void Awake()
        {
            m_NetworkObject = GetComponent<NetworkObject>();
            ResolveSinks();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            m_ImpactCount.OnValueChanged += HandleImpactCountChanged;
            m_GunnerCount.OnValueChanged += HandleGunnerCountChanged;

            if (IsServer)
            {
                if (m_HasPendingImpactCount) SetImpactCountServer(m_PendingImpactCount);
                if (m_HasPendingGunnerCount) SetGunnerCountServer(m_PendingGunnerCount);
            }

            m_HasPendingImpactCount = false;
            m_HasPendingGunnerCount = false;
            if (!IsClient) return;
            ApplyImpactCount(m_ImpactCount.Value);
            ApplyGunnerCount(m_GunnerCount.Value);
        }

        public override void OnNetworkDespawn()
        {
            m_ImpactCount.OnValueChanged -= HandleImpactCountChanged;
            m_GunnerCount.OnValueChanged -= HandleGunnerCountChanged;
            if (IsClient)
            {
                m_ImpactSink?.Clear();
                m_GunnerSink?.Clear();
            }
            base.OnNetworkDespawn();
        }

        void IImpactFamiliarPresentationPublisher.SetVisualCount(int count) => PublishImpactCount(count);
        void IGunnerFamiliarPresentationPublisher.SetVisualCount(int count) => PublishGunnerCount(count);

        void IImpactFamiliarPresentationPublisher.PublishPose(in FamiliarVisualPose pose)
        {
            if (IsStandalone())
            {
                m_ImpactSink?.ApplyPose(pose);
                return;
            }
            if (!IsServer || pose.Index < 0 || pose.Index >= m_ImpactPoses.Length) return;
            m_ImpactPoses[pose.Index] = ToNetwork(pose);
            m_ImpactDirty = true;
        }

        void IGunnerFamiliarPresentationPublisher.PublishPose(in FamiliarVisualPose pose)
        {
            if (IsStandalone())
            {
                m_GunnerSink?.ApplyPose(pose);
                return;
            }
            if (!IsServer || pose.Index < 0 || pose.Index >= m_GunnerPoses.Length) return;
            m_GunnerPoses[pose.Index] = ToNetwork(pose);
            m_GunnerDirty = true;
        }

        void IGunnerFamiliarPresentationPublisher.PublishShot(in GunnerFamiliarShotPresentationCue cue)
        {
            if (IsStandalone())
            {
                m_GunnerSink?.PlayShot(cue);
                return;
            }
            if (!IsSpawned || !IsServer) return;

            // Host 本机无需绕一次网络；远端只接收视觉事件，伤害早已在服务器完成。
            if (IsClient) m_GunnerSink?.PlayShot(cue);
            PresentGunnerShotRpc(ToNetwork(cue));
        }

        private void LateUpdate()
        {
            if (!IsSpawned || !IsServer || (!m_ImpactDirty && !m_GunnerDirty)) return;
            double now = NetworkManager != null ? NetworkManager.ServerTime.Time : Time.unscaledTimeAsDouble;
            if (now < m_NextSnapshotTime) return;
            m_NextSnapshotTime = now + 1d / Mathf.Max(1f, poseSnapshotsPerSecond);

            if (m_ImpactDirty && m_ImpactPoses.Length > 0)
            {
                uint sequence = ++m_ImpactSnapshotSequence;
                if (IsClient) ApplyPoseSnapshot(m_ImpactSink, m_ImpactPoses);
                PresentImpactPosesRpc(sequence, CreateBatch(m_ImpactPoses));
                m_ImpactDirty = false;
            }

            if (m_GunnerDirty && m_GunnerPoses.Length > 0)
            {
                uint sequence = ++m_GunnerSnapshotSequence;
                if (IsClient) ApplyPoseSnapshot(m_GunnerSink, m_GunnerPoses);
                PresentGunnerPosesRpc(sequence, CreateBatch(m_GunnerPoses));
                m_GunnerDirty = false;
            }
        }

        private void PublishImpactCount(int count)
        {
            count = ClampCount(count);
            if (IsStandalone())
            {
                ResizePoseBuffer(ref m_ImpactPoses, count);
                ApplyImpactCount(count);
                return;
            }
            if (!IsSpawned)
            {
                m_HasPendingImpactCount = true;
                m_PendingImpactCount = count;
                ResizePoseBuffer(ref m_ImpactPoses, count);
                return;
            }
            if (IsServer) SetImpactCountServer(count);
        }

        private void PublishGunnerCount(int count)
        {
            count = ClampCount(count);
            if (IsStandalone())
            {
                ResizePoseBuffer(ref m_GunnerPoses, count);
                ApplyGunnerCount(count);
                return;
            }
            if (!IsSpawned)
            {
                m_HasPendingGunnerCount = true;
                m_PendingGunnerCount = count;
                ResizePoseBuffer(ref m_GunnerPoses, count);
                return;
            }
            if (IsServer) SetGunnerCountServer(count);
        }

        private void SetImpactCountServer(int count)
        {
            count = ClampCount(count);
            ResizePoseBuffer(ref m_ImpactPoses, count);
            m_ImpactCount.Value = count;
            m_ImpactDirty = count > 0;
        }

        private void SetGunnerCountServer(int count)
        {
            count = ClampCount(count);
            ResizePoseBuffer(ref m_GunnerPoses, count);
            m_GunnerCount.Value = count;
            m_GunnerDirty = count > 0;
        }

        private void HandleImpactCountChanged(int previous, int current)
        {
            if (IsClient) ApplyImpactCount(current);
        }

        private void HandleGunnerCountChanged(int previous, int current)
        {
            if (IsClient) ApplyGunnerCount(current);
        }

        private void ApplyImpactCount(int count)
        {
            ResolveSinks();
            if (count > 0) m_ImpactSink?.Rebuild(count);
            else m_ImpactSink?.Clear();
        }

        private void ApplyGunnerCount(int count)
        {
            ResolveSinks();
            if (count > 0) m_GunnerSink?.Rebuild(count);
            else m_GunnerSink?.Clear();
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server,
            Delivery = RpcDelivery.Unreliable)]
        private void PresentImpactPosesRpc(uint sequence, FamiliarPoseNetworkBatch batch)
        {
            if (sequence <= m_LastReceivedImpactSequence) return;
            m_LastReceivedImpactSequence = sequence;
            ResolveSinks();
            ApplyPoseSnapshot(m_ImpactSink, batch);
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server,
            Delivery = RpcDelivery.Unreliable)]
        private void PresentGunnerPosesRpc(uint sequence, FamiliarPoseNetworkBatch batch)
        {
            if (sequence <= m_LastReceivedGunnerSequence) return;
            m_LastReceivedGunnerSequence = sequence;
            ResolveSinks();
            ApplyPoseSnapshot(m_GunnerSink, batch);
        }

        [Rpc(SendTo.NotServer, InvokePermission = RpcInvokePermission.Server,
            Delivery = RpcDelivery.Unreliable)]
        private void PresentGunnerShotRpc(GunnerFamiliarShotNetworkCue cue)
        {
            ResolveSinks();
            m_GunnerSink?.PlayShot(ToContract(cue));
        }

        private void ResolveSinks()
        {
            m_ImpactSink = impactPresenter as IImpactFamiliarPresentationSink;
            m_GunnerSink = gunnerPresenter as IGunnerFamiliarPresentationSink;
            if (m_ImpactSink != null && m_GunnerSink != null) return;

            MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (m_ImpactSink == null && behaviours[i] is IImpactFamiliarPresentationSink impact)
                {
                    m_ImpactSink = impact;
                    impactPresenter = behaviours[i];
                }
                if (m_GunnerSink == null && behaviours[i] is IGunnerFamiliarPresentationSink gunner)
                {
                    m_GunnerSink = gunner;
                    gunnerPresenter = behaviours[i];
                }
            }
        }

        private bool IsStandalone()
        {
            NetworkManager manager = m_NetworkObject != null ? m_NetworkObject.NetworkManager : null;
            return manager == null || !manager.IsListening;
        }

        private int ClampCount(int count) => Mathf.Clamp(count, 0, Mathf.Max(1, maxFamiliarCount));

        private static void ResizePoseBuffer(ref FamiliarPoseNetworkState[] buffer, int count)
        {
            if (buffer != null && buffer.Length == count) return;
            buffer = count > 0 ? new FamiliarPoseNetworkState[count] : Array.Empty<FamiliarPoseNetworkState>();
            for (int i = 0; i < buffer.Length; i++) buffer[i].Index = i;
        }

        private static FamiliarPoseNetworkState ToNetwork(in FamiliarVisualPose pose) =>
            new FamiliarPoseNetworkState
            {
                Index = pose.Index,
                Position = ToVector3(pose.Position),
                Facing = ToVector3(pose.Facing),
                Scale = ToVector3(pose.Scale)
            };

        private static GunnerFamiliarShotNetworkCue ToNetwork(in GunnerFamiliarShotPresentationCue cue) =>
            new GunnerFamiliarShotNetworkCue
            {
                WeaponId = cue.WeaponId,
                Origin = ToVector3(cue.Origin),
                End = ToVector3(cue.End),
                Direction = ToVector3(cue.Direction),
                TracerSpeed = cue.TracerSpeed,
                TracerScale = cue.TracerScale,
                ShowImpactOnArrival = cue.ShowImpactOnArrival
            };

        private static FamiliarVisualPose ToContract(in FamiliarPoseNetworkState pose) =>
            new FamiliarVisualPose(pose.Index, ToFloat3(pose.Position), ToFloat3(pose.Facing), ToFloat3(pose.Scale));

        private static GunnerFamiliarShotPresentationCue ToContract(in GunnerFamiliarShotNetworkCue cue) =>
            new GunnerFamiliarShotPresentationCue(cue.WeaponId, ToFloat3(cue.Origin), ToFloat3(cue.End),
                ToFloat3(cue.Direction), cue.TracerSpeed, cue.TracerScale, cue.ShowImpactOnArrival);

        private static FamiliarPoseNetworkBatch CreateBatch(FamiliarPoseNetworkState[] poses)
        {
            var batch = new FamiliarPoseNetworkBatch();
            if (poses == null) return batch;
            for (int i = 0; i < poses.Length; i++) batch.Poses.Add(poses[i]);
            return batch;
        }

        private static void ApplyPoseSnapshot(IImpactFamiliarPresentationSink sink,
            in FamiliarPoseNetworkBatch batch)
        {
            if (sink == null) return;
            for (int i = 0; i < batch.Poses.Length; i++) sink.ApplyPose(ToContract(batch.Poses[i]));
        }

        private static void ApplyPoseSnapshot(IGunnerFamiliarPresentationSink sink,
            in FamiliarPoseNetworkBatch batch)
        {
            if (sink == null) return;
            for (int i = 0; i < batch.Poses.Length; i++) sink.ApplyPose(ToContract(batch.Poses[i]));
        }

        private static void ApplyPoseSnapshot(IImpactFamiliarPresentationSink sink,
            FamiliarPoseNetworkState[] poses)
        {
            if (sink == null || poses == null) return;
            for (int i = 0; i < poses.Length; i++) sink.ApplyPose(ToContract(poses[i]));
        }

        private static void ApplyPoseSnapshot(IGunnerFamiliarPresentationSink sink,
            FamiliarPoseNetworkState[] poses)
        {
            if (sink == null || poses == null) return;
            for (int i = 0; i < poses.Length; i++) sink.ApplyPose(ToContract(poses[i]));
        }

        private static Vector3 ToVector3(in Float3 value) => new Vector3(value.X, value.Y, value.Z);
        private static Float3 ToFloat3(in Vector3 value) => new Float3(value.x, value.y, value.z);
    }
}
