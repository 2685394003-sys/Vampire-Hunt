using System;
using Blocks.Gameplay.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace VampireHunt.Presentation.HUD
{
    /// <summary>
    /// Presentation-only extension of the template HUD. It consumes the local
    /// player's stat events and exposes small rendering APIs for run, boss and
    /// ability read models without mutating gameplay state.
    /// </summary>
    public sealed class VampireHuntHudPresenter : CoreHUD
    {
        private const string HealthStatName = "Health";
        private const string StaminaStatName = "Stamina";
        private const string ScarletStatName = "Scarlet";

        private VisualElement m_HudRoot;
        private VisualElement m_LowHealthVignette;
        private VisualElement m_BossPanel;
        private ProgressBar m_HealthBar;
        private ProgressBar m_StaminaBar;
        private ProgressBar m_ScarletBar;
        private ProgressBar m_BossHealthBar;
        private Label m_HealthValue;
        private Label m_StaminaValue;
        private Label m_ScarletValue;
        private Label m_RunTimer;
        private Label m_RunPhase;
        private Label m_Objective;
        private Label m_BossName;
        private Label m_BossHealthValue;
        private Label m_PactCount;
        private Label m_BuildName;

        protected override void QueryHUDElements(VisualElement root)
        {
            m_HudRoot = root.Q<VisualElement>("hud-root");
            m_LowHealthVignette = root.Q<VisualElement>("low-health-vignette");
            m_BossPanel = root.Q<VisualElement>("boss-panel");
            m_HealthBar = root.Q<ProgressBar>("player-health-bar");
            m_StaminaBar = root.Q<ProgressBar>("player-stamina-bar");
            m_ScarletBar = root.Q<ProgressBar>("player-scarlet-bar");
            m_BossHealthBar = root.Q<ProgressBar>("boss-health-bar");
            m_HealthValue = root.Q<Label>("health-value");
            m_StaminaValue = root.Q<Label>("stamina-value");
            m_ScarletValue = root.Q<Label>("scarlet-value");
            m_RunTimer = root.Q<Label>("run-timer");
            m_RunPhase = root.Q<Label>("run-phase");
            m_Objective = root.Q<Label>("objective-text");
            m_BossName = root.Q<Label>("boss-name");
            m_BossHealthValue = root.Q<Label>("boss-health-value");
            m_PactCount = root.Q<Label>("pact-count");
            m_BuildName = root.Q<Label>("build-name");
        }

        protected override void SetHUDDefaults()
        {
            ApplyThemeColors();
            SetRunTimeRemaining(0f);
            SetRunPhase("探索阶段", "寻找敌人并收集猩红");
            SetBossState(string.Empty, 0f, 1f, false);
            SetPactSummary(0, "尚未缔结血契");
            SetVital(m_HealthBar, m_HealthValue, 0f, 100f);
            SetVital(m_StaminaBar, m_StaminaValue, 0f, 100f);
            SetVital(m_ScarletBar, m_ScarletValue, 0f, 100f);
            UpdateLowHealthPresentation(1f);
        }

        protected override void HandleStatChangedLocal(StatChangePayload payload)
        {
            if (IsStat(payload, HealthStatName))
            {
                SetVital(m_HealthBar, m_HealthValue, payload.currentValue, payload.maxValue);
                float normalized = payload.maxValue > 0f ? payload.currentValue / payload.maxValue : 0f;
                UpdateLowHealthPresentation(normalized);
                return;
            }

            if (IsStat(payload, StaminaStatName))
            {
                SetVital(m_StaminaBar, m_StaminaValue, payload.currentValue, payload.maxValue);
                return;
            }

            if (IsStat(payload, ScarletStatName))
            {
                SetVital(m_ScarletBar, m_ScarletValue, payload.currentValue, payload.maxValue);
            }
        }

        /// <summary>Updates the run countdown using a presentation-ready value.</summary>
        public void SetRunTimeRemaining(float seconds)
        {
            if (m_RunTimer == null) return;

            int totalSeconds = Mathf.Max(0, Mathf.CeilToInt(seconds));
            TimeSpan time = TimeSpan.FromSeconds(totalSeconds);
            m_RunTimer.text = time.TotalHours >= 1d
                ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}"
                : $"{time.Minutes:00}:{time.Seconds:00}";
        }

        /// <summary>Updates the current run phase and objective copy.</summary>
        public void SetRunPhase(string phase, string objective)
        {
            if (m_RunPhase != null) m_RunPhase.text = string.IsNullOrWhiteSpace(phase) ? "探索阶段" : phase;
            if (m_Objective != null) m_Objective.text = string.IsNullOrWhiteSpace(objective) ? string.Empty : objective;
        }

        /// <summary>Shows or hides the boss header and updates its read model.</summary>
        public void SetBossState(string bossName, float currentHealth, float maxHealth, bool visible)
        {
            if (m_BossPanel != null)
            {
                m_BossPanel.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (!visible) return;

            if (m_BossName != null) m_BossName.text = bossName ?? string.Empty;
            SetVital(m_BossHealthBar, m_BossHealthValue, currentHealth, maxHealth);
        }

        /// <summary>Updates the compact blood-pact build summary.</summary>
        public void SetPactSummary(int pactCount, string buildName)
        {
            if (m_PactCount != null) m_PactCount.text = Mathf.Max(0, pactCount).ToString();
            if (m_BuildName != null)
            {
                m_BuildName.text = string.IsNullOrWhiteSpace(buildName) ? "尚未缔结血契" : buildName;
            }
        }

        /// <summary>Updates one named ability slot without owning its cooldown logic.</summary>
        public void SetAbilityCooldown(string slotName, float remainingSeconds, float durationSeconds)
        {
            if (m_HudRoot == null || string.IsNullOrWhiteSpace(slotName)) return;

            VisualElement slot = m_HudRoot.Q<VisualElement>(slotName);
            if (slot == null) return;

            Label cooldownLabel = slot.Q<Label>(className: "ability-cooldown");
            VisualElement cooldownShade = slot.Q<VisualElement>(className: "ability-cooldown-shade");
            float normalized = durationSeconds > 0f ? Mathf.Clamp01(remainingSeconds / durationSeconds) : 0f;

            slot.EnableInClassList("ability-slot--cooling", remainingSeconds > 0f);
            if (cooldownLabel != null)
            {
                cooldownLabel.text = remainingSeconds > 0f ? Mathf.CeilToInt(remainingSeconds).ToString() : string.Empty;
            }

            if (cooldownShade != null)
            {
                cooldownShade.style.height = Length.Percent(normalized * 100f);
            }
        }

        private static bool IsStat(StatChangePayload payload, string expectedName)
        {
            return string.Equals(payload.statName, expectedName, StringComparison.Ordinal);
        }

        private static void SetVital(ProgressBar bar, Label label, float current, float maximum)
        {
            float safeMaximum = Mathf.Max(0.0001f, maximum);
            float safeCurrent = Mathf.Clamp(current, 0f, safeMaximum);

            if (bar != null)
            {
                bar.lowValue = 0f;
                bar.highValue = safeMaximum;
                bar.value = safeCurrent;
            }

            if (label != null)
            {
                label.text = $"{Mathf.CeilToInt(safeCurrent)} / {Mathf.CeilToInt(safeMaximum)}";
            }
        }

        private void UpdateLowHealthPresentation(float normalizedHealth)
        {
            float normalized = Mathf.Clamp01(normalizedHealth);
            bool isLowHealth = normalized <= 0.3f;
            m_HudRoot?.EnableInClassList("hud--low-health", isLowHealth);

            if (m_LowHealthVignette != null)
            {
                m_LowHealthVignette.style.opacity = isLowHealth
                    ? Mathf.Lerp(0.15f, 0.65f, 1f - normalized / 0.3f)
                    : 0f;
            }
        }

        private void ApplyThemeColors()
        {
            SetProgressColor(m_HealthBar, new Color(0.67f, 0.14f, 0.22f, 1f));
            SetProgressColor(m_StaminaBar, new Color(0.75f, 0.62f, 0.28f, 1f));
            SetProgressColor(m_ScarletBar, new Color(0.85f, 0.19f, 0.33f, 1f));
            SetProgressColor(m_BossHealthBar, new Color(0.50f, 0.07f, 0.15f, 1f));
        }

        private static void SetProgressColor(ProgressBar bar, Color color)
        {
            VisualElement fill = bar?.Q<VisualElement>(className: "unity-progress-bar__progress");
            if (fill != null) fill.style.backgroundColor = color;
        }
    }
}
