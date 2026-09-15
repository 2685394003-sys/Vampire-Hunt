using UnityEngine;
using Unity.Netcode.Components;

namespace Blocks.Gameplay.Core
{
    /// <summary>
    /// Controls the player's Animator component based on the state of the <see cref="CoreMovement"/> controller.
    /// This component is responsible for setting locomotion parameters (speed, grounded, jump, etc.)
    /// and handling Animation Events to trigger sound effects like footsteps and landing sounds.
    /// It inherits from <see cref="NetworkAnimator"/> to automatically synchronize animation states across the network.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class CoreAnimator : NetworkAnimator
    {
        #region Fields & Properties

        [Header("Component Dependencies")]
        [Tooltip("Reference to the CoreMovement component to get movement state information.")]
        [SerializeField] private CoreMovement coreMovement;

        [Header("Sound Effects")]
        [Tooltip("Sound definition for footstep sounds.")]
        [SerializeField] private SoundDef soundDefFootstep;

        [Header("Wwise Events")]
        [Tooltip("每迈一步触发一次（由 walk/run 的动画帧事件调用）。留空则静默。")]
        [SerializeField] private string footstepEventName = "Play_Player_Move";

        [Tooltip("落地时触发一次（由落地动画帧事件调用）。留空则静默。")]
        [SerializeField] private string landingEventName = "Play_Player_Land";

        [Tooltip("由「移动中」转为「静止」时触发一次。留空则静默。")]
        [SerializeField] private string moveStopEventName = "Play_Player_Move_Stop";

        [Tooltip("判定为「正在移动」的最小速度（米/秒）。")]
        [SerializeField] private float movingSpeedThreshold = 0.1f;

        private bool m_WasMoving;

        private readonly int m_AnimIDSpeed = Animator.StringToHash("Speed");
        private readonly int m_AnimIDGrounded = Animator.StringToHash("Grounded");
        private readonly int m_AnimIDJump = Animator.StringToHash("Jump");
        private readonly int m_AnimIDFreeFall = Animator.StringToHash("FreeFall");
        private readonly int m_AnimIDMotionSpeed = Animator.StringToHash("MotionSpeed");

        #endregion

        #region Unity & Network Lifecycle

        protected override void Awake()
        {
            base.Awake();
            if (coreMovement == null)
            {
                Debug.LogError("[Core Animator] needs a CoreMovement component.");
            }

            if (soundDefFootstep == null)
            {
                Debug.LogError("[Core Animator] Footstep SoundDef is not assigned.");
            }
        }

        private void Update()
        {
            if (coreMovement == null) return;

            // 「停止移动」的收尾音要在所有客户端判定（远端玩家的脚步同样需要收尾），
            // 因此放在 IsOwner 判断之前。
            UpdateFootstepState();

            // We only want the owner to send animation state updates.
            // NetworkAnimator will handle propagating these changes to other clients.
            if (!IsOwner) return;

            UpdateLocomotionParameters();
        }

        #endregion

        #region Animation Events

        public void OnFootstepWalk(AnimationEvent animationEvent)
        {
            // >=0.5 - We want to be sure that if both walk and run weights are equal, that we only trigger one SFX.
            if (animationEvent.animatorClipInfo.weight >= 0.5f)
            {
                OnFootstep(animationEvent, 0, 0.75f, 0);
            }
        }

        public void OnFootstepRun(AnimationEvent animationEvent)
        {
            if (animationEvent.animatorClipInfo.weight > 0.5f)
            {
                OnFootstep(animationEvent, 500, 1, 2000);
            }
        }

        /// <summary>
        /// This method is called by an AnimationEvent defined in the walk/run animation clips.
        /// It plays a random footstep sound.
        /// </summary>
        /// <param name="animationEvent">Data from the animation event.</param>
        /// <param name="walkRunPitchCents"></param>
        /// <param name="walkRunVolumeScale"></param>
        /// <param name="filterCutoffOffset"></param>
        public void OnFootstep(AnimationEvent animationEvent, float walkRunPitchCents, float walkRunVolumeScale, float filterCutoffOffset)
        {
            // 动画帧事件逐个触发 —— 事件名指向 Wwise 里的随机容器（多个单步变体随机取）。
            if (!string.IsNullOrEmpty(footstepEventName)) WwiseAudioBridge.PostEvent(footstepEventName, gameObject);
        }

        /// <summary>
        /// This method is called by an AnimationEvent defined in the landing animation clip.
        /// It plays the landing sound effect.
        /// </summary>
        public void OnLand(AnimationEvent animationEvent)
        {
            if (!string.IsNullOrEmpty(landingEventName)) WwiseAudioBridge.PostEvent(landingEventName, gameObject);
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Reads the current state from the CoreMovement component and updates the Animator parameters accordingly.
        /// </summary>
        private void UpdateLocomotionParameters()
        {
            bool isGrounded = coreMovement.IsGrounded;
            float verticalVelocity = coreMovement.VerticalVelocity;

            // Set booleans for grounded, jumping, and falling states.
            Animator.SetBool(m_AnimIDGrounded, isGrounded);
            Animator.SetBool(m_AnimIDJump, !isGrounded && verticalVelocity > 0.1f);
            Animator.SetBool(m_AnimIDFreeFall, !isGrounded && verticalVelocity <= 0.1f);

            // Set floats for speed and input magnitude to drive blend trees.
            Animator.SetFloat(m_AnimIDSpeed, coreMovement.CurrentSpeed);
            Animator.SetFloat(m_AnimIDMotionSpeed, coreMovement.InputMagnitude);
        }

        /// <summary>
        /// 检测「移动中 → 静止」的跃迁，播放停止移动的收尾音。
        /// 脚步音本身由 walk/run 的动画帧事件逐个触发，这里只负责补最后一步之后的收尾。
        /// </summary>
        private void UpdateFootstepState()
        {
            bool moving = coreMovement.CurrentSpeed > movingSpeedThreshold;
            if (m_WasMoving && !moving && !string.IsNullOrEmpty(moveStopEventName))
            {
                WwiseAudioBridge.PostEvent(moveStopEventName, gameObject);
            }

            m_WasMoving = moving;
        }

        public void TurnInPlaceStart()
        {
            Animator.applyRootMotion = true;
        }

        public void TurnInPlaceEnd()
        {
            Animator.applyRootMotion = false;
        }

        #endregion
    }
}
