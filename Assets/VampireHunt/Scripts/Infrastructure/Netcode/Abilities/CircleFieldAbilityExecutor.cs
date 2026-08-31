using System.Collections.Generic;
using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;
using GameplayEntityId = VampireHunt.SharedKernel.EntityId;

namespace VampireHunt.Infrastructure.Netcode
{
    /// <summary>
    /// 圆型领域（field）的服务器侧执行器：每次收到一次领域 tick 计划，就以玩家为圆心做一次
    /// 全向（360°）球形范围查询，对范围内全部敌人各结算一次伤害并挂载 buff（状态）。
    /// 与喷火器（conal）同族：都走 OverlapSphere + 可信命中（trusted hit），
    /// 但领域不做锥形过滤，且击退方向 = 从圆心指向敌人。
    /// 目标解析逻辑在本类自持（不改动喷火器执行器）。
    /// </summary>
    public sealed class CircleFieldAbilityExecutor : MonoBehaviour, ICombatAbilityNetworkExecutor
    {
        [SerializeField] private uint abilityId = 160;
        [Tooltip("领域视觉预制体（prefab，可选）：每次 tick 生成一个短生命圆形表现。")]
        [SerializeField] private GameObject fieldVfxPrefab;
        [Tooltip("敌人所在层（layer mask）。")]
        [SerializeField] private LayerMask targetMask = 1 << 8;
        [Tooltip("单次 tick 最多结算的目标数，防止极端饱和场景掉帧。")]
        [SerializeField, Min(1)] private int maxTargets = 64;
        [Tooltip("领域视觉预制体的基准直径（米）：实际按 半径×2 / 基准直径 缩放。")]
        [SerializeField, Min(0.01f)] private float vfxBaseDiameter = 1f;
        [Tooltip("领域视觉存活时间（秒）。")]
        [SerializeField, Min(0f)] private float vfxLifetime = 0.6f;
        [Tooltip("Wwise 事件名（领域每跳音），对应《策划版音频调用表》。留空不发声。")]
        [SerializeField] private string tickEventName = "";

        private Collider[] m_HitBuffer = new Collider[64];

        public uint AbilityId => abilityId;

        public bool ExecuteServer(NetworkManager manager, ulong senderClientId, in AbilityCastNetworkMessage message)
        {
            if (manager == null || !manager.IsServer || message.AbilityId != abilityId) return false;

            Vector3 center = message.Origin;
            float radius = Mathf.Max(0.1f, message.TravelDistance);

            if (m_HitBuffer == null || m_HitBuffer.Length < maxTargets)
                m_HitBuffer = new Collider[Mathf.Max(1, maxTargets)];

            int statusCount = Mathf.Min(4, message.OnHitStatuses.Count);
            var statuses = new StatusEffectSpec[statusCount];
            for (int s = 0; s < statusCount; s++)
                statuses[s] = message.OnHitStatuses.Get(s).ToDomain();

            int overlapCount = Physics.OverlapSphereNonAlloc(center, radius, m_HitBuffer, targetMask,
                QueryTriggerInteraction.Collide);
            int resolved = 0;
            var hitSet = new HashSet<MonoBehaviour>();

            for (int i = 0; i < overlapCount && resolved < maxTargets; i++)
            {
                Collider hitCollider = m_HitBuffer[i];
                if (hitCollider == null ||
                    !TryFindTarget(hitCollider, out MonoBehaviour target,
                        out ITrustedCombatHitTarget trustedTarget, out IHittable fallback)) continue;
                if (!hitSet.Add(target)) continue;

                var request = new DamageRequest(
                    new GameplayEntityId(senderClientId + 1UL), GameplayEntityId.None,
                    message.AbilityId, message.Sequence, message.Damage, (DamageTags)message.Tags);

                // 击退方向：从圆心指向敌人（水平），把敌人推离领域中心。
                Vector3 outward = hitCollider.ClosestPoint(center) - center;
                outward.y = 0f;
                if (outward.sqrMagnitude < 0.0001f)
                {
                    outward = hitCollider.transform.position - center;
                    outward.y = 0f;
                }
                if (outward.sqrMagnitude > 0.0001f) outward.Normalize();
                Vector3 force = outward * message.Knockback;

                if (trustedTarget != null)
                {
                    trustedTarget.SubmitTrustedHit(new TrustedCombatHit(request,
                        (ElementId)message.Element, statuses, new Float3(force.x, force.y, force.z)));
                }
                else
                {
                    fallback.OnHit(new HitInfo
                    {
                        amount = message.Damage,
                        hitPoint = hitCollider.ClosestPoint(center),
                        hitNormal = outward.sqrMagnitude > 0.0001f ? outward : Vector3.up,
                        attackerId = senderClientId,
                        impactForce = force
                    });
                }

                resolved++;
            }

            if (resolved > 0 && !string.IsNullOrEmpty(tickEventName))
                WwiseAudioBridge.PostEvent(tickEventName, gameObject);

            if (fieldVfxPrefab != null)
                BindFieldRadius(Instantiate(fieldVfxPrefab, center, Quaternion.identity), radius);

            return true;
        }

        /// <summary>把领域视觉缩放到实际半径：视觉直径 == 领域直径（2 × radius）。</summary>
        private void BindFieldRadius(GameObject field, float radius)
        {
            if (field == null) return;
            float scale = radius * 2f / Mathf.Max(0.01f, vfxBaseDiameter);
            field.transform.localScale = new Vector3(scale, field.transform.localScale.y, scale);
            Destroy(field, Mathf.Max(0.01f, vfxLifetime));
        }

        /// <summary>从碰撞体向上找可信命中目标（ITrustedCombatHitTarget），找不到则退回 IHittable。</summary>
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
