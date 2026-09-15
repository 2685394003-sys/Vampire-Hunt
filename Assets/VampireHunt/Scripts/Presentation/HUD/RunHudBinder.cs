using Unity.Netcode;
using UnityEngine;
using VampireHunt.Bootstrap;
using VampireHunt.Presentation.Audio;
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

        [Header("音效（留空则不发声）")]
        [Tooltip("本局胜利时播放一次，例如「Play_UI_Victory」")]
        [SerializeField] private string victoryEventName = "Play_UI_Victory";
        [Tooltip("本局失败时播放一次，例如「Play_UI_Defeat」")]
        [SerializeField] private string defeatEventName = "Play_UI_Defeat";

        private VampireHuntGameManager m_RunManager;
        private float m_NextRefreshTime;
        private float m_NextBindAttemptTime;
        private RunPhase m_LastPhase;
        private bool m_HasLastPhase;

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
            // ApplyPhase 由 0.1s 轮询反复调用，所以结算音必须做边缘触发：
            // 只在阶段真正切换的那一次发声，否则会每 0.1 秒重复播放。
            if (!m_HasLastPhase || phase != m_LastPhase)
            {
                if (phase == RunPhase.Victory) AudioCue.Post(victoryEventName, gameObject);
                else if (phase == RunPhase.Defeat) AudioCue.Post(defeatEventName, gameObject);
                m_LastPhase = phase;
                m_HasLastPhase = true;
            }

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
