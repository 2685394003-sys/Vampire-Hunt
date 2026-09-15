using System.Collections.Generic;
using UnityEngine;
using VampireHunt.Infrastructure.Unity.Boss;

namespace VampireHunt.Presentation.Boss
{
    /// <summary>Consumes only VFX fields from Boss ability presentation cues.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BossAbilityPresenter))]
    public sealed class BossAbilityVfxPresenter : MonoBehaviour
    {
        [SerializeField] private BossAbilityPresenter cueSource;

        private readonly List<GameObject> m_CastOwnedObjects = new List<GameObject>();

        private void Awake()
        {
            if (cueSource == null) cueSource = GetComponent<BossAbilityPresenter>();
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
            ClearOwnedObjects();
        }

        private void HandleCue(BossAbilityCueEvent cueEvent)
        {
            BossAbilityPresentationCue cue = cueEvent.Cue;
            if (cue?.VfxPrefab == null || cue.SpawnMode != BossAbilityCueSpawnMode.SingleAnchor) return;

            Transform anchor = cueEvent.Anchor != null ? cueEvent.Anchor : transform;
            Quaternion localRotation = Quaternion.Euler(cue.LocalEulerAngles);
            GameObject instance;

            if (cue.FollowAnchor)
            {
                instance = Instantiate(cue.VfxPrefab, anchor, false);
                instance.transform.localPosition = cue.LocalPosition;
                instance.transform.localRotation = localRotation;
                instance.transform.localScale = cue.LocalScale;
            }
            else
            {
                instance = Instantiate(
                    cue.VfxPrefab,
                    anchor.TransformPoint(cue.LocalPosition),
                    anchor.rotation * localRotation);
                instance.transform.localScale = Vector3.Scale(anchor.lossyScale, cue.LocalScale);
            }

            if (cueEvent.EffectiveLifetime > 0f) Destroy(instance, cueEvent.EffectiveLifetime);
            else m_CastOwnedObjects.Add(instance);
        }

        private void HandleCastCleared(ulong castSequence)
        {
            ClearOwnedObjects();
        }

        private void ClearOwnedObjects()
        {
            for (int i = 0; i < m_CastOwnedObjects.Count; i++)
            {
                if (m_CastOwnedObjects[i] != null) Destroy(m_CastOwnedObjects[i]);
            }
            m_CastOwnedObjects.Clear();
        }
    }
}
