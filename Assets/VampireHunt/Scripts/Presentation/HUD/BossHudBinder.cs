using UnityEngine;
using VampireHunt.Boss.Encounter;
using VampireHunt.Infrastructure.Netcode;
using VampireHunt.Infrastructure.Unity.Boss;

namespace VampireHunt.Presentation.HUD
{
    /// <summary>Local read-model binder; it never sends commands back to the encounter.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BossEncounterStateReplicator))]
    public sealed class BossHudBinder : MonoBehaviour
    {
        [SerializeField] private BossEncounterConfigAsset config;
        [SerializeField] private BossEncounterStateReplicator stateSource;
        private VampireHuntHudPresenter m_Hud;
        private float m_NextHudLookup;

        private void Awake()
        {
            if (stateSource == null) stateSource = GetComponent<BossEncounterStateReplicator>();
        }

        private void OnEnable()
        {
            if (stateSource != null) stateSource.StateChanged += Render;
        }

        private void OnDisable()
        {
            if (stateSource != null) stateSource.StateChanged -= Render;
            m_Hud?.SetBossState(string.Empty, 0f, 1f, false);
        }

        private void Update()
        {
            if (m_Hud == null && Time.unscaledTime >= m_NextHudLookup)
            {
                m_NextHudLookup = Time.unscaledTime + 0.5f;
                m_Hud = FindAnyObjectByType<VampireHuntHudPresenter>();
                if (m_Hud != null && stateSource != null) Render(stateSource.Current);
            }
        }

        private void Render(BossEncounterNetworkState state)
        {
            if (m_Hud == null) return;
            bool guardVisible = state.State == BossEncounterState.RoamingIdle ||
                                state.State == BossEncounterState.RoamingEvade ||
                                state.State == BossEncounterState.StaggerEffect;
            m_Hud.SetBossEncounterState(config != null ? config.BossName : "猩红之主",
                state.Health, state.MaxHealth, state.GuardHealth, state.MaxGuardHealth,
                state.StageNumber, GetStatusText(state.State), state.HudVisible, guardVisible);
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
