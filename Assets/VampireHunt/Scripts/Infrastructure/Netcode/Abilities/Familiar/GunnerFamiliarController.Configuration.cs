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

        /// <summary>
        /// 切换武器档位（血契「使魔·改型」系列调用）：手枪 → 狙击 / 步枪 / 激光。
        /// 会整体替换伤害倍率、射程、连发、冷却、穿透等一整套武器参数。
        /// </summary>
        public void SetWeapon(FamiliarWeaponId weaponId)
        {
            ComposeIfNeeded();
            GunnerFamiliarWeaponProfile profile = definitionAsset != null
                ? definitionAsset.FindWeapon(weaponId)
                : null;
            m_Definition = definitionAsset != null
                ? definitionAsset.CreateDefinition(weaponId)
                : m_Definition;
            m_CurrentWeapon = weaponId;
            RebuildStatuses();
            RebuildBrains();
            if (m_Active) SpawnVisuals();

            if (logToConsole)
            {
                Debug.Log($"[GunnerFamiliar] 武器档位 → {weaponId}" +
                          (profile == null ? "（资产里没配这一档，已退回内置默认）" : $"（{profile.displayName}）"), this);
            }
        }

        // ── 运行期系数接口（供血契调整）──────────────────────

        /// <summary>设置基础伤害继承系数（1 = 全额继承玩家基础伤害）。</summary>
        public void SetBaseDamageInheritRatio(float ratio) => OverrideDefinition(baseDamageInheritRatio: Mathf.Max(0f, ratio));

        /// <summary>设置当前武器档位的伤害倍率（单发伤害 = 玩家基础伤害 × 继承系数 × 倍率）。</summary>
        public void SetDamageMultiplier(float multiplier) => OverrideDefinition(damageMultiplier: Mathf.Max(0f, multiplier));

        /// <summary>
        /// 设置 buff 触发数量系数：每次触发挂载层数 = 配置层数 × 此系数（四舍五入、最少 1）。
        /// </summary>
        public void SetBuffTriggerCountMultiplier(float multiplier) =>
            OverrideDefinition(buffTriggerCountMultiplier: Mathf.Max(0f, multiplier));

        /// <summary>设置飞行速度（血契「使魔·疾行」）。</summary>
        public void SetMoveSpeed(float speed) => OverrideDefinition(moveSpeed: Mathf.Max(0.1f, speed));

        /// <summary>设置环绕半径（血契「使魔·扩域」）。</summary>
        public void SetOrbitRadius(float radius) => OverrideDefinition(orbitRadius: Mathf.Max(0.1f, radius));

        /// <summary>设置瞄准前摇时长（秒）。</summary>
        public void SetAimDuration(float seconds) => OverrideDefinition(aimDuration: Mathf.Max(0f, seconds));

        /// <summary>
        /// 设置当前武器档位的元素与命中附加状态（血契「使魔·元素化」/ 调试键调用）。
        /// <paramref name="onHitStatuses"/> 传 null 则只改元素、保留原档位命中状态。
        /// </summary>
        public void SetElement(ElementId element, StatusEffectSpec[] onHitStatuses = null)
        {
            ComposeIfNeeded();
            GunnerFamiliarWeaponDefinition weapon = m_Definition.Weapon;
            if (weapon == null) return;
            weapon.element = element;
            if (onHitStatuses != null) weapon.onHitStatuses = onHitStatuses;
            RebuildStatuses();
            if (m_Active) RebuildBrains();
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
            float? moveSpeed = null,
            float? orbitRadius = null,
            float? aimDuration = null)
        {
            GunnerFamiliarDefinition current = m_Definition;
            GunnerFamiliarWeaponDefinition weapon = current.Weapon;
            if (damageMultiplier.HasValue) weapon.damageMultiplier = Mathf.Max(0f, damageMultiplier.Value);

            m_Definition = new GunnerFamiliarDefinition(
                current.AbilityId, current.WeaponTag,
                orbitRadius ?? current.OrbitRadius, current.OrbitSpeed, current.OrbitHeight,
                current.BobAmplitude, current.BobFrequency, current.FollowLerp,
                current.AcquireRange, current.PendingTargetLifetime, current.TargetMask, current.MaxTargets,
                moveSpeed ?? current.MoveSpeed, current.MoveLerp, current.RangeTolerance, current.StrafeSpeed,
                current.MaxApproachDuration,
                aimDuration ?? current.AimDuration, current.AimLerp,
                current.MaxVolleysPerTarget, current.SwitchTargetOnNewCandidate, current.StopWhenPendingExpired,
                current.ReturnSpeed, current.ReturnArriveDistance,
                baseDamageInheritRatio ?? current.BaseDamageInheritRatio,
                buffTriggerCountMultiplier ?? current.BuffTriggerCountMultiplier,
                weapon,
                current.VisualScale);
            RebuildStatuses();
            RebuildBrains();
            if (m_Active) SpawnVisuals();
        }

        // ── 血契调制端口实现（IFamiliarPactTarget，供 8xxx 契模块调用）────────

        public FamiliarKind Kind => FamiliarKind.Gunner;

        /// <summary>召唤（8001）：激活射击使魔群。仅一次、永久；重复调用幂等。</summary>
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

        public void SetWeaponTier(object source, int weaponTier)
        {
            m_WeaponTierSource = source;
            m_WeaponTierId = Mathf.Clamp(weaponTier, 0, 4);
            RecomputeFromPactScales();
        }

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
            if (ReferenceEquals(m_WeaponTierSource, source))
            {
                m_WeaponTierSource = null;
                m_WeaponTierId = -1;
                changed = true;
            }
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
        /// 血契变更后的统一重算：按「资产默认档位 or 契档位覆写」<b>从配置资产重建基线定义</b>，
        /// 再把全部已注册契因子乘到 weapon profile 上（伤害 ×Πscale、整套冷却 ×Πscale）、
        /// 套用元素转化覆写（元素 + 命中状态 + 叠层效率 ×stackEff）。契因子永远相对 asset 默认值乘，
        /// 避免叠层时二次累乘。契变更发生在升级选牌/叠层时刻，RebuildBrains 打断战斗属可接受行为。
        /// </summary>
        private void RecomputeFromPactScales()
        {
            if (definitionAsset == null) return;
            ComposeIfNeeded();
            if (!m_Composed) return;

            FamiliarWeaponId weaponId = m_WeaponTierSource != null
                ? (FamiliarWeaponId)Mathf.Clamp(m_WeaponTierId, 0, 4)
                : definitionAsset.DefaultWeaponId;
            m_CurrentWeapon = weaponId;
            m_Definition = definitionAsset.CreateDefinition(weaponId);   // 含武器档位拷贝（基线）
            GunnerFamiliarWeaponDefinition weapon = m_Definition.Weapon;
            if (weapon == null) return;

            float damageFactor = 1f;
            foreach (float factor in m_DamageScales.Values) damageFactor *= factor;
            float intervalFactor = 1f;
            foreach (float factor in m_IntervalScales.Values) intervalFactor *= factor;

            weapon.damageMultiplier = Mathf.Max(0f, weapon.damageMultiplier * damageFactor);
            weapon.fireCooldown = Mathf.Max(0f, weapon.fireCooldown * intervalFactor);

            if (m_ElementSource != null)
            {
                weapon.element = m_OverrideElement;
                weapon.onHitStatuses = m_OverrideStatuses;
                // 叠层效率 ×stackEff：命中挂载层数 = 基础层数 × buff 触发系数（转化契 ×2）。
                // GunnerFamiliarDefinition 是 readonly struct，乘系数需整体重建一份（weapon 引用不变，已乘的伤害/冷却保留）。
                if (!Mathf.Approximately(m_ElementStackEff, 1f))
                {
                    GunnerFamiliarDefinition current = m_Definition;
                    m_Definition = new GunnerFamiliarDefinition(
                        current.AbilityId, current.WeaponTag,
                        current.OrbitRadius, current.OrbitSpeed, current.OrbitHeight,
                        current.BobAmplitude, current.BobFrequency, current.FollowLerp,
                        current.AcquireRange, current.PendingTargetLifetime, current.TargetMask, current.MaxTargets,
                        current.MoveSpeed, current.MoveLerp, current.RangeTolerance, current.StrafeSpeed,
                        current.MaxApproachDuration,
                        current.AimDuration, current.AimLerp,
                        current.MaxVolleysPerTarget, current.SwitchTargetOnNewCandidate, current.StopWhenPendingExpired,
                        current.ReturnSpeed, current.ReturnArriveDistance,
                        current.BaseDamageInheritRatio,
                        current.BuffTriggerCountMultiplier * m_ElementStackEff,
                        weapon,
                        current.VisualScale);
                }
            }
            RebuildStatuses();
            RebuildBrains();
            if (m_Active) SpawnVisuals();
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
