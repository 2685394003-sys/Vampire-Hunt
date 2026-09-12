using VampireHunt.Infrastructure.Integration;
using System.Collections.Generic;
using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Unity;
using VampireHunt.Infrastructure.Netcode.Abilities.Familiar;
using VampireHunt.Player.Abilities.Familiar;
using GameplayEntityId = VampireHunt.SharedKernel.EntityId;

namespace VampireHunt.Infrastructure.Netcode.Abilities.Familiar
{
    public sealed partial class ImpactFamiliarController
    {
        // ── 候选目标：玩家命中回调 ────────────────────────────

        public int Priority => 0;

        /// <summary>
        /// 玩家（作为伤害来源）命中敌人时由 <c>ServerCombatResolutionHost</c> 回调。
        /// 把被命中的敌人登记为每只使魔的候选目标；新命中的敌人<b>覆盖</b>旧候选。
        /// 若某只使魔正在冲刺/撞击，则只写入它的缓存、<b>不打断</b>本次撞击。
        /// </summary>
        public void OnCombatResolved(in CombatResolutionRecord record, CombatParticipantRole role)
        {
            if (!m_Active) return;
            if ((role & CombatParticipantRole.Source) == 0) return;
            if ((record.Tags & m_Definition.WeaponTag) != 0) return;   // 使魔自己的伤害，不登记
            if (record.Target.IsNone) return;

            // 不在索敌范围内的命中不登记（用命中点近似目标位置：优先查缓存）。
            if (!IsWithinAcquireRange(record.Target)) return;

            // 被动索敌的优先级低于左键指令：玩家指过目标之后，武器误伤到别的怪不会把使魔带走。
            for (int i = 0; i < m_Brains.Count; i++)
                m_Brains[i].SetPendingTarget(record.Target, m_Definition.PendingTargetLifetime,
                    ImpactFamiliarBrain.PriorityPassive);

            if (logToConsole)
            {
                Debug.Log($"[ImpactFamiliar] 登记候选目标 entity={record.Target.Value} " +
                          $"（{m_Brains.Count} 只使魔各自缓存，冲刺中的不会被打断）", this);
            }
        }

        private bool IsWithinAcquireRange(GameplayEntityId targetId)
        {
            return m_TargetQuery.IsWithinAcquireRange(targetId, transform.position, m_Definition.AcquireRange);
        }

        // ── 每帧推进 ──────────────────────────────────────────

        private void TickAll(float deltaTime)
        {
            Vector3 ownerPosition = transform.position;

            m_ScanTimer += deltaTime;
            if (m_ScanTimer >= EnemyScanInterval)
            {
                m_ScanTimer = 0f;
                RefreshEnemyCache(ownerPosition);
            }

            // ① 先算这一帧的环绕中心：血契开启且指针有效时跟鼠标，否则跟玩家（行为与改动前一致）。
            Vector3 orbitCenter = ComputeOrbitCenter(ownerPosition, deltaTime);

            // ② 按住左键 → 把指针附近的敌人按距离指派给各只使魔（优先级高于被动索敌）。
            IssuePointerCommands();

            for (int i = 0; i < m_Brains.Count; i++)
            {
                ImpactFamiliarBrain brain = m_Brains[i];

                // 空闲且手上有有效候选 → 解析出目标对象后立刻起飞。
                // 冲刺/撞击中（IsEngaged）的使魔不会走这里，因此不会被打断。
                if (brain.WantsEngage && TryResolveTarget(brain.PendingTargetId, ownerPosition, out MonoBehaviour target))
                {
                    brain.BeginDash(target, brain.PendingTargetId);
                    ServerCombatActivity.Action(networkObject != null ? networkObject.NetworkManager : null, m_OwnerEntityId, m_Definition.AbilityId, ++m_Sequence);
                    if (logToConsole)
                        Debug.Log($"[ImpactFamiliar] #{i} 起飞 → entity={brain.PendingTargetId.Value}", this);
                }

                // 注意：环绕中心传的是 orbitCenter（待机/返回时围绕的点），
                // 但索敌范围仍以玩家位置为准 —— 使魔能追多远，始终由玩家决定，不会被鼠标拖出战场。
                brain.Tick(deltaTime, orbitCenter);
                ResolveHits(brain);
            }

            SyncVisuals();
        }

        // ── 指针跟随与指令索敌 ────────────────────────────────

        /// <summary>
        /// 计算这一帧的环绕中心（orbit center）：血契开启且指针有效时跟随鼠标，否则回到玩家身上。<br/>
        /// 无论往哪个方向切都是<b>平滑插值</b>过去的（<c>OrbitCenterFollowLerp</c>），不会瞬移。
        /// </summary>
        private Vector3 ComputeOrbitCenter(Vector3 ownerPosition, float deltaTime)
        {
            Vector3 target = ownerPosition;

            if (pointerOrbit && pointerCommand != null && pointerCommand.TryGetPointer(out Vector3 pointer))
            {
                target = pointer;
                // 距离上限：鼠标拖太远时把队列夹在玩家周围这个半径上（0 = 不限制）。
                if (maxOrbitCenterDistance > 0f)
                {
                    Vector3 offset = target - ownerPosition;
                    offset.y = 0f;
                    float max = maxOrbitCenterDistance;
                    if (offset.sqrMagnitude > max * max)
                        target = ownerPosition + offset.normalized * max;
                }
            }

            if (!m_OrbitCenterInitialized)
            {
                m_OrbitCenter = target;
                m_OrbitCenterInitialized = true;
                return target;
            }

            float t = 1f - Mathf.Exp(-orbitCenterFollowLerp * deltaTime);
            m_OrbitCenter = Vector3.Lerp(m_OrbitCenter, target, t);
            return m_OrbitCenter;
        }

        /// <summary>
        /// 左键指令索敌（pointer command）：按住左键时，以指针为圆心、<c>CommandRadius</c> 为半径
        /// 找出范围内的敌人，<b>按到指针的距离从近到远排序</b>，第 N 只使魔领第 N 近的目标。<br/>
        /// 敌人比使魔少时多只一起集火最近的；使魔比敌人多时多余的也集火最近的（不会闲着）。
        /// </summary>
        /// <remarks>
        /// 写进的是<b>候选缓存</b>而不是强制起飞：正在冲刺/掉头的使魔只记下目标、不打断当前这一程，
        /// 等这一程走完自然转向新目标 —— 与「不可打断」的既有规则保持一致。
        /// </remarks>
        private void IssuePointerCommands()
        {
            if (!pointerOrbit || pointerCommand == null) return;
            if (!pointerCommand.IsCommandHeld) return;
            if (!pointerCommand.TryGetPointer(out Vector3 origin)) return;

            if (!m_TargetQuery.QueryCommandTargets(
                    origin, commandRadius, m_Definition.MaxTargets, m_Definition.TargetMask)) return;

            for (int i = 0; i < m_Brains.Count; i++)
            {
                // 敌人比使魔少 → 多余的使魔集火最近的那只（取最后一个下标，不会越界）。
                int pick = i < m_TargetQuery.CommandTargetCount ? i : m_TargetQuery.CommandTargetCount - 1;
                GameplayEntityId targetId = m_TargetQuery.GetCommandTargetId(pick);
                if (targetId.IsNone) continue;

                m_Brains[i].SetPendingTarget(targetId, commandTargetLifetime,
                    ImpactFamiliarBrain.PriorityCommand);
            }

            if (logToConsole)
            {
                Debug.Log($"[ImpactFamiliar] 左键指令 → 指针 {origin} 半径 {commandRadius}m 内 " +
                          $"{m_TargetQuery.CommandTargetCount} 个敌人，指派给 {m_Brains.Count} 只使魔", this);
            }
        }

        private void RefreshEnemyCache(Vector3 ownerPosition)
        {
            m_TargetQuery.RefreshCache(
                ownerPosition, m_Definition.AcquireRange, m_Definition.MaxTargets, m_Definition.TargetMask);
        }

        private bool TryResolveTarget(GameplayEntityId targetId, Vector3 ownerPosition, out MonoBehaviour target)
        {
            return m_TargetQuery.TryResolveTarget(
                targetId, ownerPosition, m_Definition.AcquireRange, out target);
        }

        private void ResolveHits(ImpactFamiliarBrain brain)
        {
            if (!brain.IsHitWindowOpen) return;
            if (m_HitBuffer == null || m_HitBuffer.Length < m_Definition.MaxTargets)
                m_HitBuffer = new Collider[Mathf.Max(1, m_Definition.MaxTargets)];

            int count = Physics.OverlapSphereNonAlloc(brain.Position, m_Definition.ImpactRadius, m_HitBuffer,
                m_Definition.TargetMask, QueryTriggerInteraction.Collide);
            if (count <= 0) return;

            float damage = ReadAttribute(StatKeys.Damage) * m_Definition.BaseDamageInheritRatio *
                           m_Definition.DamageMultiplier;
            if (damage <= 0f) return;

            float knockback = ReadAttribute(StatKeys.KnockbackForce) * m_Definition.KnockbackMultiplier;
            Vector3 force = brain.Facing * knockback;
            int resolved = 0;

            for (int i = 0; i < count && resolved < m_Definition.MaxTargets; i++)
            {
                Collider hitCollider = m_HitBuffer[i];
                if (hitCollider == null) continue;
                if (!FamiliarTargetQuery.TryFindTarget(
                        hitCollider, out _, out ITrustedCombatHitTarget trusted, out IHittable fallback))
                    continue;

                GameplayEntityId targetId = FamiliarTargetQuery.ResolveEntityId(hitCollider.transform);
                // 无穿透上限、无「每敌一次」去重：路径上扫到的敌人一律结算，
                // 唯一节流是同一敌人的重复命中间隔 HitCooldownPerTarget。
                if (!brain.TryConsumeHit(targetId)) continue;

                var request = new DamageRequest(m_OwnerEntityId, targetId, m_Definition.AbilityId,
                    m_Sequence++, damage, m_Definition.WeaponTag);
                var hit = new TrustedCombatHit(request, m_Definition.Element, m_Statuses,
                    new Float3(force.x, force.y, force.z));

                if (trusted != null)
                {
                    trusted.SubmitTrustedHit(hit);
                }
                else if (fallback != null)
                {
                    fallback.OnHit(new HitInfo
                    {
                        amount = damage,
                        hitPoint = hitCollider.ClosestPoint(brain.Position),
                        hitNormal = brain.Facing.sqrMagnitude > 0.0001f ? brain.Facing : Vector3.up,
                        attackerId = m_OwnerEntityId.IsNone ? 0UL : m_OwnerEntityId.Value - 1UL,
                        impactForce = force
                    });
                }
                resolved++;
            }
        }
    }
}
