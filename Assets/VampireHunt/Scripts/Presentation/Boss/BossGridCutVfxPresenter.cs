using System.Collections.Generic;
using UnityEngine;
using VampireHunt.Boss.Abilities;
using VampireHunt.Infrastructure.Unity.Boss;

namespace VampireHunt.Presentation.Boss
{
    /// <summary>Consumes replicated cast cues and renders all three Grid Cut cycles.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BossAbilityPresenter))]
    public sealed class BossGridCutVfxPresenter : MonoBehaviour
    {
        [SerializeField] private BossAbilityPresenter cueSource;
        [SerializeField] private BossAbilityPhaseProvider phaseProvider;

        private readonly List<GameObject> m_Instances = new List<GameObject>();

        private void Awake()
        {
            if (cueSource == null) cueSource = GetComponent<BossAbilityPresenter>();
            if (phaseProvider == null) phaseProvider = GetComponent<BossAbilityPhaseProvider>();
        }

        private void OnEnable()
        {
            if (cueSource == null) return;
            cueSource.CueTriggered += HandleCue;
            cueSource.CastPresentationCleared += HandleCastCleared;
        }

        private void OnDisable()
        {
            if (cueSource != null)
            {
                cueSource.CueTriggered -= HandleCue;
                cueSource.CastPresentationCleared -= HandleCastCleared;
            }
            Clear();
        }

        private void HandleCue(BossAbilityCueEvent cueEvent)
        {
            BossAbilityPresentationCue cue = cueEvent.Cue;
            bool isWarning = cue?.SpawnMode == BossAbilityCueSpawnMode.GridCutWarning;
            bool isBurst = cue?.SpawnMode == BossAbilityCueSpawnMode.GridCutBurst;
            if ((!isWarning && !isBurst) || cue.VfxPrefab == null || phaseProvider == null ||
                !phaseProvider.TryGetAbility(cueEvent.State.AbilityId, out BossAbilityAsset ability)) return;

            Transform anchor = cueEvent.Anchor != null ? cueEvent.Anchor : transform;
            GameObject instance = Instantiate(
                cue.VfxPrefab,
                anchor.TransformPoint(cue.LocalPosition),
                Quaternion.Euler(cue.LocalEulerAngles));
            instance.transform.localScale = cue.LocalScale;
            m_Instances.Add(instance);

            BossAbilityTuning tuning = ability.Tuning;
            if (isWarning && instance.TryGetComponent(out BossGridCutWarningVfxPresenter warning))
            {
                warning.Configure(
                    cueEvent.State.CastStartServerTime,
                    (float)cueEvent.State.TelegraphDuration,
                    tuning.Interval,
                    tuning.Repetitions,
                    tuning.Radius,
                    tuning.GridLineCount,
                    tuning.Width);
            }
            else if (isBurst && instance.TryGetComponent(out BossGridCutBurstVfxPresenter burst))
            {
                burst.Configure(
                    cueEvent.State.CastStartServerTime + cueEvent.ScheduledTimeFromCastStart,
                    tuning.Interval,
                    tuning.Repetitions,
                    tuning.Radius,
                    tuning.GridLineCount,
                    tuning.Width);
            }
        }

        private void HandleCastCleared(ulong castSequence) => Clear();

        private void Clear()
        {
            for (int i = 0; i < m_Instances.Count; i++)
                if (m_Instances[i] != null) Destroy(m_Instances[i]);
            m_Instances.Clear();
        }
    }
}
