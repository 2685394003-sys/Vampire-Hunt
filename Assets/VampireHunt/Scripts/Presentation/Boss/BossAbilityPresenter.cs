using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Infrastructure.Netcode;
using VampireHunt.Infrastructure.Unity.Boss;

namespace VampireHunt.Presentation.Boss
{
    public readonly struct BossAbilityCueEvent
    {
        public BossAbilityNetworkState State { get; }
        public BossAbilityPresentationCue Cue { get; }
        public Transform Anchor { get; }
        public float ScheduledTimeFromCastStart { get; }
        public float EffectiveLifetime { get; }

        public BossAbilityCueEvent(
            in BossAbilityNetworkState state,
            BossAbilityPresentationCue cue,
            Transform anchor,
            float scheduledTimeFromCastStart,
            float effectiveLifetime)
        {
            State = state;
            Cue = cue;
            Anchor = anchor;
            ScheduledTimeFromCastStart = scheduledTimeFromCastStart;
            EffectiveLifetime = effectiveLifetime;
        }
    }

    /// <summary>
    /// Converts a replicated cast timeline into local presentation cue events. Animation,
    /// VFX and audio are deliberately handled by sibling presenter components.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossAbilityPresenter : MonoBehaviour
    {
        [SerializeField] private BossAbilityStateReplicator stateReplicator;
        [SerializeField] private BossAbilityPhaseProvider phaseProvider;
        [SerializeField] private BossAbilityAnchorRegistry anchors;

        private BossAbilityNetworkState m_State;
        private BossAbilityAsset m_Ability;
        private bool[] m_PlayedCues;
        private ulong m_PresentedSequence;

        public event System.Action<BossAbilityCueEvent> CueTriggered;
        public event System.Action<ulong> CastPresentationCleared;

        private void Awake()
        {
            if (stateReplicator == null) stateReplicator = GetComponent<BossAbilityStateReplicator>();
            if (phaseProvider == null) phaseProvider = GetComponent<BossAbilityPhaseProvider>();
            if (anchors == null) anchors = GetComponent<BossAbilityAnchorRegistry>();
        }

        private void OnEnable()
        {
            if (stateReplicator != null)
            {
                stateReplicator.StateChanged += HandleStateChanged;
                HandleStateChanged(stateReplicator.CurrentState);
            }
        }

        private void OnDisable()
        {
            if (stateReplicator != null) stateReplicator.StateChanged -= HandleStateChanged;
            ClearCastPresentation();
        }

        private void Update()
        {
            if (m_Ability == null || !m_State.IsCasting || m_PlayedCues == null) return;
            double elapsed = ReadServerTime() - m_State.CastStartServerTime;
            if (elapsed < 0d) return;

            IReadOnlyList<BossAbilityPresentationCue> cues = m_Ability.PresentationCues;
            for (int i = 0; i < cues.Count; i++)
            {
                float cueTime = EffectiveCueTime(cues[i]);
                if (m_PlayedCues[i] || elapsed < cueTime) continue;
                m_PlayedCues[i] = true;
                PlayCue(cues[i], cueTime);
            }
        }

        private void HandleStateChanged(BossAbilityNetworkState state)
        {
            m_State = state;
            if (!state.IsCasting)
            {
                ClearCastPresentation();
                return;
            }

            if (m_Ability != null && m_PresentedSequence == state.CastSequence &&
                m_Ability.AbilityId == state.AbilityId) return;

            ClearCastPresentation();
            if (phaseProvider == null || !phaseProvider.TryGetAbility(state.AbilityId, out m_Ability))
            {
                Debug.LogWarning($"[BossAbilityPresenter] Ability {state.AbilityId} is missing from the local phase set.", this);
                return;
            }

            m_PresentedSequence = state.CastSequence;
            m_PlayedCues = new bool[m_Ability.PresentationCues.Count];
        }

        private void PlayCue(BossAbilityPresentationCue cue, float scheduledTime)
        {
            if (cue == null) return;
            Transform anchor = anchors != null ? anchors.Resolve(cue.Anchor) : transform;
            float effectiveLifetime = BossAbilityTimeline.RemapLifetime(
                cue.TimeFromCastStart,
                cue.Lifetime,
                m_Ability.TelegraphDuration,
                m_State.TelegraphDuration);
            CueTriggered?.Invoke(new BossAbilityCueEvent(
                m_State, cue, anchor, scheduledTime, effectiveLifetime));
        }

        private float EffectiveCueTime(BossAbilityPresentationCue cue) =>
            BossAbilityTimeline.RemapTime(
                cue.TimeFromCastStart,
                m_Ability.TelegraphDuration,
                m_State.TelegraphDuration);

        private double ReadServerTime()
        {
            NetworkManager manager = NetworkManager.Singleton;
            return manager != null && manager.IsListening
                ? manager.ServerTime.Time
                : Time.unscaledTimeAsDouble;
        }

        private void ClearCastPresentation()
        {
            ulong clearedSequence = m_PresentedSequence;
            m_Ability = null;
            m_PlayedCues = null;
            m_PresentedSequence = 0;
            if (clearedSequence != 0)
                CastPresentationCleared?.Invoke(clearedSequence);
        }
    }
}
