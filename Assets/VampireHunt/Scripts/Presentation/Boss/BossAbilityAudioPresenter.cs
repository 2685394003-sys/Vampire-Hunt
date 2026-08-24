using UnityEngine;

namespace VampireHunt.Presentation.Boss
{
    /// <summary>Consumes only audio fields from Boss ability presentation cues.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BossAbilityPresenter))]
    [RequireComponent(typeof(AudioSource))]
    public sealed class BossAbilityAudioPresenter : MonoBehaviour
    {
        [SerializeField] private BossAbilityPresenter cueSource;
        [SerializeField] private AudioSource audioSource;

        private void Awake()
        {
            if (cueSource == null) cueSource = GetComponent<BossAbilityPresenter>();
            if (audioSource == null) audioSource = GetComponent<AudioSource>();
        }

        private void OnEnable()
        {
            if (cueSource != null) cueSource.CueTriggered += HandleCue;
        }

        private void OnDisable()
        {
            if (cueSource != null) cueSource.CueTriggered -= HandleCue;
        }

        private void HandleCue(BossAbilityCueEvent cueEvent)
        {
            if (audioSource == null || cueEvent.Cue?.AudioClip == null) return;
            audioSource.PlayOneShot(cueEvent.Cue.AudioClip, cueEvent.Cue.AudioVolume);
        }
    }
}
