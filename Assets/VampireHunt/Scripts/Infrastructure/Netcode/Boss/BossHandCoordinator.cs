using Unity.Netcode;
using UnityEngine;
using VampireHunt.Infrastructure.Integration;

namespace VampireHunt.Infrastructure.Netcode
{
    /// <summary>Spawns two instances of one hand prefab and mirrors their availability to BossBodyState.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class BossHandCoordinator : NetworkBehaviour
    {
        [SerializeField] private NetworkObject handPrefab;
        [SerializeField] private Transform leftAnchor;
        [SerializeField] private Transform rightAnchor;
        [SerializeField] private BossBodyStateHost bodyState;
        [Min(1f)] [SerializeField] private float handHealth = 100f;

        private BossHandNetworkActor m_Left;
        private BossHandNetworkActor m_Right;

        private void Awake()
        {
            if (bodyState == null) bodyState = GetComponent<BossBodyStateHost>();
            if (leftAnchor == null) leftAnchor = transform.Find("AbilityAnchor_LeftHand");
            if (rightAnchor == null) rightAnchor = transform.Find("AbilityAnchor_RightHand");
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer) SpawnHands();
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer)
            {
                Despawn(m_Left);
                Despawn(m_Right);
            }
            m_Left = null;
            m_Right = null;
            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer) return;
            bodyState?.TrySetHandState(m_Left != null && m_Left.IsFunctional,
                m_Right != null && m_Right.IsFunctional);
        }

        public bool SetIndependentServer(BossHandSide side, bool independent)
        {
            BossHandNetworkActor hand = side == BossHandSide.Left ? m_Left : m_Right;
            return hand != null && hand.SetIndependentServer(independent);
        }

        /// <summary>阶段切换时左右手立刻复活（血量回满）。</summary>
        public void RestoreAllHandsServer()
        {
            if (!IsServer) return;
            m_Left?.RestoreServer();
            m_Right?.RestoreServer();
        }

        /// <summary>Boss 随机传送后，左右手跟随传送到 Boss 新位置。</summary>
        public void TeleportHandsToBossServer()
        {
            if (!IsServer) return;
            m_Left?.TeleportToBossServer();
            m_Right?.TeleportToBossServer();
        }

        private void SpawnHands()
        {
            if (handPrefab == null) return;
            m_Left = SpawnOne(BossHandSide.Left, leftAnchor, new Vector3(-1.2f, 1.2f, 0f));
            m_Right = SpawnOne(BossHandSide.Right, rightAnchor, new Vector3(1.2f, 1.2f, 0f));
        }

        private BossHandNetworkActor SpawnOne(BossHandSide side, Transform anchor, Vector3 fallbackOffset)
        {
            Vector3 offset = anchor != null ? transform.InverseTransformPoint(anchor.position) : fallbackOffset;
            NetworkObject instance = Instantiate(handPrefab, transform.TransformPoint(offset), transform.rotation);
            if (!instance.TryGetComponent(out BossHandNetworkActor actor))
            {
                Destroy(instance.gameObject);
                return null;
            }
            actor.PrepareServer(transform, side, offset, handHealth);
            instance.Spawn();
            return actor;
        }

        private static void Despawn(BossHandNetworkActor hand)
        {
            if (hand != null && hand.NetworkObject != null && hand.NetworkObject.IsSpawned)
                hand.NetworkObject.Despawn();
        }
    }
}
