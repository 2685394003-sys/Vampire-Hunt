using System;
using VampireHunt.Contracts;

namespace VampireHunt.Player.Abilities.CircleField
{
    /// <summary>
    /// 圆型领域（field，常驻光环）纯数据定义。
    /// 形态：圆心跟随玩家，按 <see cref="TickInterval"/> 周期对半径内全部敌人结算一次伤害并挂载 buff（状态）。
    /// 伤害口径与玩家发射型武器（狙击 110 / 步枪 120 / 激光 140 / 导弹 130）一致：
    /// 每跳伤害 = 玩家基础伤害 × 基础伤害继承系数 × 每跳倍率。
    /// </summary>
    /// <remarks>
    /// 两个策划接口（血契系统可运行期覆盖，默认值来自配置资产）：
    /// 1) <see cref="CircleFieldAbilityDefinition.BaseDamageInheritRatio"/>：基础伤害继承系数；
    /// 2) <see cref="CircleFieldAbilityDefinition.BuffTriggerCountMultiplier"/>：buff 触发数量系数。
    /// </remarks>
    public sealed class CircleFieldAbilityDefinition
    {
        public uint AbilityId { get; }
        public AbilitySlot Slot { get; }
        public DamageTags WeaponTag { get; }

        /// <summary>基础伤害继承系数：领域从玩家基础伤害属性（DamageStat，默认 10）继承的比例。</summary>
        public float BaseDamageInheritRatio { get; }

        /// <summary>每跳伤害倍率：在继承后的伤害上再乘的系数（与狙击枪 damageMultiplier 同口径）。</summary>
        public float TickDamageMultiplier { get; }

        /// <summary>伤害间隔（秒）；同时是光环的驱动节奏。</summary>
        public float TickInterval { get; }

        /// <summary>领域半径（米）。会被血契的 AbilityPlanModifier(TravelDistance) 再缩放。</summary>
        public float Radius { get; }

        /// <summary>领域中心相对玩家脚下（origin）的高度偏移，用于覆盖敌人碰撞体。</summary>
        public float HeightOffset { get; }

        /// <summary>击退力倍率：最终击退力 = 全局击退力 × 此倍率，方向 = 从圆心指向敌人。0 = 不击退。</summary>
        public float KnockbackMultiplier { get; }

        /// <summary>
        /// buff 触发数量系数：领域每次触发挂载的状态层数 = onHitStatuses 里配置的层数 × 此系数
        /// （四舍五入，最少 1 层）。玩家发射型武器暂无此接口。
        /// </summary>
        public float BuffTriggerCountMultiplier { get; }

        public ElementId Element { get; }
        public StatusEffectSpec[] OnHitStatuses { get; }

        public CircleFieldAbilityDefinition(
            uint abilityId,
            AbilitySlot slot,
            DamageTags weaponTag,
            float baseDamageInheritRatio,
            float tickDamageMultiplier,
            float tickInterval,
            float radius,
            float heightOffset,
            float knockbackMultiplier,
            float buffTriggerCountMultiplier,
            ElementId element,
            StatusEffectSpec[] onHitStatuses)
        {
            AbilityId = abilityId;
            Slot = slot;
            WeaponTag = weaponTag;
            BaseDamageInheritRatio = Math.Max(0f, baseDamageInheritRatio);
            TickDamageMultiplier = Math.Max(0f, tickDamageMultiplier);
            TickInterval = Math.Max(0.01f, tickInterval);
            Radius = Math.Max(0.1f, radius);
            HeightOffset = heightOffset;
            KnockbackMultiplier = Math.Max(0f, knockbackMultiplier);
            BuffTriggerCountMultiplier = Math.Max(0f, buffTriggerCountMultiplier);
            Element = element;
            OnHitStatuses = onHitStatuses ?? Array.Empty<StatusEffectSpec>();
        }
    }

    /// <summary>
    /// 圆型领域的纯施法计划（cast plan）构造器。
    /// 由 <c>CircleFieldAuraDriver</c> 每帧驱动，自身只负责按 TickInterval 节流出一次全向 AOE 计划；
    /// 实际范围查询与伤害结算在服务器侧 <c>CircleFieldAbilityExecutor</c> 完成。
    /// </summary>
    public sealed class CircleFieldAbilityRuntime : ICombatAbility
    {
        private readonly CircleFieldAbilityDefinition m_Definition;
        private double m_NextReadyTime;
        private float m_BaseDamageInheritRatio;
        private float m_BuffTriggerCountMultiplier;
        private float m_RadiusScale = 1f;

        public uint AbilityId => m_Definition.AbilityId;
        public AbilitySlot Slot => m_Definition.Slot;

        /// <summary>基础伤害继承系数（策划接口）：可在运行期被血契覆盖。</summary>
        public float BaseDamageInheritRatio
        {
            get => m_BaseDamageInheritRatio;
            set => m_BaseDamageInheritRatio = Math.Max(0f, value);
        }

        /// <summary>buff 触发数量系数（策划接口）：每次触发挂载层数 = 配置层数 × 此系数。</summary>
        public float BuffTriggerCountMultiplier
        {
            get => m_BuffTriggerCountMultiplier;
            set => m_BuffTriggerCountMultiplier = Math.Max(0f, value);
        }

        /// <summary>半径缩放（运行期接口）：「领域·扩大」类血契调用，1 = 不缩放。</summary>
        public float RadiusScale
        {
            get => m_RadiusScale;
            set => m_RadiusScale = Math.Max(0.05f, value);
        }

        /// <summary>当前实际领域半径（米）= 配置半径 × 半径缩放。</summary>
        public float CurrentRadius => Math.Max(0.05f, m_Definition.Radius * m_RadiusScale);

        /// <summary>当前配置的领域半径（未乘缩放），供表现层读取。</summary>
        public float BaseRadius => m_Definition.Radius;

        public CircleFieldAbilityRuntime(CircleFieldAbilityDefinition definition)
        {
            m_Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            m_BaseDamageInheritRatio = definition.BaseDamageInheritRatio;
            m_BuffTriggerCountMultiplier = definition.BuffTriggerCountMultiplier;
        }

        public bool TryBuildCast(in CombatAbilityActivationContext context, out AbilityCastPlan plan)
        {
            plan = null;
            if (context.Time < m_NextReadyTime || context.Damage <= 0f) return false;

            float cooldown = m_Definition.TickInterval / context.CooldownMultiplier;
            m_NextReadyTime = context.Time + cooldown;

            float damage = context.Damage * m_BaseDamageInheritRatio * m_Definition.TickDamageMultiplier;
            DamageTags tags = m_Definition.WeaponTag | DamageTags.Periodic;
            if (context.Random01 < context.CriticalChance)
            {
                damage *= context.CriticalDamageMultiplier;
                tags |= DamageTags.Critical;
            }

            Float3 direction = context.Forward.Normalized();
            if (direction.SqrMagnitude <= 0.0001f) direction = new Float3(0f, 0f, 1f);

            plan = new AbilityCastPlan
            {
                AbilityId = AbilityId,
                Slot = Slot,
                Caster = context.Caster,
                Sequence = context.Sequence,
                Origin = context.Origin + Float3.Up * m_Definition.HeightOffset,
                Direction = direction,
                Damage = damage,
                Cooldown = cooldown,
                // 圆型领域复用 TravelDistance 承载半径，因此「领域·扩大」可直接用
                // AbilityPlanModifier(TravelDistance) 实现，无需新增字段。
                TravelDistance = CurrentRadius * context.RangeMultiplier,
                ProjectileSpeed = 0f,
                Knockback = context.Knockback * m_Definition.KnockbackMultiplier,
                ProjectileSize = 1f,
                ProjectileCount = 1,
                PierceCount = 1,
                SpreadAngle = 360f, // 全向：领域不吃锥形过滤
                Tags = tags,
                Element = m_Definition.Element
            };

            AppendOnHitStatuses(plan);
            return true;
        }

        /// <summary>
        /// 把配置的状态按 buff 触发数量系数放大层数后写入计划。
        /// 实际层数 = 配置层数 × BuffTriggerCountMultiplier（四舍五入，最少 1 层）。
        /// </summary>
        private void AppendOnHitStatuses(AbilityCastPlan plan)
        {
            StatusEffectSpec[] specs = m_Definition.OnHitStatuses;
            for (int i = 0; i < specs.Length; i++)
            {
                StatusEffectSpec spec = specs[i];
                int stacks = Math.Max(1, (int)Math.Round(spec.Stacks * m_BuffTriggerCountMultiplier));
                plan.OnHitStatuses.Add(new StatusEffectSpec(
                    spec.StatusId, stacks, spec.Duration, spec.Magnitude, spec.Element));
            }
        }

        public void Reset()
        {
            m_NextReadyTime = 0d;
        }
    }
}
