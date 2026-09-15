using VampireHunt.Infrastructure.Integration;
using System.Collections.Generic;
using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Netcode.Abilities.Familiar;
using VampireHunt.Infrastructure.Unity;
using VampireHunt.Player.Abilities.Familiar;
using GameplayEntityId = VampireHunt.SharedKernel.EntityId;

namespace VampireHunt.Infrastructure.Netcode.Abilities.Familiar
{
    public sealed partial class GunnerFamiliarController
    {
        // ── 候选目标：玩家命中回调 ────────────────────────────

        public int Priority => 0;

        /// <summary>
        /// 玩家（作为伤害来源）命中敌人时由 <c>ServerCombatResolutionHost</c> 回调。
        /// 把被命中的敌人登记为每只使魔的候选目标；新命中的敌人<b>覆盖</b>旧候选。
        /// 正在射击流程中的使魔只写缓存、<b>不打断</b>当前这一套。
        /// </summary>
        public void OnCombatResolved(in CombatResolutionRecord record, CombatParticipantRole role)
        {
            if (!m_Active) return;
            if ((role & CombatParticipantRole.Source) == 0) return;
            if ((record.Tags & m_Definition.WeaponTag) != 0) return;   // 使魔自己的伤害，不登记
            if (record.Target.IsNone) return;
            if (!IsWithinAcquireRange(record.Target)) return;

            // 被动索敌的优先级低于左键指令：玩家指过目标之后，武器误伤到别的怪不会把使魔带走。
            for (int i = 0; i < m_Brains.Count; i++)
                m_Brains[i].SetPendingTarget(record.Target, m_Definition.PendingTargetLifetime,
                    GunnerFamiliarBrain.PriorityPassive);

            if (logToConsole)
            {
                Debug.Log($"[GunnerFamiliar] 登记候选目标 entity={record.Target.Value} " +
                          $"（{m_Brains.Count} 只使魔各自缓存，射击流程中的不会被打断）", this);
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
                GunnerFamiliarBrain brain = m_Brains[i];

                // 空闲且手上有有效候选 → 解析出目标对象后立刻起飞。
                // 交战中（接近/瞄准/开火/冷却）的使魔不会走这里，因此不会被打断。
                if (brain.WantsEngage && TryResolveTarget(brain.PendingTargetId, ownerPosition, out MonoBehaviour target))
                {
                    brain.BeginEngagement(target, brain.PendingTargetId);
                    if (logToConsole)
                        Debug.Log($"[GunnerFamiliar] #{i} 起飞 → entity={brain.PendingTargetId.Value}", this);
                }

                // 注意：环绕中心传的是 orbitCenter（待机/返回时围绕的点），
                // 但索敌范围仍以玩家位置为准 —— 使魔能追多远，始终由玩家决定，不会被鼠标拖出战场。
                brain.Tick(deltaTime, orbitCenter);

                // 开火请求：只有 Fire 状态才会产生，且每发只取一次。
                if (brain.HasPendingShot && brain.TryConsumeShot(out Vector3 origin, out Vector3 direction))
                    ResolveShot(brain, origin, direction);
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
        /// 写进的是<b>候选缓存</b>而不是强制起飞：正在打一套（接近/瞄准/开火/冷却）的使魔只记下目标，
        /// 等这套打完在决策点自然转向 —— 与「射击流程不可打断」的既有规则保持一致。
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
                    GunnerFamiliarBrain.PriorityCommand);
            }

            if (logToConsole)
            {
                Debug.Log($"[GunnerFamiliar] 左键指令 → 指针 {origin} 半径 {commandRadius}m 内 " +
                          $"{m_TargetQuery.CommandTargetCount} 个敌人，指派给 {m_Brains.Count} 只使魔", this);
            }
        }

        /// <summary>结算一次射击：球形扫描 → 按穿透数取前 N 个敌人 → 逐个走可信命中。</summary>
        private void ResolveShot(GunnerFamiliarBrain brain, Vector3 origin, Vector3 direction)
        {
            ServerCombatActivity.Action(networkObject != null ? networkObject.NetworkManager : null, m_OwnerEntityId, m_Definition.AbilityId, ++m_Sequence);
            GunnerFamiliarWeaponDefinition weapon = m_Definition.Weapon;
            if (m_HitBuffer == null || m_HitBuffer.Length < m_Definition.MaxTargets)
                m_HitBuffer = new RaycastHit[Mathf.Max(1, m_Definition.MaxTargets)];

            Vector3 aimDirection = ApplySpread(direction, weapon.spreadAngle);
            float maxDistance = Mathf.Max(0.5f, weapon.maxFireDistance);
            float radius = Mathf.Max(0.01f, weapon.projectileRadius);

            int count = Physics.SphereCastNonAlloc(origin, radius, aimDirection, m_HitBuffer,
                maxDistance, m_Definition.TargetMask, QueryTriggerInteraction.Collide);

            Vector3 endPoint = origin + aimDirection * maxDistance;
            if (count > 0)
            {
                // SphereCast 的返回顺序不保证按距离排序，手动排一次，穿透才符合直觉（先打近的）。
                SortByDistance(m_HitBuffer, count);
                endPoint = m_HitBuffer[0].point;
            }

            if (debugDrawShots) Debug.DrawLine(origin, endPoint, Color.yellow, 0.1f);
            float damage = ReadAttribute(StatKeys.Damage) * m_Definition.BaseDamageInheritRatio *
                           weapon.damageMultiplier;
            if (damage <= 0f || count <= 0)
            {
                // 没打中任何东西也要把子弹画出来（飞满最大射程），否则玩家会以为没开火。
                PresentShot(origin, endPoint, aimDirection, weapon, false);
                return;
            }

            float knockback = ReadAttribute(StatKeys.KnockbackForce) * weapon.knockbackMultiplier;
            int pierce = Mathf.Max(1, weapon.pierceCount);
            DamageTags tags = m_Definition.WeaponTag | weapon.extraDamageTags;
            int resolved = 0;
            Vector3 tracerEnd = endPoint;   // 穿透武器让子弹飞过所有被打中的目标，而不是停在第一个

            for (int i = 0; i < count && resolved < pierce; i++)
            {
                Collider hitCollider = m_HitBuffer[i].collider;
                if (hitCollider == null) continue;
                if (!FamiliarTargetQuery.TryFindTarget(
                        hitCollider, out _, out ITrustedCombatHitTarget trusted, out IHittable fallback))
                    continue;

                GameplayEntityId targetId = FamiliarTargetQuery.ResolveEntityId(hitCollider.transform);
                if (targetId.IsNone) continue;

                Vector3 force = aimDirection * knockback;
                var request = new DamageRequest(m_OwnerEntityId, targetId, m_Definition.AbilityId,
                    m_Sequence++, damage, tags);
                var hit = new TrustedCombatHit(request, weapon.element, m_Statuses,
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
                        hitPoint = m_HitBuffer[i].point,
                        hitNormal = -aimDirection,
                        attackerId = m_OwnerEntityId.IsNone ? 0UL : m_OwnerEntityId.Value - 1UL,
                        impactForce = force
                    });
                }
                resolved++;
                tracerEnd = m_HitBuffer[i].point;
            }

            PresentShot(origin, tracerEnd, aimDirection, weapon, true);
        }

        /// <summary>按散布角随机偏转朝向（0 = 精准）。</summary>
        private static Vector3 ApplySpread(Vector3 direction, float spreadAngle)
        {
            if (spreadAngle <= 0f) return direction;
            Vector3 up = Mathf.Abs(direction.y) > 0.99f ? Vector3.forward : Vector3.up;
            Vector3 right = Vector3.Cross(up, direction).normalized;
            Vector3 realUp = Vector3.Cross(direction, right).normalized;
            Quaternion offset = Quaternion.AngleAxis(Random.Range(-spreadAngle, spreadAngle), realUp) *
                                Quaternion.AngleAxis(Random.Range(-spreadAngle, spreadAngle), right);
            return (offset * direction).normalized;
        }

        private static void SortByDistance(RaycastHit[] buffer, int count)
        {
            // 命中数很少（≤ MaxTargets），插入排序足够且无 GC。
            for (int i = 1; i < count; i++)
            {
                RaycastHit key = buffer[i];
                int j = i - 1;
                while (j >= 0 && buffer[j].distance > key.distance)
                {
                    buffer[j + 1] = buffer[j];
                    j--;
                }
                buffer[j + 1] = key;
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
    }
}
