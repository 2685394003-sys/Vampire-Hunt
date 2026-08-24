using Unity.Netcode;
using UnityEngine;
using VampireHunt.Bootstrap;
using VampireHunt.Run;

namespace VampireHunt.Presentation.HUD
{
    /// <summary>Owner-only presentation adapter from the replicated run snapshot to the local HUD.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(VampireHuntHudPresenter))]
    public sealed class RunHudBinder : NetworkBehaviour
    {
        [SerializeField] private VampireHuntHudPresenter hud;
        [SerializeField, Min(0.02f)] private float refreshInterval = 0.1f;

        private VampireHuntGameManager m_RunManager;
        private float m_NextRefreshTime;
        private float m_NextBindAttemptTime;

        private void Awake()
        {
            if (hud == null) hud = GetComponent<VampireHuntHudPresenter>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (!IsOwner) return;

            TryBindRunManager();
            RefreshHud();
        }

        public override void OnNetworkDespawn()
        {
            UnbindRunManager();
            base.OnNetworkDespawn();
        }

        private void Update()
        {
            if (!IsSpawned || !IsOwner || hud == null) return;

            if (m_RunManager == null)
            {
                if (Time.unscaledTime < m_NextBindAttemptTime) return;
                m_NextBindAttemptTime = Time.unscaledTime + 0.5f;
                TryBindRunManager();
                if (m_RunManager == null) return;
            }

            if (Time.unscaledTime < m_NextRefreshTime) return;
            m_NextRefreshTime = Time.unscaledTime + refreshInterval;
            RefreshHud();
        }

        private void TryBindRunManager()
        {
            if (m_RunManager != null) return;

            m_RunManager = FindAnyObjectByType<VampireHuntGameManager>();
            if (m_RunManager == null) return;

            m_RunManager.SnapshotChanged += HandleSnapshotChanged;
        }

        private void UnbindRunManager()
        {
            if (m_RunManager != null)
                m_RunManager.SnapshotChanged -= HandleSnapshotChanged;
            m_RunManager = null;
        }

        private void HandleSnapshotChanged(RunSnapshot snapshot)
        {
            RefreshHud();
        }

        private void RefreshHud()
        {
            if (hud == null || m_RunManager == null) return;

            hud.SetRunTimeRemaining((float)m_RunManager.GetEstimatedRemainingSeconds());
            ApplyPhase(m_RunManager.CurrentSnapshot.Phase);
        }

        private void ApplyPhase(RunPhase phase)
        {
            switch (phase)
            {
                case RunPhase.Lobby:
                    hud.SetRunPhase("等待开始", "等待玩家加入");
                    break;
                case RunPhase.Exploring:
                    hud.SetRunPhase("探索阶段", "寻找敌人并收集猩红");
                    break;
                case RunPhase.BossEncounter:
                    hud.SetRunPhase("Boss 战", "击败当前阶段 Boss");
                    break;
                case RunPhase.BossPhaseTransition:
                    hud.SetRunPhase("阶段过渡", "领取奖励并准备下一阶段");
                    break;
                case RunPhase.Victory:
                    hud.SetRunPhase("胜利", "狩猎完成");
                    break;
                case RunPhase.Defeat:
                    hud.SetRunPhase("失败", "倒计时已经归零");
                    break;
            }
        }

        private void OnValidate()
        {
            refreshInterval = Mathf.Max(0.02f, refreshInterval);
        }
    }
}
