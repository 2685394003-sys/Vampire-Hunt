using Unity.Netcode;
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
        private bool m_HasRenderedCurrentBinding;

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
            if (m_Hud != null && m_Hud.IsPresentationReady)
                m_Hud.SetBossState(string.Empty, 0f, 1f, false);
            m_Hud = null;
            m_HasRenderedCurrentBinding = false;
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
