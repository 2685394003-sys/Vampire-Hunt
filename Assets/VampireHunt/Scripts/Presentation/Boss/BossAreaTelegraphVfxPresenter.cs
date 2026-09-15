using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Boss.Abilities;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Netcode;
using VampireHunt.Infrastructure.Unity.Boss;

namespace VampireHunt.Presentation.Boss
{
    /// <summary>
    /// Presentation-only renderer for replicated fixed area snapshots. Every client creates
    /// the same editable skill VFX at the server-captured world positions.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossAreaTelegraphVfxPresenter : MonoBehaviour
    {
        [SerializeField] private BossAreaTelegraphNetworkBridge source;
        [SerializeField] private BossAbilityPhaseProvider phaseProvider;

        private readonly List<GameObject> m_TelegraphInstances = new List<GameObject>();
        private readonly List<LingeringVfxInstance> m_LingeringVfxInstances =
            new List<LingeringVfxInstance>();
        private BossAreaTelegraphPresentation m_Current;
        private BossAbilityAsset m_Ability;
        private bool[] m_PlayedCues;
        private double m_ClearServerTime;

        private void Awake()
        {
            if (source == null) source = GetComponent<BossAreaTelegraphNetworkBridge>();
            if (phaseProvider == null) phaseProvider = GetComponent<BossAbilityPhaseProvider>();
        }

        private void OnEnable()
        {
            if (source == null) return;
            source.TelegraphStarted += HandleStarted;
            source.TelegraphCancelled += HandleCancelled;
        }

        private void OnDisable()
        {
            if (source != null)
            {
                source.TelegraphStarted -= HandleStarted;
                source.TelegraphCancelled -= HandleCancelled;
            }
            ClearActiveTelegraph();
            ClearLingeringVfx();
        }

        private void Update()
        {
            double now = ReadServerTime();
            UpdateLingeringVfx(now);
            if (m_Ability == null || m_PlayedCues == null) return;
            double elapsed = now - m_Current.StartServerTime;
            if (elapsed < 0d) return;

            IReadOnlyList<BossAbilityPresentationCue> cues = m_Ability.PresentationCues;
            for (int i = 0; i < cues.Count; i++)
            {
                BossAbilityPresentationCue cue = cues[i];
                if (m_PlayedCues[i] ||
                    cue.SpawnMode == BossAbilityCueSpawnMode.SingleAnchor ||
                    elapsed < EffectiveCueTime(cue)) continue;
                m_PlayedCues[i] = true;
                if (cue.SpawnMode == BossAbilityCueSpawnMode.DirectionalTravel)
                    SpawnDirectionalTravel(cue);
                else
                    SpawnCueAtEveryCenter(cue);
            }

            if (now >= m_ClearServerTime) ClearActiveTelegraph();
        }

        private void HandleStarted(BossAreaTelegraphPresentation presentation)
        {
            // A new cast replaces only the active warning. Released VFX from an older cast
            // owns an independent lifetime and must be allowed to finish.
            ClearActiveTelegraph();
            if (presentation.Centers.Length == 0 || phaseProvider == null ||
                !phaseProvider.TryGetAbility(presentation.AbilityId, out m_Ability)) return;

            m_Current = presentation;
            m_PlayedCues = new bool[m_Ability.PresentationCues.Count];
            m_ClearServerTime = CalculateClearTime(
                m_Ability, presentation.StartServerTime, presentation.TelegraphDuration);
            Update();
        }

        private void HandleCancelled(uint abilityId, ulong castSequence)
        {
            if (m_Ability != null && m_Current.AbilityId == abilityId &&
                m_Current.CastSequence == castSequence) ClearActiveTelegraph();
        }

        private void SpawnCueAtEveryCenter(BossAbilityPresentationCue cue)
        {
            if (cue?.VfxPrefab == null) return;
            bool isBox = m_Current.Shape == BossAreaTelegraphShape.Box;
            Quaternion areaRotation = isBox
                ? Quaternion.LookRotation(FlattenedForward(), Vector3.up)
                : Quaternion.identity;
            // The prefab owns its authored visual orientation. Area/cue rotations are
            // placement offsets, not replacements for the prefab root rotation.
            Quaternion rotation = areaRotation *
                                  Quaternion.Euler(cue.LocalEulerAngles) *
                                  cue.VfxPrefab.transform.localRotation;
            Vector3 areaScale = isBox
                ? new Vector3(m_Current.Size.x, 1f, m_Current.Size.z)
                : Vector3.one * Mathf.Max(.01f, m_Current.Radius * 2f);
            Vector3 placementScale = cue.UseManualScale ? Vector3.one : areaScale;
            Vector3 scale = Vector3.Scale(
                cue.VfxPrefab.transform.localScale,
                Vector3.Scale(cue.LocalScale, placementScale));

            for (int i = 0; i < m_Current.Centers.Length; i++)
            {
                GameObject instance = Instantiate(
                    cue.VfxPrefab,
                    m_Current.Centers[i] + areaRotation * cue.LocalPosition,
                    rotation);
                instance.transform.localScale = scale;
                if (instance.TryGetComponent(out BossChargeSlashWarningVfxPresenter warning))
                    warning.Configure(
                        m_Current.StartServerTime,
                        (float)m_Current.TelegraphDuration);
                TrackInstance(instance, cue);
            }
        }

        private void SpawnDirectionalTravel(BossAbilityPresentationCue cue)
        {
            if (cue?.VfxPrefab == null || m_Current.Shape != BossAreaTelegraphShape.Box ||
                m_Current.Centers.Length == 0) return;

            Vector3 forward = FlattenedForward();
            Vector3 center = m_Current.Centers[0];
            Vector3 start = center - forward * (m_Current.Size.z * .5f);
            Vector3 end = center + forward * (m_Current.Size.z * .5f);
            Quaternion rotation = Quaternion.LookRotation(forward, Vector3.up) *
                                  Quaternion.Euler(cue.LocalEulerAngles);
            Vector3 worldOffset = Quaternion.LookRotation(forward, Vector3.up) * cue.LocalPosition;

            GameObject instance = Instantiate(cue.VfxPrefab, start + worldOffset, rotation);
            instance.transform.localScale = cue.LocalScale;
            if (instance.TryGetComponent(out BossChargeSlashSwordQiVfxPresenter swordQi))
            {
                BossAbilityTuning tuning = m_Ability.Tuning;
                swordQi.Configure(
                    start + worldOffset + Vector3.up * tuning.VfxHeight,
                    end + worldOffset + Vector3.up * tuning.VfxHeight,
                    m_Current.StartServerTime + EffectiveCueTime(cue),
                    tuning.TravelDuration,
                    tuning.DissolveDuration,
                    m_Current.Size.x,
                    m_Current.Size.y);
            }
            TrackInstance(instance, cue);
        }

        private void TrackInstance(GameObject instance, BossAbilityPresentationCue cue)
        {
            if (instance == null) return;
            float effectiveLifetime = BossAbilityTimeline.RemapLifetime(
                cue.TimeFromCastStart,
                cue.Lifetime,
                m_Ability.TelegraphDuration,
                m_Current.TelegraphDuration);
            if (!cue.KeepAliveAfterCastEnd || effectiveLifetime <= 0f)
            {
                m_TelegraphInstances.Add(instance);
                return;
            }

            double expiresAt = m_Current.StartServerTime + EffectiveCueTime(cue) + effectiveLifetime;
            if (ReadServerTime() >= expiresAt)
            {
                Destroy(instance);
                return;
            }
            m_LingeringVfxInstances.Add(new LingeringVfxInstance(instance, expiresAt));
        }

        private void UpdateLingeringVfx(double now)
        {
            for (int i = m_LingeringVfxInstances.Count - 1; i >= 0; i--)
            {
                LingeringVfxInstance entry = m_LingeringVfxInstances[i];
                if (entry.Instance != null && now < entry.ExpiresAtServerTime) continue;
                if (entry.Instance != null) Destroy(entry.Instance);
                m_LingeringVfxInstances.RemoveAt(i);
            }
        }

        private static double CalculateClearTime(
            BossAbilityAsset ability,
            double startServerTime,
            double effectiveTelegraphDuration)
        {
            double latest = startServerTime;
            IReadOnlyList<BossAbilityPresentationCue> cues = ability.PresentationCues;
            for (int i = 0; i < cues.Count; i++)
            {
                BossAbilityPresentationCue cue = cues[i];
                if (cue.SpawnMode == BossAbilityCueSpawnMode.SingleAnchor) continue;
                float effectiveStart = BossAbilityTimeline.RemapTime(
                    cue.TimeFromCastStart,
                    ability.TelegraphDuration,
                    effectiveTelegraphDuration);
                float effectiveLifetime = BossAbilityTimeline.RemapLifetime(
                    cue.TimeFromCastStart,
                    cue.Lifetime,
                    ability.TelegraphDuration,
                    effectiveTelegraphDuration);
                latest = System.Math.Max(latest,
                    startServerTime + effectiveStart + Mathf.Max(0f, effectiveLifetime));
            }
            return latest;
        }

        private float EffectiveCueTime(BossAbilityPresentationCue cue) =>
            BossAbilityTimeline.RemapTime(
                cue.TimeFromCastStart,
                m_Ability.TelegraphDuration,
                m_Current.TelegraphDuration);

        private double ReadServerTime()
        {
            NetworkManager manager = NetworkManager.Singleton;
            return manager != null && manager.IsListening
                ? manager.ServerTime.Time
                : Time.unscaledTimeAsDouble;
        }

        private Vector3 FlattenedForward()
        {
            Vector3 forward = Vector3.ProjectOnPlane(m_Current.Forward, Vector3.up);
            return forward.sqrMagnitude > .0001f ? forward.normalized : Vector3.forward;
        }

        private void ClearActiveTelegraph()
        {
            for (int i = 0; i < m_TelegraphInstances.Count; i++)
                if (m_TelegraphInstances[i] != null) Destroy(m_TelegraphInstances[i]);
            m_TelegraphInstances.Clear();
            m_Current = default;
            m_Ability = null;
            m_PlayedCues = null;
            m_ClearServerTime = 0d;
        }

        private void ClearLingeringVfx()
        {
            for (int i = 0; i < m_LingeringVfxInstances.Count; i++)
                if (m_LingeringVfxInstances[i].Instance != null)
                    Destroy(m_LingeringVfxInstances[i].Instance);
            m_LingeringVfxInstances.Clear();
        }

        private readonly struct LingeringVfxInstance
        {
            public GameObject Instance { get; }
            public double ExpiresAtServerTime { get; }

            public LingeringVfxInstance(GameObject instance, double expiresAtServerTime)
            {
                Instance = instance;
                ExpiresAtServerTime = expiresAtServerTime;
            }
        }
    }
}
