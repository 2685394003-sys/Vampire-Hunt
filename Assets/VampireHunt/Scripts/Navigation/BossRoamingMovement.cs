using System;
using UnityEngine;
using UnityEngine.AI;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Unity.Boss;

namespace VampireHunt.Navigation
{
    /// <summary>Server-only roaming movement. It never decides encounter state or damage.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class BossRoamingMovement : MonoBehaviour
    {
        [SerializeField] private BossEncounterConfigAsset config;
        [SerializeField] private CharacterController characterController;

        private IPlayerTargetQuery m_Targets;
        private float m_VerticalVelocity;
        private float m_CurrentSpeed;
        private float m_ReferenceMaxSpeed;
        private Vector3 m_MoveDirection = Vector3.forward;
        private float m_BattleOrbitSign = 1f;
        private float m_BattleDesiredDistance;
        private float m_BattleOrbitSwitchTimer;
        private float m_BattleDistanceDriftTimer;
        private bool m_BattleInitialized;

        private void Awake()
        {
            if (characterController == null) characterController = GetComponent<CharacterController>();
            ResolveTargetPort();
        }

        public void SetConfig(BossEncounterConfigAsset value) => config = value;

        public bool TrySpawnInPlayerAnnulusServer(uint seed)
        {
            if (config == null) return false;
            ResolveTargetPort();
            if (m_Targets == null || !m_Targets.TryGetNearest(ToFloat3(transform.position), float.MaxValue,
                    out BossPlayerTarget target)) return false;

            var random = new System.Random(unchecked((int)seed));
            for (int attempt = 0; attempt < 24; attempt++)
            {
                double angle = random.NextDouble() * Math.PI * 2d;
                float distance = Mathf.Lerp(config.SpawnMinDistance, config.SpawnMaxDistance,
                    (float)random.NextDouble());
                Vector3 candidate = ToVector3(target.Position) +
                                    new Vector3((float)Math.Cos(angle), 0f, (float)Math.Sin(angle)) * distance;
                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 5f, NavMesh.AllAreas))
                {
                    transform.position = hit.position;
                    ResetSamples();
                    return true;
                }
            }

            return false;
        }

        public void TickServer(bool shouldEvade)
        {
            m_BattleInitialized = false;  // 退出 Battle 移动模式时重置随机状态

            if (config == null || characterController == null || !characterController.enabled)
            {
                ApplyGravityOnly();
                return;
            }

            // 目标速度：默认 0（停止）；evade 且有目标时 = 玩家普通速度
            float targetSpeed = 0f;
            Vector3 moveDirection = m_MoveDirection;
            Vector3 faceDirection = -m_MoveDirection;

            if (shouldEvade)
            {
                ResolveTargetPort();
                if (m_Targets != null && m_Targets.TryGetNearest(ToFloat3(transform.position), config.DetectionRange,
                        out BossPlayerTarget target))
                {
                    Vector3 away = transform.position - ToVector3(target.Position);
                    away.y = 0f;
                    float distanceToPlayer = away.magnitude;
                    if (away.sqrMagnitude < 0.001f) away = -transform.forward;
                    away.Normalize();
                    moveDirection = away;
                    faceDirection = -away;

                    // Boss 速度 = 玩家普通速度 × 距离倍率（玩家越近 Boss 越快，最高 BossMoveProximityMaxMultiplier）
                    float baseSpeed = target.NormalMoveSpeed > 0.001f
                        ? target.NormalMoveSpeed
                        : config.NormalMoveSpeed;
                    float proximityT = Mathf.Clamp01(distanceToPlayer / config.BossMoveProximityRadius);
                    float proximityMultiplier = Mathf.Lerp(config.BossMoveProximityMaxMultiplier, 1f, proximityT);
                    targetSpeed = baseSpeed * proximityMultiplier;
                }
            }

            ApplyMovement(moveDirection, faceDirection, targetSpeed);
        }

        /// <summary>
        /// 漫游期（未进 Boss 战）受击逃离：朝【伤害来源的反方向】跑。
        /// 与 <see cref="TickServer(bool)"/> 的关键区别：<b>不依赖 DetectionRange 去找玩家</b>，
        /// 而是直接用已知的威胁坐标算方向 —— 这样玩家站在 DetectionRange 外远程输出时 Boss 也会躲。
        /// </summary>
        /// <param name="threatPosition">伤害来源坐标（通常是玩家位置）。</param>
        /// <param name="maxFleeDistance">与威胁点的距离上限，超过则停下；0 或负 = 不限。</param>
        public void TickFleeServer(in Float3 threatPosition, float maxFleeDistance)
        {
            m_BattleInitialized = false;

            if (config == null || characterController == null || !characterController.enabled)
            {
                ApplyGravityOnly();
                return;
            }

            Vector3 away = transform.position - ToVector3(threatPosition);
            away.y = 0f;
            float distanceToThreat = away.magnitude;
            if (away.sqrMagnitude < 0.001f) away = -transform.forward;
            away.Normalize();

            // 已经跑够远 → 停下（仍保持朝向，让玩家能追上），避免 Boss 越跑越远导致关卡卡死。
            if (maxFleeDistance > 0f && distanceToThreat >= maxFleeDistance)
            {
                ApplyMovement(away, -away, 0f);
                return;
            }

            // 速度基准：能取到真实玩家速度就用它（Boss 与玩家同源），否则退回配置值。
            float baseSpeed = config.NormalMoveSpeed;
            ResolveTargetPort();
            if (m_Targets != null && m_Targets.TryGetNearest(ToFloat3(transform.position), config.DetectionRange,
                    out BossPlayerTarget target) && target.NormalMoveSpeed > 0.001f)
                baseSpeed = target.NormalMoveSpeed;

            float proximityT = Mathf.Clamp01(distanceToThreat / config.BossMoveProximityRadius);
            float proximityMultiplier = Mathf.Lerp(config.BossMoveProximityMaxMultiplier, 1f, proximityT);

            ApplyMovement(away, -away, baseSpeed * proximityMultiplier);
        }

        public void TickBattleServer(in BossPlayerTarget target)
        {
            if (config == null || characterController == null || !characterController.enabled)
            {
                ApplyGravityOnly();
                return;
            }

            Vector3 outward = transform.position - ToVector3(target.Position);
            outward.y = 0f;
            float distanceToPlayer = outward.magnitude;
            if (outward.sqrMagnitude < 0.001f) outward = transform.forward;
            else outward /= Mathf.Max(0.001f, distanceToPlayer);
            // outward = 从玩家指向 Boss 的单位方向

            if (!m_BattleInitialized)
            {
                m_BattleInitialized = true;
                m_BattleOrbitSign = UnityEngine.Random.value < 0.5f ? 1f : -1f;
                m_BattleDesiredDistance = UnityEngine.Random.Range(config.BattleDesiredDistanceMin, config.BattleDesiredDistanceMax);
                m_BattleOrbitSwitchTimer = config.BattleOrbitSwitchInterval;
                m_BattleDistanceDriftTimer = config.BattleDistanceDriftInterval;
            }

            // 随机切换顺/逆时针
            m_BattleOrbitSwitchTimer -= Time.deltaTime;
            if (m_BattleOrbitSwitchTimer <= 0f)
            {
                m_BattleOrbitSwitchTimer = config.BattleOrbitSwitchInterval;
                m_BattleOrbitSign = UnityEngine.Random.value < 0.5f ? 1f : -1f;
            }

            // 期望距离随机漂移
            m_BattleDistanceDriftTimer -= Time.deltaTime;
            if (m_BattleDistanceDriftTimer <= 0f)
            {
                m_BattleDistanceDriftTimer = config.BattleDistanceDriftInterval;
                m_BattleDesiredDistance = UnityEngine.Random.Range(config.BattleDesiredDistanceMin, config.BattleDesiredDistanceMax);
            }

            // 移动方向 = 切线（绕圈）+ 径向（拉回期望距离，距离抖动）
            Vector3 tangent = Vector3.Cross(Vector3.up, outward) * m_BattleOrbitSign;
            float distanceError = distanceToPlayer - m_BattleDesiredDistance;
            float radial = Mathf.Clamp(distanceError / config.BattleDistanceSpring, -1f, 1f);
            Vector3 moveDirection = (tangent - outward * radial).normalized;

            float baseSpeed = target.NormalMoveSpeed > 0.001f
                ? target.NormalMoveSpeed
                : config.NormalMoveSpeed;

            ApplyMovement(moveDirection, -outward, baseSpeed);
        }

        private void ApplyMovement(Vector3 moveDirection, Vector3 faceDirection, float targetSpeed)
        {
            // 惯性：起步加速用 AccelerationSeconds（灵敏），停下减速用 DecelerationSeconds（滑行更远）
            if (targetSpeed > m_ReferenceMaxSpeed) m_ReferenceMaxSpeed = targetSpeed;
            if (m_ReferenceMaxSpeed < 0.001f) m_ReferenceMaxSpeed = Mathf.Max(config.NormalMoveSpeed, 0.1f);
            float rampSeconds = targetSpeed >= m_CurrentSpeed
                ? config.AccelerationSeconds
                : config.DecelerationSeconds;
            float speedDelta = m_ReferenceMaxSpeed / rampSeconds;
            m_CurrentSpeed = Mathf.MoveTowards(m_CurrentSpeed, targetSpeed, speedDelta * Time.deltaTime);
            if (m_CurrentSpeed < 0.0001f && targetSpeed <= 0f)
            {
                m_CurrentSpeed = 0f;
                m_ReferenceMaxSpeed = 0f;
            }

            m_MoveDirection = moveDirection;

            Vector3 movement = moveDirection * m_CurrentSpeed;
            if (characterController.isGrounded && m_VerticalVelocity < 0f) m_VerticalVelocity = -2f;
            else m_VerticalVelocity += Physics.gravity.y * Time.deltaTime;
            movement.y = m_VerticalVelocity;
            characterController.Move(movement * Time.deltaTime);

            if (faceDirection.sqrMagnitude > 0.001f)
            {
                Quaternion facing = Quaternion.LookRotation(faceDirection, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, facing, 540f * Time.deltaTime);
            }
        }

        public bool TryTeleportAwayServer(uint seed) => TrySpawnInPlayerAnnulusServer(seed);

        private void ApplyGravityOnly()
        {
            if (characterController == null || !characterController.enabled) return;
            if (characterController.isGrounded && m_VerticalVelocity < 0f) m_VerticalVelocity = -2f;
            else m_VerticalVelocity += Physics.gravity.y * Time.deltaTime;
            characterController.Move(Vector3.up * (m_VerticalVelocity * Time.deltaTime));
        }

        private void ResetSamples()
        {
            m_VerticalVelocity = 0f;
            m_CurrentSpeed = 0f;
            m_ReferenceMaxSpeed = 0f;
        }

        private void ResolveTargetPort()
        {
            if (m_Targets != null) return;
            MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
                if (behaviours[i] is IPlayerTargetQuery query) { m_Targets = query; break; }
        }

        private static Float3 ToFloat3(Vector3 value) => new Float3(value.x, value.y, value.z);
        private static Vector3 ToVector3(in Float3 value) => new Vector3(value.X, value.Y, value.Z);
    }
}
