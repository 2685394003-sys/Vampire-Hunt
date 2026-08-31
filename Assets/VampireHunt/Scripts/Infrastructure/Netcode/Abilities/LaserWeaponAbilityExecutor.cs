using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;

namespace VampireHunt.Infrastructure.Netcode
{
    /// <summary>
    /// Server-side executor for the continuous laser (Boss-laser style). Holds a single persistent
    /// <see cref="LaserBeam"/> while the player holds the attack, steering it each frame; releases it
    /// (and stops the sound) when the hold ends.
    /// </summary>
    public sealed class LaserWeaponAbilityExecutor : MonoBehaviour, ICombatAbilityNetworkExecutor
    {
        [SerializeField] private uint abilityId = 140;
        [SerializeField] private NetworkObject beamPrefab;
        [Tooltip("Release detection: if no cast arrives for this long (seconds), the beam is released.")]
        [SerializeField] private float releaseTimeout = 0.15f;

        [Header("转向限制")]
        [Tooltip("激光朝向与人物面向的最大允许夹角（度）。超过则关闭激光。")]
        [Range(0f, 180f)] [SerializeField] private float maxTurnAngle = 90f;
        [Tooltip("超角关闭后，需等待多久（秒）才能再次释放激光。")]
        [Min(0f)] [SerializeField] private float angleBreakCooldown = 1f;

        [Header("Audio (Wwise)")]
        [Tooltip("Wwise 事件名（按下开始的持续音，建议 loop），对应《策划版音频调用表》。留空不发声。")]
        [SerializeField] private string fireEventName = "";
        [Tooltip("Wwise 事件名（松开停止），对应《策划版音频调用表》。留空不发声。")]
        [SerializeField] private string stopEventName = "";

        private NetworkObject m_ActiveBeam;
        private LaserBeam m_ActiveBeamComponent;
        private float m_LastCastTime;
        private float m_CooldownUntil;

        public uint AbilityId => abilityId;

        public bool ExecuteServer(NetworkManager manager, ulong senderClientId, in AbilityCastNetworkMessage message)
        {
            if (manager == null || !manager.IsServer || beamPrefab == null ||
                message.AbilityId != abilityId) return false;

            // 超角关闭后的冷却期内禁止释放
            if (Time.time < m_CooldownUntil) return false;

            if (m_ActiveBeam == null)
            {
                Vector3 direction = message.Direction.sqrMagnitude > 0.0001f
                    ? message.Direction.normalized
                    : transform.forward;

                // 初始朝向 = 玩家面朝方向（水平），让激光从玩家前方开始，再按角速度平滑转向鼠标
                Vector3 initialDirection = transform.forward;
                initialDirection.y = 0f;
                if (initialDirection.sqrMagnitude < 0.0001f) initialDirection = direction;
                initialDirection = initialDirection.normalized;

                m_ActiveBeam = Instantiate(beamPrefab, message.Origin, Quaternion.LookRotation(initialDirection, Vector3.up));
                m_ActiveBeamComponent = m_ActiveBeam.GetComponent<LaserBeam>();
                m_ActiveBeamComponent?.ConfigureServer(senderClientId, message);
                m_ActiveBeam.SpawnWithOwnership(senderClientId);

                if (!string.IsNullOrEmpty(fireEventName)) WwiseAudioBridge.PostEvent(fireEventName, gameObject);
            }
            else if (m_ActiveBeamComponent != null && m_ActiveBeamComponent.IsReleasing)
            {
                // 缩小过程中重新按住：取消缩小，重新蓄力
                m_ActiveBeamComponent.CancelRelease();
            }

            m_ActiveBeamComponent?.SetBeam(message.Origin, message.Direction);
            m_LastCastTime = Time.time;
            return true;
        }

        private void Update()
        {
            if (m_ActiveBeam == null) return;

            // 超角检测：激光朝向与人物面向夹角过大 → 关闭 + 冷却
            Vector3 playerForward = transform.forward;
            playerForward.y = 0f;
            if (playerForward.sqrMagnitude >= 0.0001f &&
                m_ActiveBeamComponent != null && !m_ActiveBeamComponent.IsReleasing &&
                !m_ActiveBeamComponent.IsReleaseComplete)
            {
                playerForward = playerForward.normalized;
                float angle = Vector3.Angle(playerForward, m_ActiveBeamComponent.CurrentDirection);
                if (angle > maxTurnAngle)
                {
                    m_ActiveBeamComponent.BeginRelease();
                    if (!string.IsNullOrEmpty(stopEventName)) WwiseAudioBridge.PostEvent(stopEventName, gameObject);
                    m_CooldownUntil = Time.time + angleBreakCooldown;
                }
            }

            // 松手检测：开始缩小 + 停止声音
            if (Time.time - m_LastCastTime > releaseTimeout)
            {
                if (m_ActiveBeamComponent != null && !m_ActiveBeamComponent.IsReleasing &&
                    !m_ActiveBeamComponent.IsReleaseComplete)
                {
                    m_ActiveBeamComponent.BeginRelease();
                    if (!string.IsNullOrEmpty(stopEventName)) WwiseAudioBridge.PostEvent(stopEventName, gameObject);
                }
            }

            // 缩小完成后销毁
            if (m_ActiveBeamComponent != null && m_ActiveBeamComponent.IsReleaseComplete)
            {
                if (m_ActiveBeam != null && m_ActiveBeam.IsSpawned) m_ActiveBeam.Despawn();
                m_ActiveBeam = null;
                m_ActiveBeamComponent = null;
            }
        }
    }
}
