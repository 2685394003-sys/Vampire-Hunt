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
        // ── 血契 / 调试开关 ───────────────────────────────────

        /// <summary>开关使魔（血契「获得使魔」时调用 true）。</summary>
        public void SetActive(bool active)
        {
            ComposeIfNeeded();
            m_Active = active;
            if (!m_Active)
            {
                for (int i = 0; i < m_Brains.Count; i++) m_Brains[i].AbortEngagement();
                ReleaseBrains();     // 关掉的使魔不留空位
                DestroyVisuals();
                return;
            }
            RebuildBrains();
            SpawnVisuals();
        }

        /// <summary>使魔是否处于激活状态。</summary>
        public bool IsActive => m_Active;

        /// <summary>当前使魔数量。</summary>
        public int FamiliarCount => m_Brains.Count;

        /// <summary>设置使魔数量（血契「使魔·增殖」等调用），会立即重建。</summary>
        public void SetFamiliarCount(int count)
        {
            if (count < 1) count = 1;
            familiarCount = count;
            if (!m_Active) return;
            RebuildBrains();
            SpawnVisuals();
        }

        // ── 运行期系数接口（供血契调整）──────────────────────

        /// <summary>设置基础伤害继承系数（1 = 全额继承玩家基础伤害）。</summary>
        public void SetBaseDamageInheritRatio(float ratio) => OverrideDefinition(baseDamageInheritRatio: Mathf.Max(0f, ratio));

        /// <summary>设置撞击伤害倍率。</summary>
        public void SetDamageMultiplier(float multiplier) => OverrideDefinition(damageMultiplier: Mathf.Max(0f, multiplier));

        /// <summary>
        /// 设置 buff 触发数量系数：每次触发挂载层数 = 配置层数 × 此系数（四舍五入、最少 1）。
        /// </summary>
        public void SetBuffTriggerCountMultiplier(float multiplier) =>
            OverrideDefinition(buffTriggerCountMultiplier: Mathf.Max(0f, multiplier));

        /// <summary>设置冲刺速度（血契「使魔·疾行」）。</summary>
        public void SetDashSpeed(float speed) => OverrideDefinition(dashSpeed: Mathf.Max(0.1f, speed));

        /// <summary>设置环绕半径（血契「使魔·扩域」）。</summary>
        public void SetOrbitRadius(float radius) => OverrideDefinition(orbitRadius: Mathf.Max(0.1f, radius));

        /// <summary>设置同一敌人的重复命中间隔（秒）。0 = 接触即结算、每帧都打。</summary>
        public void SetHitCooldownPerTarget(float seconds) =>
            OverrideDefinition(hitCooldownPerTarget: Mathf.Max(0f, seconds));

        /// <summary>设置穿过目标后继续飞的距离（米），即穿插的「越过量」。</summary>
        public void SetOvershootDistance(float distance) =>
            OverrideDefinition(overshootDistance: Mathf.Max(0.5f, distance));

        /// <summary>
        /// 设置使魔元素与命中附加状态（血契「使魔·元素化」/ 调试键调用）。
        /// <paramref name="onHitStatuses"/> 传 null 则只改元素、保留原有命中状态。
        /// </summary>
        public void SetElement(ElementId element, StatusEffectSpec[] onHitStatuses = null)
        {
            ComposeIfNeeded();
            OverrideDefinition(element: element, onHitStatuses: onHitStatuses);
        }

        // ── 血契「牵丝之契」接口 ──────────────────────────────

        /// <summary>
        /// 开关「围绕鼠标」（血契「牵丝之契」选中时调用 true）。<br/>
        /// 关闭后环绕中心会<b>平滑地</b>滑回玩家身上（不是瞬移），使魔行为回到默认（围绕玩家）。
        /// </summary>
        public void SetPointerOrbit(bool enabled) => pointerOrbit = enabled;

        /// <summary>是否处于「围绕鼠标」模式。</summary>
        public bool PointerOrbit => pointerOrbit;

        /// <summary>设置左键指令的索敌半径（米）：以指针为圆心多大范围内的敌人会被指派。</summary>
        public void SetCommandRadius(float radius) => commandRadius = Mathf.Max(0.5f, radius);

        private void OverrideDefinition(
            float? baseDamageInheritRatio = null,
            float? damageMultiplier = null,
            float? buffTriggerCountMultiplier = null,
            float? dashSpeed = null,
            float? orbitRadius = null,
            float? hitCooldownPerTarget = null,
            float? overshootDistance = null,
            ElementId? element = null,
            StatusEffectSpec[] onHitStatuses = null)
        {
            ImpactFamiliarDefinition current = m_Definition;
            m_Definition = new ImpactFamiliarDefinition(
                current.AbilityId, current.WeaponTag,
                orbitRadius ?? current.OrbitRadius, current.OrbitSpeed, current.OrbitHeight,
                current.BobAmplitude, current.BobFrequency, current.FollowLerp,
                current.AcquireRange, current.PendingTargetLifetime, current.TargetMask, current.MaxTargets,
                dashSpeed ?? current.DashSpeed, current.MaxDashDuration, current.MaxDashDistance,
                current.ArriveDistance,
                overshootDistance ?? current.OvershootDistance,
                current.TurnDuration, current.TurnSpeedRatio, current.TurnLerp,
                current.MaxPassesPerTarget, current.SwitchTargetOnNewCandidate, current.StopWhenPendingExpired,
                current.ImpactRadius,
                current.ReturnSpeed, current.ReturnArriveDistance,
                baseDamageInheritRatio ?? current.BaseDamageInheritRatio,
                damageMultiplier ?? current.DamageMultiplier,
                current.KnockbackMultiplier,
                hitCooldownPerTarget ?? current.HitCooldownPerTarget,
                element ?? current.Element, onHitStatuses ?? current.OnHitStatuses,
                buffTriggerCountMultiplier ?? current.BuffTriggerCountMultiplier,
                current.VisualScale, current.StretchFactor);
            RebuildStatuses();
            RebuildBrains();
            if (m_Active) SpawnVisuals();
        }

        // ── 血契调制端口实现（IFamiliarPactTarget，供 7xxx 契模块调用）────────

        public FamiliarKind Kind => FamiliarKind.Impact;

        /// <summary>召唤（7001）：激活撞击使魔群。仅一次、永久；重复调用幂等。</summary>
        public void ActivateFamiliar() => SetActive(true);

        public void RegisterDamageScale(object source, float factor)
        {
            m_DamageScales[source] = Mathf.Max(0f, factor);
            RecomputeFromPactScales();
        }

        public void RegisterIntervalScale(object source, float factor)
        {
            m_IntervalScales[source] = Mathf.Max(0f, factor);
            RecomputeFromPactScales();
        }

        public void RegisterCountAdd(object source, int add)
        {
            m_CountAdds[source] = add;
            RecomputeFromPactScales();
        }

        /// <summary>撞击使魔无武器档位（契数据保证 7xxx 不含档位契）；no-op。</summary>
        public void SetWeaponTier(object source, int weaponTier) { }

        public void ConvertElement(object source, ElementId element, uint statusId,
            int statusStacks, float statusDuration, float stackEffMultiplier)
        {
            m_ElementSource = source;
            m_OverrideElement = element;
            m_OverrideStatuses = statusId != 0
                ? new[] { new StatusEffectSpec(statusId, Mathf.Max(1, statusStacks), statusDuration, 0f, element) }
                : System.Array.Empty<StatusEffectSpec>();
            m_ElementStackEff = Mathf.Max(0f, stackEffMultiplier);
            RecomputeFromPactScales();
        }

        public void UnregisterAll(object source)
        {
            bool changed = m_DamageScales.Remove(source);
            changed |= m_IntervalScales.Remove(source);
            changed |= m_CountAdds.Remove(source);
            if (ReferenceEquals(m_ElementSource, source))
            {
                m_ElementSource = null;
                m_OverrideElement = ElementId.None;
                m_OverrideStatuses = System.Array.Empty<StatusEffectSpec>();
                m_ElementStackEff = 1f;
                changed = true;
            }
            if (changed) RecomputeFromPactScales();
        }

        /// <summary>
        /// 血契变更后的统一重算：<b>从配置资产重建基线定义</b>，再把全部已注册契因子乘/加上去。
        /// 契间伤害/间隔因子乘法叠加、数量加法叠加（基础 = prefab familiarCount）；元素转化契覆写元素与命中状态。
        /// 由 <see cref="OverrideDefinition"/> 完成重建并触发 RebuildBrains（契变更发生在升级选牌/叠层时刻，频率低）。
        /// </summary>
        private void RecomputeFromPactScales()
        {
            if (definitionAsset == null) return;   // 未接资产前无法重建（Awake 后必然已接）
            ComposeIfNeeded();
            if (!m_Composed) return;

            float damageFactor = 1f;
            foreach (float factor in m_DamageScales.Values) damageFactor *= factor;
            float intervalFactor = 1f;
            foreach (float factor in m_IntervalScales.Values) intervalFactor *= factor;
            bool hasElement = m_ElementSource != null;

            // ① 回到资产基线（契因子永远相对 asset 默认值乘，避免叠层时二次累乘）
            ImpactFamiliarDefinition baseline = definitionAsset.CreateDefinition();
            m_Definition = baseline;
            // ② 套用全部契调制（OverrideDefinition 内部会 RebuildStatuses + RebuildBrains）
            OverrideDefinition(
                damageMultiplier: baseline.DamageMultiplier * damageFactor,
                buffTriggerCountMultiplier: hasElement
                    ? baseline.BuffTriggerCountMultiplier * m_ElementStackEff
                    : baseline.BuffTriggerCountMultiplier,
                hitCooldownPerTarget: baseline.HitCooldownPerTarget * intervalFactor,
                element: hasElement ? m_OverrideElement : (ElementId?)null,
                onHitStatuses: hasElement ? m_OverrideStatuses : null);
        }

        /// <summary>实际使魔数量 = prefab 基础数量 + 契增量合计（下限 1）。</summary>
        private int EffectiveFamiliarCount
        {
            get
            {
                int add = 0;
                foreach (int value in m_CountAdds.Values) add += value;
                return familiarCount + add;
            }
        }
    }
}
