using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Netcode;
using VampireHunt.Infrastructure.Unity.Boss;

namespace VampireHunt.Presentation.Boss
{
    /// <summary>
    /// Client-only renderer for all beams in one replicated laser cast. It never selects a
    /// target or performs hit detection; direction snapshots come from the authoritative server.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossTrackingLaserVfxPresenter : MonoBehaviour
    {
        private sealed class ActiveBeam
        {
            public BossTrackingLaserPresentation Data;
            public bool[] PlayedCues;
            public Transform Target;
            public Vector3 CurrentDirection;
            public Vector3 AuthoritativeDirection;
            public Vector3 BeamLocalOffset;
            public BossTrackingLaserTargetMarkerVfxPresenter TargetMarker;
            public BossTrackingLaserBeamVfxPresenter Beam;
            public bool Released;
            public readonly List<GameObject> Instances = new List<GameObject>();
        }

        [SerializeField] private BossTrackingLaserNetworkBridge source;
        [SerializeField] private BossAbilityPhaseProvider phaseProvider;

        private readonly List<ActiveBeam> m_Beams = new List<ActiveBeam>();
        private BossAbilityAsset m_Ability;
        private uint m_AbilityId;
        private ulong m_CastSequence;

        private void Awake()
        {
            if (source == null) source = GetComponent<BossTrackingLaserNetworkBridge>();
            if (phaseProvider == null) phaseProvider = GetComponent<BossAbilityPhaseProvider>();
        }

        private void OnEnable()
        {
            if (source == null) return;
            source.LaserStarted += HandleStarted;
            source.LaserDirectionUpdated += HandleDirectionUpdated;
            source.LaserCancelled += HandleCancelled;
        }

        private void OnDisable()
        {
            if (source != null)
            {
                source.LaserStarted -= HandleStarted;
                source.LaserDirectionUpdated -= HandleDirectionUpdated;
                source.LaserCancelled -= HandleCancelled;
            }
            Clear();
        }

        private void Update()
        {
            if (m_Ability == null || m_Beams.Count == 0) return;
            double now = ReadServerTime();
            IReadOnlyList<BossAbilityPresentationCue> cues = m_Ability.PresentationCues;

            for (int beamIndex = 0; beamIndex < m_Beams.Count; beamIndex++)
            {
                ActiveBeam beam = m_Beams[beamIndex];
                if (beam.Target == null)
                    beam.Target = ResolvePlayerTransform(beam.Data.TargetEntityId);

                double elapsed = now - beam.Data.StartServerTime;
                if (elapsed < 0d) continue;
                if (!beam.Released && elapsed >= beam.Data.TelegraphDuration)
                    beam.Released = true;
                if (beam.Released)
                {
                    float radiansPerSecond = beam.Data.RotationSpeed * Mathf.Deg2Rad;
                    beam.CurrentDirection = Vector3.RotateTowards(
                        beam.CurrentDirection,
                        beam.AuthoritativeDirection,
                        radiansPerSecond * Time.deltaTime,
                        0f).normalized;
                }

                for (int cueIndex = 0; cueIndex < cues.Count; cueIndex++)
                {
                    BossAbilityPresentationCue cue = cues[cueIndex];
                    bool isLaserCue = cue.SpawnMode == BossAbilityCueSpawnMode.TrackingLaserTargetMarker ||
                                      cue.SpawnMode == BossAbilityCueSpawnMode.TrackingLaserBeam;
                    float cueTime = EffectiveCueTime(beam, cue);
                    if (beam.PlayedCues[cueIndex] || !isLaserCue || elapsed < cueTime) continue;
                    beam.PlayedCues[cueIndex] = true;
                    SpawnCue(beam, cue, cueTime, now);
                }

                if (beam.TargetMarker != null) beam.TargetMarker.SetTarget(beam.Target);
                if (beam.Beam != null)
                {
                    Vector3 origin = transform.position + transform.rotation * beam.BeamLocalOffset;
                    beam.Beam.SetPose(origin, beam.CurrentDirection);
                }
            }
        }

        private void HandleStarted(BossTrackingLaserPresentation presentation)
        {
            if (m_Ability == null || presentation.AbilityId != m_AbilityId ||
                presentation.CastSequence != m_CastSequence)
            {
                Clear();
                if (phaseProvider == null ||
                    !phaseProvider.TryGetAbility(presentation.AbilityId, out m_Ability)) return;
                m_AbilityId = presentation.AbilityId;
                m_CastSequence = presentation.CastSequence;
            }

            RemoveBeam(presentation.BeamIndex);
            Vector3 direction = Vector3.ProjectOnPlane(presentation.InitialDirection, Vector3.up);
            if (direction.sqrMagnitude <= .0001f) direction = transform.forward;
            direction.Normalize();
            m_Beams.Add(new ActiveBeam
            {
                Data = presentation,
                PlayedCues = new bool[m_Ability.PresentationCues.Count],
                Target = ResolvePlayerTransform(presentation.TargetEntityId),
                CurrentDirection = direction,
                AuthoritativeDirection = direction,
                BeamLocalOffset = Vector3.up,
                Released = false
            });
        }

        private void HandleDirectionUpdated(
            uint abilityId,
            ulong castSequence,
            uint beamIndex,
            Vector3 direction,
            bool snap)
        {
            if (abilityId != m_AbilityId || castSequence != m_CastSequence) return;
            ActiveBeam beam = FindBeam(beamIndex);
            if (beam == null) return;
            Vector3 planar = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (planar.sqrMagnitude <= .0001f) return;
            beam.AuthoritativeDirection = planar.normalized;
            if (snap)
            {
                beam.CurrentDirection = beam.AuthoritativeDirection;
                beam.Released = true;
            }
        }

        private void HandleCancelled(uint abilityId, ulong castSequence)
        {
            if (abilityId == m_AbilityId && castSequence == m_CastSequence) Clear();
        }

        private void SpawnCue(
            ActiveBeam beam,
            BossAbilityPresentationCue cue,
            float cueTime,
            double now)
        {
            if (cue?.VfxPrefab == null) return;
            GameObject instance = Instantiate(cue.VfxPrefab);
            instance.transform.localScale = cue.LocalScale;
            beam.Instances.Add(instance);

            if (cue.SpawnMode == BossAbilityCueSpawnMode.TrackingLaserTargetMarker)
            {
                beam.TargetMarker = instance.GetComponent<BossTrackingLaserTargetMarkerVfxPresenter>();
                if (beam.TargetMarker != null) beam.TargetMarker.Configure(beam.Target, cue.LocalPosition);
            }
            else
            {
                beam.BeamLocalOffset = cue.LocalPosition;
                beam.Beam = instance.GetComponent<BossTrackingLaserBeamVfxPresenter>();
                if (beam.Beam != null) beam.Beam.Configure(beam.Data.Size);
            }

            if (cue.Lifetime > 0f)
            {
                float effectiveLifetime = BossAbilityTimeline.RemapLifetime(
                    cue.TimeFromCastStart,
                    cue.Lifetime,
                    m_Ability.TelegraphDuration,
                    beam.Data.TelegraphDuration);
                float alreadyElapsed = Mathf.Max(
                    0f,
                    (float)(now - beam.Data.StartServerTime - cueTime));
                Destroy(instance, Mathf.Max(.01f, effectiveLifetime - alreadyElapsed));
            }
        }

        private float EffectiveCueTime(ActiveBeam beam, BossAbilityPresentationCue cue) =>
            BossAbilityTimeline.RemapTime(
                cue.TimeFromCastStart,
                m_Ability.TelegraphDuration,
                beam.Data.TelegraphDuration);

        private ActiveBeam FindBeam(uint beamIndex)
        {
            for (int i = 0; i < m_Beams.Count; i++)
                if (m_Beams[i].Data.BeamIndex == beamIndex) return m_Beams[i];
            return null;
        }

        private void RemoveBeam(uint beamIndex)
        {
            for (int i = m_Beams.Count - 1; i >= 0; i--)
            {
                if (m_Beams[i].Data.BeamIndex != beamIndex) continue;
                ClearBeam(m_Beams[i]);
                m_Beams.RemoveAt(i);
            }
        }

        private static Transform ResolvePlayerTransform(ulong entityId)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening || manager.SpawnManager == null) return null;

            foreach (NetworkObject candidate in manager.SpawnManager.SpawnedObjects.Values)
            {
                if (candidate == null || !candidate.gameObject.activeInHierarchy) continue;
                MonoBehaviour[] behaviours = candidate.GetComponents<MonoBehaviour>();
                for (int i = 0; i < behaviours.Length; i++)
                {
                    if (behaviours[i] is ICombatEntityIdentity identity &&
                        identity.CombatEntityId.Value == entityId) return candidate.transform;
                }

                if (candidate.IsPlayerObject && candidate.OwnerClientId + 1UL == entityId)
                    return candidate.transform;
            }
            return null;
        }

        private static double ReadServerTime()
        {
            NetworkManager manager = NetworkManager.Singleton;
            return manager != null && manager.IsListening
                ? manager.ServerTime.Time
                : Time.unscaledTimeAsDouble;
        }

        private static void ClearBeam(ActiveBeam beam)
        {
            for (int i = 0; i < beam.Instances.Count; i++)
                if (beam.Instances[i] != null) Destroy(beam.Instances[i]);
            beam.Instances.Clear();
        }

        private void Clear()
        {
            for (int i = 0; i < m_Beams.Count; i++) ClearBeam(m_Beams[i]);
            m_Beams.Clear();
            m_Ability = null;
            m_AbilityId = 0;
            m_CastSequence = 0;
        }
    }
}
