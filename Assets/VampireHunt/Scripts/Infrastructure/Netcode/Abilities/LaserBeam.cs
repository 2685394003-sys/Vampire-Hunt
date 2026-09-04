using System.Collections.Generic;
using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;
using GameplayEntityId = VampireHunt.SharedKernel.EntityId;

namespace VampireHunt.Infrastructure.Netcode
{
    /// <summary>
    /// Continuous laser beam (Boss-laser style): a single stretched-cube beam that persists while the
    /// player holds the attack. It charges up (grows from 0 to full range/width) on spawn, smoothly
    /// rotates toward the aim at a bounded turn speed instead of snapping, deals tick damage to every
    /// enemy along the ray, then shrinks out on release. The owning executor despawns it once the
    /// shrink completes.
    /// </summary>
    public sealed class LaserBeam : NetworkBehaviour
    {
        [SerializeField] private Transform beamVisual;
        [Tooltip("Seconds between damage ticks.")]
        [Min(0.01f)] [SerializeField] private float tickInterval = 0.1f;
        [SerializeField] private LayerMask targetMask = 1 << 8;
        [Tooltip("Wwise 事件名（命中音），对应《策划版音频调用表》。留空不发声。")]
        [SerializeField] private string hitEventName = "";

        [Header("穿透消耗")]
        [Tooltip("全局共享配置，与剑气/自动步枪/狙击的弹丸（SwordWaveEffect）共用同一份，改一处即可同时影响五把武器。留空则回退到内置默认值 1 / 30 / 30。")]
        [SerializeField] private PierceCostConfig pierceCost;

        [Header("Visual Dynamics")]
        [Tooltip("蓄力时长（秒）：激光从 0 生长到满射程+满宽的时长。0 = 立即满。")]
        [Min(0f)] [SerializeField] private float chargeDuration = 0.15f;
        [Tooltip("结束缩小时长（秒）：松开后从满宽缩小到消失的时长。")]
        [Min(0.01f)] [SerializeField] private float releaseDuration = 0.2f;
        [Tooltip("旋转角速度（度/秒）：激光朝向鼠标平滑转动的最大角速度。")]
        [Min(0f)] [SerializeField] private float turnSpeed = 240f;

        private readonly NetworkVariable<ulong> m_AttackerClientId = new NetworkVariable<ulong>(
            0UL, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<AbilityCastNetworkMessage> m_Cast =
            new NetworkVariable<AbilityCastNetworkMessage>(default,
                NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<Vector3> m_Origin =
            new NetworkVariable<Vector3>(default,
                NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<Vector3> m_Direction =
            new NetworkVariable<Vector3>(Vector3.forward,
                NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<bool> m_Releasing =
            new NetworkVariable<bool>(false,
                NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly HashSet<MonoBehaviour> m_HitTargets = new HashSet<MonoBehaviour>();

        // 光束被阻挡的位置（沿光束方向的投影距离）；-1 = 无阻挡（显示满射程）。每 tick 由 TickDamage 更新。
        private float m_VisualBlockDistance = -1f;

        private ulong m_PendingAttackerClientId;
        private AbilityCastNetworkMessage m_PendingCast;
        private bool m_HasPendingConfiguration;
        private float m_NextTickTime;

        private Vector3 m_CurrentDirection = Vector3.forward;
        private float m_WidthScale;
        private bool m_ReleaseComplete;

        // 穿透消耗：优先读全局共享配置；漏挂资产时回退内置默认值，避免 Boss 阻挡静默失效。
        private int NormalCost => pierceCost != null ? pierceCost.NormalPierceCost : 1;
        private int BossCost => pierceCost != null ? pierceCost.BossPierceCost : 30;
        private int BossHandCost => pierceCost != null ? pierceCost.BossHandPierceCost : 30;

        /// <summary>Beam width follows the cast's ProjectileSize (the ability asset's Width).</summary>
        private float BeamWidth => m_Cast.Value.ProjectileSize > 0f ? m_Cast.Value.ProjectileSize : 0.5f;

        /// <summary>True once the release shrink has finished (the executor despawns on this).</summary>
        public bool IsReleaseComplete => m_ReleaseComplete;

        /// <summary>True while the beam is shrinking out (release in progress).</summary>
        public bool IsReleasing => m_Releasing.Value;

        /// <summary>Current (smoothed, horizontal) facing direction of the beam.</summary>
        public Vector3 CurrentDirection => m_CurrentDirection;

        public void ConfigureServer(ulong attackerClientId, in AbilityCastNetworkMessage message)
        {
            m_PendingAttackerClientId = attackerClientId;
            m_PendingCast = message;
            m_HasPendingConfiguration = true;
        }

        /// <summary>Called by the owner executor every frame while held, to steer the beam toward the aim.</summary>
        public void SetBeam(Vector3 origin, Vector3 direction)
        {
            if (!IsServer) return;
            m_Origin.Value = origin;
            Vector3 d = direction;
            d.y = 0f;
            m_Direction.Value = d.sqrMagnitude > 0.0001f ? d.normalized : Vector3.forward;
        }

        /// <summary>Called by the owner executor on release: shrink the beam out, then report completion.</summary>
        public void BeginRelease()
        {
            if (!IsServer || m_Releasing.Value) return;
            m_Releasing.Value = true;
        }

        /// <summary>Called by the owner executor if the player re-presses before the shrink finishes.</summary>
        public void CancelRelease()
        {
            if (!IsServer || !m_Releasing.Value) return;
            m_Releasing.Value = false;
            m_ReleaseComplete = false;
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer && m_HasPendingConfiguration)
            {
                m_AttackerClientId.Value = m_PendingAttackerClientId;
                m_Cast.Value = m_PendingCast;
                m_HasPendingConfiguration = false;
            }

            m_CurrentDirection = transform.forward;
            m_CurrentDirection.y = 0f;
            if (m_CurrentDirection.sqrMagnitude < 0.0001f) m_CurrentDirection = Vector3.forward;
            m_CurrentDirection = m_CurrentDirection.normalized;

            m_WidthScale = 0f;
            m_NextTickTime = Time.time;
            UpdateVisual();
        }

        private void Update()
        {
            if (!NetworkObject.IsSpawned) return;

            UpdateDynamics();
            UpdateVisual();

            if (IsServer && Time.time >= m_NextTickTime && m_WidthScale > 0.01f)
            {
                m_NextTickTime = Time.time + tickInterval;
                TickDamage();
            }
        }

        private void UpdateDynamics()
        {
            // 蓄力放大 / 结束缩小
            if (!m_Releasing.Value)
            {
                if (chargeDuration <= 0f) m_WidthScale = 1f;
                else m_WidthScale = Mathf.MoveTowards(m_WidthScale, 1f, Time.deltaTime / chargeDuration);
            }
            else
            {
                if (releaseDuration <= 0f)
                {
                    m_WidthScale = 0f;
                    m_ReleaseComplete = true;
                }
                else
                {
                    m_WidthScale = Mathf.MoveTowards(m_WidthScale, 0f, Time.deltaTime / releaseDuration);
                    if (m_WidthScale <= 0.0001f)
                    {
                        m_WidthScale = 0f;
                        m_ReleaseComplete = true;
                    }
                }
            }

            // 平滑旋转向目标方向（限角速度，不瞬移）
            Vector3 target = m_Direction.Value;
            target.y = 0f;
            if (target.sqrMagnitude < 0.0001f) target = m_CurrentDirection;
            target = target.normalized;

            if (turnSpeed <= 0f)
            {
                m_CurrentDirection = target;
            }
            else
            {
                float maxRadians = turnSpeed * Mathf.Deg2Rad * Time.deltaTime;
                m_CurrentDirection = Vector3.RotateTowards(m_CurrentDirection, target, maxRadians, 0f);
                if (m_CurrentDirection.sqrMagnitude < 0.0001f) m_CurrentDirection = target;
            }
        }

        private void UpdateVisual()
        {
            Vector3 origin = m_Origin.Value;
            Vector3 direction = m_CurrentDirection;
            float range = Mathf.Max(0.1f, m_Cast.Value.TravelDistance);
            float width = BeamWidth * m_WidthScale;
            // 特效与判定同步：穿透用尽 / Boss 阻挡时，光束只显示到最后一个可命中单位处，不再贯穿到满射程
            float blockedLength = m_VisualBlockDistance >= 0f ? m_VisualBlockDistance : range;
            float visualLength = Mathf.Clamp(blockedLength, 0f, range) * m_WidthScale;

            Transform target = beamVisual != null ? beamVisual : transform;
            target.position = origin + direction * (visualLength * 0.5f);
            target.rotation = Quaternion.LookRotation(direction, Vector3.up);
            target.localScale = new Vector3(width, width, visualLength);
        }

        private void TickDamage()
        {
            // 防御：cast 尚未配置成功（如 OnNetworkSpawn 未完成 / 无 NetworkManager 裸场景）时不判定，
            // 避免 Damage/PierceCount 读到 0 造成"激光停在第一个目标"的假象。
            if (m_Cast.Value.AbilityId == 0) return;

            Vector3 origin = m_Origin.Value;
            Vector3 direction = m_CurrentDirection;
            float range = Mathf.Max(0.1f, m_Cast.Value.TravelDistance * m_WidthScale);
            float radius = Mathf.Max(0.01f, BeamWidth * m_WidthScale * 0.5f);

            RaycastHit[] hits = Physics.SphereCastAll(origin, radius, direction, range, targetMask,
                QueryTriggerInteraction.Collide);

            // SphereCastAll 不保证顺序：沿光束方向从近到远排序，才能正确表达"穿透"。
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            // 穿透规则（按距离从近到远）：
            // 每个目标按类型消耗穿透数 —— 普通敌人 / Boss 本体 / Boss 手，三者数值统一取自全局共享配置 PierceCostConfig。
            // 穿透数用尽即停：Boss 的消耗远高于小怪，因此穿透力低于该值的武器（如步枪 5、激光 10）会被 Boss 挡住；
            // 穿透力高于该值的武器（如剑气 100、狙击 999）仍会打穿 Boss 继续命中后方目标 —— 调 Boss Pierce Cost 即可控制这条线。
            // 视觉规则：穿透未耗尽时（还能继续穿）光束保持满射程；只在穿透停止点（耗尽）截断。
            int pierceLimit = Mathf.Max(1, m_Cast.Value.PierceCount);
            int pierceSpent = 0;
            m_VisualBlockDistance = -1f; // 复位：无停止点则光束显示满射程
            m_HitTargets.Clear();
            for (int i = 0; i < hits.Length; i++)
            {
                RaycastHit hit = hits[i];
                if (hit.collider == null ||
                    !TryFindTarget(hit.collider, out MonoBehaviour target,
                        out ITrustedCombatHitTarget trustedTarget, out IHittable fallback)) continue;
                // 同一目标命中多个碰撞体时，只按最近一次处理
                if (!m_HitTargets.Add(target)) continue;

                SubmitDamage(trustedTarget, fallback, hit, m_Cast.Value.Damage);
                if (!string.IsNullOrEmpty(hitEventName)) WwiseAudioBridge.PostEvent(hitEventName, gameObject);

                pierceSpent += GetPierceCost(target);
                if (pierceSpent >= pierceLimit)
                {
                    // 穿透停止点：光束视觉在此截断（穿透耗尽 / Boss 阻挡），停止点 = 当前单位
                    m_VisualBlockDistance = Mathf.Max(0f, Vector3.Dot(hit.point - origin, direction));
                    break;
                }
            }
            // 循环正常结束（穿透未耗尽）→ m_VisualBlockDistance 保持 -1 → 视觉满射程
        }

        /// <summary>
        /// 返回目标对穿透的消耗值：Boss 本体 → BossCost，Boss 手 → BossHandCost，其余（普通敌人）→ NormalCost。
        /// 三者统一取自全局共享配置 PierceCostConfig，与弹丸武器（SwordWaveEffect）保持同一口径。
        /// </summary>
        private int GetPierceCost(MonoBehaviour target)
        {
            if (target.GetComponentInParent<BossVitalsReceiver>() != null) return BossCost;
            if (target.GetComponentInParent<BossHandNetworkActor>() != null) return BossHandCost;
            return NormalCost;
        }

        private void SubmitDamage(ITrustedCombatHitTarget trustedTarget, IHittable fallback, RaycastHit hit, float damage)
        {
            AbilityCastNetworkMessage cast = m_Cast.Value;
            var request = new DamageRequest(
                new GameplayEntityId(m_AttackerClientId.Value + 1UL), GameplayEntityId.None,
                cast.AbilityId, cast.Sequence, damage, (DamageTags)cast.Tags);

            int statusCount = Mathf.Min(4, cast.OnHitStatuses.Count);
            var statuses = new StatusEffectSpec[statusCount];
            for (int i = 0; i < statusCount; i++)
                statuses[i] = cast.OnHitStatuses.Get(i).ToDomain();

            Vector3 force = m_CurrentDirection * cast.Knockback;

            if (trustedTarget != null)
            {
                trustedTarget.SubmitTrustedHit(new TrustedCombatHit(request,
                    (ElementId)cast.Element, statuses, new Float3(force.x, force.y, force.z)));
            }
            else
            {
                fallback.OnHit(new HitInfo
                {
                    amount = damage,
                    hitPoint = hit.point,
                    hitNormal = -m_CurrentDirection,
                    attackerId = m_AttackerClientId.Value,
                    impactForce = force
                });
            }
        }

        private static bool TryFindTarget(Collider collider, out MonoBehaviour target,
            out ITrustedCombatHitTarget trustedTarget, out IHittable fallback)
        {
            MonoBehaviour[] behaviours = collider.GetComponentsInParent<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is ITrustedCombatHitTarget trusted)
                {
                    target = behaviours[i];
                    trustedTarget = trusted;
                    fallback = null;
                    return true;
                }
            }
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IHittable hittable)
                {
                    target = behaviours[i];
                    trustedTarget = null;
                    fallback = hittable;
                    return true;
                }
            }
            target = null;
            trustedTarget = null;
            fallback = null;
            return false;
        }
    }
}
