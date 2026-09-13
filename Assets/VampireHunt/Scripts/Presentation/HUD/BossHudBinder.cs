using Unity.Netcode;
using UnityEngine;
using VampireHunt.Boss.Encounter;
using VampireHunt.Infrastructure.Netcode;
using VampireHunt.Infrastructure.Unity.Boss;
using VampireHunt.Presentation.Audio;

namespace VampireHunt.Presentation.HUD
{
    /// <summary>Local read-model binder; it never sends commands back to the encounter.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BossEncounterStateReplicator))]
    public sealed class BossHudBinder : MonoBehaviour
    {
        [SerializeField] private BossEncounterConfigAsset config;
        [SerializeField] private BossEncounterStateReplicator stateSource;

        [Header("Boss 音效（留空则不发声）")]
        [Tooltip("从漫游进入 Boss 战时播放一次，例如「Play_Boss_BattleStart」")]
        [SerializeField] private string battleStartEventName = "Play_Boss_BattleStart";
        [Tooltip("格挡条被击破时播放一次，例如「Play_Boss_GuardBroken」")]
        [SerializeField] private string guardBrokenEventName = "Play_Boss_GuardBroken";
        [Tooltip("进入踉跄状态时播放一次，例如「Play_Boss_Stagger」")]
        [SerializeField] private string staggerEventName = "Play_Boss_Stagger";
        [Tooltip("阶段推进（StageNumber 变化）时播放一次，例如「Play_Boss_PhaseChange」")]
        [SerializeField] private string phaseChangeEventName = "Play_Boss_PhaseChange";
        [Tooltip("Boss 掉血时播放（带节流），例如「Play_Boss_Hurt」")]
        [SerializeField] private string hurtEventName = "Play_Boss_Hurt";
        [Tooltip("两次受伤音之间的最小间隔（秒），避免多段伤害把音效叠成一团")]
        [SerializeField, Min(0f)] private float hurtSoundInterval = 0.15f;
        [Tooltip("Boss 被击败时播放一次，例如「Play_Boss_Defeated」")]
        [SerializeField] private string defeatedEventName = "Play_Boss_Defeated";

        private VampireHuntHudPresenter m_Hud;
        private float m_NextHudLookup;
        private bool m_HasRenderedCurrentBinding;

        private BossEncounterState m_LastState;
        private int m_LastStageNumber;
        private float m_LastHealth;
        private float m_LastGuardHealth;
        private bool m_HasPreviousSnapshot;
        private float m_NextHurtSoundTime;

        private void Awake()
        {
            if (stateSource == null) stateSource = GetComponent<BossEncounterStateReplicator>();
        }

        private void OnEnable()
        {
            // 音效由 StateChanged 直接驱动、不经过 HUD —— 否则 HUD 尚未就绪的那几帧会丢掉状态音。
            if (stateSource != null) stateSource.StateChanged += HandleStateChanged;
        }

        private void OnDisable()
        {
            if (stateSource != null) stateSource.StateChanged -= HandleStateChanged;
            if (m_Hud != null && m_Hud.IsPresentationReady)
                m_Hud.SetBossState(string.Empty, 0f, 1f, false);
            m_Hud = null;
            m_HasRenderedCurrentBinding = false;
            m_HasPreviousSnapshot = false;
        }

        private void Update()
        {
            if (m_Hud != null && m_Hud.IsPresentationReady && m_HasRenderedCurrentBinding) return;
            if (Time.unscaledTime < m_NextHudLookup) return;

            m_NextHudLookup = Time.unscaledTime + 0.25f;
            VampireHuntHudPresenter localHud = ResolveLocalPlayerHud();
            if (localHud != m_Hud)
            {
                m_Hud = localHud;
                m_HasRenderedCurrentBinding = false;
            }

            if (m_Hud == null || !m_Hud.IsPresentationReady || stateSource == null) return;
            Render(stateSource.Current);
        }

        private void HandleStateChanged(BossEncounterNetworkState state)
        {
            PlayStateAudio(state);
            Render(state);
        }

        /// <summary>
        /// Boss 状态音全部基于客户端已经同步的只读状态做边缘触发，
        /// 因此不需要任何额外的 RPC / 网络通道。
        /// </summary>
        private void PlayStateAudio(BossEncounterNetworkState state)
        {
            if (m_HasPreviousSnapshot)
            {
                if (state.State != m_LastState)
                {
                    switch (state.State)
                    {
                        case BossEncounterState.Battle:
                            // 只有「从漫游状态进入战斗」才算开战。从 PhaseTransition 进来的那次
                            // 交给下方的 StageNumber 变化播阶段音，否则两个音会叠在同一帧。
                            if (m_LastState == BossEncounterState.RoamingIdle ||
                                m_LastState == BossEncounterState.RoamingEvade)
                            {
                                AudioCue.Post(battleStartEventName, gameObject);
                            }
                            break;
                        case BossEncounterState.StaggerEffect:
                            AudioCue.Post(staggerEventName, gameObject);
                            break;
                        case BossEncounterState.Defeated:
                            AudioCue.Post(defeatedEventName, gameObject);
                            break;
                    }
                }

                if (m_LastGuardHealth > 0f && state.GuardHealth <= 0f)
                    AudioCue.Post(guardBrokenEventName, gameObject);

                if (state.StageNumber != m_LastStageNumber)
                    AudioCue.Post(phaseChangeEventName, gameObject);

                if (state.Health < m_LastHealth && Time.unscaledTime >= m_NextHurtSoundTime)
                {
                    m_NextHurtSoundTime = Time.unscaledTime + hurtSoundInterval;
                    AudioCue.Post(hurtEventName, gameObject);
                }
            }

            m_LastState = state.State;
            m_LastStageNumber = state.StageNumber;
            m_LastHealth = state.Health;
            m_LastGuardHealth = state.GuardHealth;
            m_HasPreviousSnapshot = true;
        }

        private void Render(BossEncounterNetworkState state)
        {
            if (m_Hud == null || !m_Hud.IsPresentationReady)
            {
                m_HasRenderedCurrentBinding = false;
                return;
            }
            bool guardVisible = state.State == BossEncounterState.RoamingIdle ||
                                state.State == BossEncounterState.RoamingEvade ||
                                state.State == BossEncounterState.StaggerEffect;
            m_Hud.SetBossEncounterState(config != null ? config.BossName : "猩红之主",
                state.Health, state.MaxHealth, state.GuardHealth, state.MaxGuardHealth,
                state.StageNumber, GetStatusText(state.State), state.HudVisible, guardVisible);
            m_HasRenderedCurrentBinding = true;
        }

        private static VampireHuntHudPresenter ResolveLocalPlayerHud()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening || manager.SpawnManager == null)
                return FindAnyObjectByType<VampireHuntHudPresenter>();

            NetworkObject localPlayer = manager.SpawnManager.GetPlayerNetworkObject(manager.LocalClientId);
            return localPlayer != null
                ? localPlayer.GetComponent<VampireHuntHudPresenter>()
                : null;
        }

        private static string GetStatusText(BossEncounterState state)
        {
            switch (state)
            {
                case BossEncounterState.RoamingIdle: return "游走·待机攻击";
                case BossEncounterState.RoamingEvade: return "游走·远离";
                case BossEncounterState.StaggerEffect: return "踉跄反击";
                case BossEncounterState.Battle: return "Boss 战";
                case BossEncounterState.PhaseTransition: return "阶段转换";
                case BossEncounterState.Defeated: return "已击败";
                default: return string.Empty;
            }
        }
    }
}
