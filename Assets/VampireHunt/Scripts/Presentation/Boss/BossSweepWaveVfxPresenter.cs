using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Boss.Abilities;
using VampireHunt.Infrastructure.Netcode;
using VampireHunt.Infrastructure.Unity.Boss;

namespace VampireHunt.Presentation.Boss
{
    /// <summary>Client-only renderer for the replicated one-or-two-pass sweep snapshot.</summary>
    [DisallowMultipleComponent]
    public sealed class BossSweepWaveVfxPresenter : MonoBehaviour
    {
        private sealed class ActivePass
        {
            public BossSweepPresentationPass Data;
            public bool[] PlayedCues;
            public readonly List<GameObject> Instances = new List<GameObject>();
            public double ClearServerTime;
            public bool Cancelled;
        }

        [SerializeField] private BossSweepTelegraphNetworkBridge source;
        [SerializeField] private BossAbilityPhaseProvider phaseProvider;

        private readonly List<ActivePass> m_Passes = new List<ActivePass>();
        private BossAbilityAsset m_Ability;
        private uint m_AbilityId;
        private ulong m_CastSequence;
        private double m_TelegraphDuration;

        private void Awake()
        {
            if (source == null) source = GetComponent<BossSweepTelegraphNetworkBridge>();
            if (phaseProvider == null) phaseProvider = GetComponent<BossAbilityPhaseProvider>();
        }

        private void OnEnable()
        {
            if (source == null) return;
            source.SweepStarted += HandleStarted;
            source.SweepPassCancelled += HandlePassCancelled;
            source.SweepCancelled += HandleCancelled;
        }

        private void OnDisable()
        {
            if (source != null)
            {
                source.SweepStarted -= HandleStarted;
                source.SweepPassCancelled -= HandlePassCancelled;
                source.SweepCancelled -= HandleCancelled;
            }
            ClearAll();
        }

        private void Update()
        {
            if (m_Ability == null) return;
            double now = ReadServerTime();
            IReadOnlyList<BossAbilityPresentationCue> cues = m_Ability.PresentationCues;

            for (int passIndex = m_Passes.Count - 1; passIndex >= 0; passIndex--)
            {
                ActivePass pass = m_Passes[passIndex];
                if (pass.Cancelled)
                {
                    ClearPass(pass);
                    m_Passes.RemoveAt(passIndex);
                    continue;
                }

                double elapsed = now - pass.Data.StartServerTime;
                if (elapsed >= 0d)
                {
                    for (int cueIndex = 0; cueIndex < cues.Count; cueIndex++)
                    {
                        BossAbilityPresentationCue cue = cues[cueIndex];
                        bool isSweepCue = cue.SpawnMode == BossAbilityCueSpawnMode.SweepWaveWarning ||
                                          cue.SpawnMode == BossAbilityCueSpawnMode.SweepWaveAttack;
                        float cueTime = EffectiveCueTime(cue);
                        if (pass.PlayedCues[cueIndex] || !isSweepCue ||
                            elapsed < cueTime) continue;
                        pass.PlayedCues[cueIndex] = true;
                        SpawnCue(pass, cue, now);
                    }
                }

                if (now >= pass.ClearServerTime)
                {
                    ClearPass(pass);
                    m_Passes.RemoveAt(passIndex);
                }
            }

            if (m_Passes.Count == 0) ResetIdentity();
        }

        private void HandleStarted(
            uint abilityId,
            ulong castSequence,
            double telegraphDuration,
            BossSweepPresentationPass[] passes)
        {
            ClearAll();
            if (passes == null || passes.Length == 0 || phaseProvider == null ||
                !phaseProvider.TryGetAbility(abilityId, out m_Ability)) return;

            m_AbilityId = abilityId;
            m_CastSequence = castSequence;
            m_TelegraphDuration = telegraphDuration;
            for (int i = 0; i < passes.Length; i++)
            {
                m_Passes.Add(new ActivePass
                {
                    Data = passes[i],
                    PlayedCues = new bool[m_Ability.PresentationCues.Count],
                    ClearServerTime = CalculateClearTime(
                        m_Ability, passes[i].StartServerTime, m_TelegraphDuration)
                });
            }
            Update();
        }

        private void HandlePassCancelled(uint abilityId, ulong castSequence, uint passIndex)
        {
            if (abilityId != m_AbilityId || castSequence != m_CastSequence) return;
            for (int i = 0; i < m_Passes.Count; i++)
            {
                if (m_Passes[i].Data.PassIndex != passIndex) continue;
                m_Passes[i].Cancelled = true;
                break;
            }
        }

        private void HandleCancelled(uint abilityId, ulong castSequence)
        {
            if (abilityId == m_AbilityId && castSequence == m_CastSequence) ClearAll();
        }

        private void SpawnCue(ActivePass pass, BossAbilityPresentationCue cue, double now)
        {
            if (cue?.VfxPrefab == null) return;
            Vector3 forward = Vector3.ProjectOnPlane(pass.Data.Forward, Vector3.up);
            if (forward.sqrMagnitude <= .0001f) forward = Vector3.forward;
            forward.Normalize();
            Quaternion areaRotation = Quaternion.LookRotation(forward, Vector3.up);
            Quaternion rotation = areaRotation * Quaternion.Euler(cue.LocalEulerAngles);
            Vector3 position = pass.Data.Center + areaRotation * cue.LocalPosition;

            GameObject instance = Instantiate(cue.VfxPrefab, position, rotation);
            float cueTime = EffectiveCueTime(cue);
            if (cue.SpawnMode == BossAbilityCueSpawnMode.SweepWaveWarning)
            {
                instance.transform.localScale = Vector3.Scale(
                    cue.LocalScale,
                    new Vector3(pass.Data.Size.x, 1f, pass.Data.Size.z));
                if (instance.TryGetComponent(out BossSweepWaveWarningVfxPresenter warning))
                    warning.Configure(
                        pass.Data.StartServerTime + cueTime,
                        (float)m_TelegraphDuration,
                        pass.Data.Direction);
            }
            else
            {
                instance.transform.position += Vector3.up * m_Ability.Tuning.VfxHeight;
                instance.transform.localScale = cue.LocalScale;
                if (instance.TryGetComponent(out BossSweepWaveAttackVfxPresenter attack))
                    attack.Configure(
                        pass.Data.StartServerTime + cueTime,
                        m_Ability.Tuning.TravelDuration,
                        pass.Data.Direction,
                        pass.Data.Size.x,
                        pass.Data.Size.y);
            }

            if (cue.Lifetime > 0f)
            {
                float effectiveLifetime = BossAbilityTimeline.RemapLifetime(
                    cue.TimeFromCastStart,
                    cue.Lifetime,
                    m_Ability.TelegraphDuration,
                    m_TelegraphDuration);
                float elapsedSinceCue = Mathf.Max(
                    0f,
                    (float)(now - pass.Data.StartServerTime - cueTime));
                Destroy(instance, Mathf.Max(.01f, effectiveLifetime - elapsedSinceCue));
            }
            pass.Instances.Add(instance);
        }

        private static double CalculateClearTime(
            BossAbilityAsset ability,
            double passStartServerTime,
            double effectiveTelegraphDuration)
        {
            double latest = passStartServerTime;
            IReadOnlyList<BossAbilityPresentationCue> cues = ability.PresentationCues;
            for (int i = 0; i < cues.Count; i++)
            {
                BossAbilityPresentationCue cue = cues[i];
                if (cue.SpawnMode != BossAbilityCueSpawnMode.SweepWaveWarning &&
                    cue.SpawnMode != BossAbilityCueSpawnMode.SweepWaveAttack) continue;
                float effectiveStart = BossAbilityTimeline.RemapTime(
                    cue.TimeFromCastStart,
                    ability.TelegraphDuration,
                    effectiveTelegraphDuration);
                float effectiveLifetime = BossAbilityTimeline.RemapLifetime(
                    cue.TimeFromCastStart,
                    cue.Lifetime,
                    ability.TelegraphDuration,
                    effectiveTelegraphDuration);
                latest = System.Math.Max(
                    latest,
                    passStartServerTime + effectiveStart + Mathf.Max(0f, effectiveLifetime));
            }
            return latest;
        }

        private float EffectiveCueTime(BossAbilityPresentationCue cue) =>
            BossAbilityTimeline.RemapTime(
                cue.TimeFromCastStart,
                m_Ability.TelegraphDuration,
                m_TelegraphDuration);

        private static double ReadServerTime()
        {
            NetworkManager manager = NetworkManager.Singleton;
            return manager != null && manager.IsListening
                ? manager.ServerTime.Time
                : Time.unscaledTimeAsDouble;
        }

        private static void ClearPass(ActivePass pass)
        {
            for (int i = 0; i < pass.Instances.Count; i++)
                if (pass.Instances[i] != null) Destroy(pass.Instances[i]);
            pass.Instances.Clear();
        }

        private void ClearAll()
        {
            for (int i = 0; i < m_Passes.Count; i++) ClearPass(m_Passes[i]);
            m_Passes.Clear();
            ResetIdentity();
        }

        private void ResetIdentity()
        {
            m_Ability = null;
            m_AbilityId = 0;
            m_CastSequence = 0;
            m_TelegraphDuration = 0d;
        }
    }
}
