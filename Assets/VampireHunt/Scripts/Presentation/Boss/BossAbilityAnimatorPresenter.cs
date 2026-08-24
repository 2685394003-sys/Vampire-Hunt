using UnityEngine;

namespace VampireHunt.Presentation.Boss
{
    /// <summary>Consumes only animation fields from Boss ability presentation cues.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BossAbilityPresenter))]
    public sealed class BossAbilityAnimatorPresenter : MonoBehaviour
    {
        [SerializeField] private BossAbilityPresenter cueSource;
        [SerializeField] private Animator animator;

        private void Awake()
        {
            if (cueSource == null) cueSource = GetComponent<BossAbilityPresenter>();
            if (animator == null) animator = GetComponentInChildren<Animator>();
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
            string trigger = cueEvent.Cue?.AnimatorTrigger;
            if (animator != null && !string.IsNullOrWhiteSpace(trigger))
                animator.SetTrigger(trigger);
        }
    }
}
