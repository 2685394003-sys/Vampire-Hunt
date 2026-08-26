using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Netcode;
using VampireHunt.Infrastructure.Unity.Boss;

namespace VampireHunt.Presentation.Boss
{
    /// <summary>Client renderer for the replicated target marker and smoothly tracking beam.</summary>
    [DisallowMultipleComponent]
    public sealed class BossTrackingLaserVfxPresenter : MonoBehaviour
    {
        [SerializeField] private BossTrackingLaserNetworkBridge source;
        [SerializeField] private BossAbilityPhaseProvider phaseProvider;

        private BossTrackingLaserPresentation m_Presentation;
        private BossAbilityAsset m_Ability;
        private bool[] m_PlayedCues;
        private Transform m_Target;
        private Vector3 m_CurrentDirection = Vector3.forward;
        private BossTrackingLaserTargetMarkerVfxPresenter m_TargetMarker;
        private BossTrackingLaserBeamVfxPresenter m_Beam;
        private bool m_ReleaseStarted;
        private readonly List<GameObject> m_Instances = new List<GameObject>();

        private void Awake()
        {
            if (source == null) source = GetComponent<BossTrackingLaserNetworkBridge>();
            if (phaseProvider == null) phaseProvider = GetComponent<BossAbilityPhaseProvider>();
        }

        private void OnEnable()
        {
            if (source == null) return;
            source.LaserStarted += HandleStarted;
            source.LaserCancelled += HandleCancelled;
        }

        private void OnDisable()
        {
            if (source != null)
            {
                source.LaserStarted -= HandleStarted;
                source.LaserCancelled -= HandleCancelled;
            }
            Clear();
        }

        private void Update()
        {
            if (m_Ability == null) return;
            if (m_Target == null) m_Target = ResolvePlayerTransform(m_Presentation.TargetEntityId);

            double now = ReadServerTime();
            double elapsed = now - m_Presentation.StartServerTime;
            Vector3 desired = ResolveDesiredDirection();
            if (!m_ReleaseStarted && elapsed >= m_Ability.TelegraphDuration)
            {
                // Match the authoritative release: face the locked player immediately.
                m_CurrentDirection = desired;
                m_ReleaseStarted = true;
            }
            else if (m_ReleaseStarted)
            {
                // Only the active beam tracks slowly; the telegraph does not rotate the Boss.
                float radiansPerSecond = m_Presentation.RotationSpeed * Mathf.Deg2Rad;
                m_CurrentDirection = Vector3.RotateTowards(
                    m_CurrentDirection,
                    desired,
                    radiansPerSecond * Time.deltaTime,
                    0f).normalized;
            }

            IReadOnlyList<BossAbilityPresentationCue> cues = m_Ability.PresentationCues;
            for (int i = 0; i < cues.Count; i++)
            {
                BossAbilityPresentationCue cue = cues[i];
                bool isLaserCue = cue.SpawnMode == BossAbilityCueSpawnMode.TrackingLaserTargetMarker ||
                                  cue.SpawnMode == BossAbilityCueSpawnMode.TrackingLaserBeam;
                if (m_PlayedCues[i] || !isLaserCue || elapsed < cue.TimeFromCastStart) continue;
                m_PlayedCues[i] = true;
                SpawnCue(cue, now);
            }

            if (m_TargetMarker != null) m_TargetMarker.SetTarget(m_Target);
            if (m_Beam != null)
            {
                BossAbilityPresentationCue beamCue = FindCue(BossAbilityCueSpawnMode.TrackingLaserBeam);
                Vector3 localOffset = beamCue != null ? beamCue.LocalPosition : Vector3.up;
                Vector3 origin = transform.position + transform.rotation * localOffset;
                m_Beam.SetPose(origin, m_CurrentDirection);
            }
        }

        private void HandleStarted(BossTrackingLaserPresentation presentation)
        {
            Clear();
            if (phaseProvider == null ||
                !phaseProvider.TryGetAbility(presentation.AbilityId, out m_Ability)) return;
            m_Presentation = presentation;
            m_PlayedCues = new bool[m_Ability.PresentationCues.Count];
            m_CurrentDirection = Vector3.ProjectOnPlane(presentation.InitialDirection, Vector3.up);
            if (m_CurrentDirection.sqrMagnitude <= .0001f) m_CurrentDirection = transform.forward;
            m_CurrentDirection.Normalize();
            m_ReleaseStarted = false;
            m_Target = ResolvePlayerTransform(presentation.TargetEntityId);
            Update();
        }

        private Vector3 ResolveDesiredDirection()
        {
            if (m_Target == null) return m_CurrentDirection;
            Vector3 desired = Vector3.ProjectOnPlane(m_Target.position - transform.position, Vector3.up);
            return desired.sqrMagnitude > .0001f ? desired.normalized : m_CurrentDirection;
        }

        private void HandleCancelled(uint abilityId, ulong castSequence)
        {
            if (m_Ability != null && abilityId == m_Presentation.AbilityId &&
                castSequence == m_Presentation.CastSequence) Clear();
        }

        private void SpawnCue(BossAbilityPresentationCue cue, double now)
        {
            if (cue?.VfxPrefab == null) return;
            GameObject instance = Instantiate(cue.VfxPrefab);
            instance.transform.localScale = cue.LocalScale;
            m_Instances.Add(instance);

            if (cue.SpawnMode == BossAbilityCueSpawnMode.TrackingLaserTargetMarker)
            {
                m_TargetMarker = instance.GetComponent<BossTrackingLaserTargetMarkerVfxPresenter>();
                if (m_TargetMarker != null) m_TargetMarker.Configure(m_Target, cue.LocalPosition);
            }
            else
            {
                m_Beam = instance.GetComponent<BossTrackingLaserBeamVfxPresenter>();
                if (m_Beam != null) m_Beam.Configure(m_Presentation.Size);
            }

            if (cue.Lifetime > 0f)
            {
                float alreadyElapsed = Mathf.Max(
                    0f,
                    (float)(now - m_Presentation.StartServerTime - cue.TimeFromCastStart));
                Destroy(instance, Mathf.Max(.01f, cue.Lifetime - alreadyElapsed));
            }
        }

        private BossAbilityPresentationCue FindCue(BossAbilityCueSpawnMode spawnMode)
        {
            if (m_Ability == null) return null;
            IReadOnlyList<BossAbilityPresentationCue> cues = m_Ability.PresentationCues;
            for (int i = 0; i < cues.Count; i++)
                if (cues[i].SpawnMode == spawnMode) return cues[i];
            return null;
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

        private void Clear()
        {
            for (int i = 0; i < m_Instances.Count; i++)
                if (m_Instances[i] != null) Destroy(m_Instances[i]);
            m_Instances.Clear();
            m_TargetMarker = null;
            m_Beam = null;
            m_Target = null;
            m_Ability = null;
            m_PlayedCues = null;
            m_ReleaseStarted = false;
        }
    }
}
