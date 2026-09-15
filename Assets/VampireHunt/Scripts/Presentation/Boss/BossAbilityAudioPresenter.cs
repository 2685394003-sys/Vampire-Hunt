using UnityEngine;
using VampireHunt.Presentation.Audio;

namespace VampireHunt.Presentation.Boss
{
    /// <summary>
    /// Consumes only audio fields from Boss ability presentation cues.
    /// 发声优先走 Wwise（cue 上的 audioEvent）；事件名为空时回退到 Unity 原生 AudioClip，
    /// 保证渐进迁移期间已经配好 AudioClip 的招式不会突然变哑。
    /// </summary>
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
            if (cueEvent.Cue == null) return;

            // Wwise 分支：Boss 招式音从此可走混音 / 总线 / 空间化，并与全局音量、暂停统一联动。
            if (!string.IsNullOrEmpty(cueEvent.Cue.AudioEvent))
            {
                AudioCue.Post(cueEvent.Cue.AudioEvent, gameObject);
                return;
            }

            // 回退分支：保留旧的 AudioSource 播放，供尚未迁移到 Wwise 的 cue 使用。
            // 全部 cue 都填好 audioEvent 之后，可连同 AudioSource 一起移除。
            if (audioSource == null || cueEvent.Cue.AudioClip == null) return;
            audioSource.PlayOneShot(cueEvent.Cue.AudioClip, cueEvent.Cue.AudioVolume);
        }
    }
}
