using System;
using VampireHunt.Contracts;

namespace VampireHunt.Effects
{
    public static class EffectModuleTypeIds
    {
        public const uint AbilityPlanModifier = 1;
        public const uint ApplyStatusOnHit = 2;
        public const uint PeriodicDamage = 3;
        public const uint ActionBlock = 4;
        public const uint StatusThreshold = 5;
        public const uint PresentationCue = 6;
        public const uint AttributeModifier = 7;
        public const uint ResourceCapacityModifier = 8;
        /// <summary>使魔指针指令（血契「牵丝之契」）：绕鼠标 + 左键指派目标。</summary>
        public const uint FamiliarCommand = 9;
        /// <summary>敌人移速修改（霜寒减速）：挂到 IAttributeModifierTarget（EnemyStat.MoveSpeed）。</summary>
        public const uint EnemySpeedModifier = 10;
        /// <summary>燃爆：结算灼烧剩余 DoT 并清除灼烧（经 IStatusEffectExecutor）。</summary>
        public const uint DetonateBurn = 11;
        /// <summary>闪电连锁：向周围敌人跳跃伤害 + 传导（经 IStatusEffectExecutor）。</summary>
        public const uint ChainLightning = 12;
        /// <summary>引雷：一次性伤害（经 IStatusEffectExecutor）。</summary>
        public const uint ThunderStrike = 13;
        /// <summary>解锁：获得武器 / 获得领域（血契 2001/3001/4001/5001/6001），Install 时执行一次。</summary>
        public const uint Unlock = 14;
        /// <summary>使魔调制（血契 7001~7008 撞击使魔 / 8001~8012 射击使魔）：召唤、伤害/间隔倍率、
        /// 数量增量、武器档位、元素转化。经 IFamiliarPactTarget 端口落到对应控制器。</summary>
        public const uint FamiliarPact = 15;
    }

    public enum AbilityPlanProperty : byte
    {
        Damage = 0,
        ProjectileCount = 1,
        SpreadAngle = 2,
        PierceCount = 3,
        TravelDistance = 4,
        Cooldown = 5,
        /// <summary>扇形张开全角（度）：剑气等扇形武器每颗弹丸自身的判定/视觉角度，与 SpreadAngle（排布散布）解耦。</summary>
        FanAngle = 6
    }

    public enum EffectNumericOperation : byte
    {
        AddPerStack = 0,
        MultiplyAddPerStack = 1,
        MaxConstant = 2
    }

    /// <summary>
    /// 解锁类血契的目标类型。
    /// <list type="bullet">
    /// <item><term>Weapon</term>「获得武器」：把活动主武器切换到 targetId（狙击110/步枪120/导弹130/激光140）。</item>
    /// <item><term>Field</term>「获得领域」：激活常驻圆型领域（荒芜降临 6001）。</item>
    /// </list>
    /// </summary>
    public enum UnlockKind : byte
    {
        Weapon = 0,
        Field = 1
    }

    public sealed class AbilityPlanModifierEffectDescriptor : IEffectModuleDescriptor
    {
        public uint ModuleTypeId => EffectModuleTypeIds.AbilityPlanModifier;
        public EffectExecutionRealm Realm => EffectExecutionRealm.Owner;
        public DamageTags RequiredTags { get; }
        public AbilityPlanProperty Property { get; }
        public EffectNumericOperation Operation { get; }
        public float PerStackValue { get; }
        public float ConstantValue { get; }

        public AbilityPlanModifierEffectDescriptor(
            DamageTags requiredTags,
            AbilityPlanProperty property,
            EffectNumericOperation operation,
            float perStackValue,
            float constantValue)
        {
            RequiredTags = requiredTags;
            Property = property;
            Operation = operation;
            PerStackValue = perStackValue;
            ConstantValue = constantValue;
        }
    }

    public sealed class ApplyStatusOnHitEffectDescriptor : IEffectModuleDescriptor
    {
        public uint ModuleTypeId => EffectModuleTypeIds.ApplyStatusOnHit;
        public EffectExecutionRealm Realm => EffectExecutionRealm.Owner;
        public DamageTags RequiredTags { get; }
        public uint StatusId { get; }
        public int StacksPerEffectStack { get; }
        public float Duration { get; }
        public float Magnitude { get; }
        public ElementId Element { get; }

        public ApplyStatusOnHitEffectDescriptor(
            DamageTags requiredTags,
            uint statusId,
            int stacksPerEffectStack,
            float duration,
            float magnitude,
            ElementId element)
        {
            RequiredTags = requiredTags;
            StatusId = statusId;
            StacksPerEffectStack = Math.Max(1, stacksPerEffectStack);
            Duration = Math.Max(0f, duration);
            Magnitude = Math.Max(0f, magnitude);
            Element = element;
        }
    }

    /// <summary>
    /// 使魔指针指令（familiar command effect）—— 血契「牵丝之契」(pactId 316) 的效果参数。<br/>
    /// 选中后：<b>使魔待机时围绕鼠标</b>而不是围绕玩家；<b>按住左键</b>时，
    /// 以鼠标为圆心 <c>CommandRadius</c> 米内的敌人会被指派为使魔目标，
    /// 且该指令的优先级高于「谁被打中就追谁」的被动索敌。
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>血契系统引擎侧尚未实装</b>：本 descriptor 只作为<b>数据载体</b>，
    /// 真正的运行时行为由 <c>ImpactFamiliarController</c> / <c>GunnerFamiliarController</c>
    /// 的 <c>SetPointerOrbit</c> / <c>SetCommandRadius</c> 承载（当前靠调试按键 Digit6 驱动）。
    /// 等血契系统落地，把这里的字段接到那两个方法上即可，不需要再改使魔代码。
    /// </remarks>
    public sealed class FamiliarCommandEffectDescriptor : IEffectModuleDescriptor
    {
        public uint ModuleTypeId => EffectModuleTypeIds.FamiliarCommand;
        public EffectExecutionRealm Realm => EffectExecutionRealm.Server;

        /// <summary>是否开启「围绕鼠标」（true = 血契生效）。</summary>
        public bool PointerOrbit { get; }
        /// <summary>左键指令的索敌半径（米）：以鼠标为圆心。</summary>
        public float CommandRadius { get; }
        /// <summary>指令目标的有效期（秒）：松手后多久回归被动索敌。</summary>
        public float CommandTargetLifetime { get; }
        /// <summary>环绕中心跟随鼠标的平滑速度（越大跟得越紧）。</summary>
        public float OrbitCenterFollowLerp { get; }
        /// <summary>环绕中心离玩家的最大距离（米），0 = 不限制。</summary>
        public float MaxOrbitCenterDistance { get; }

        public FamiliarCommandEffectDescriptor(
            bool pointerOrbit,
            float commandRadius,
            float commandTargetLifetime,
            float orbitCenterFollowLerp,
            float maxOrbitCenterDistance)
        {
            PointerOrbit = pointerOrbit;
            CommandRadius = Math.Max(0.5f, commandRadius);
            CommandTargetLifetime = Math.Max(0.05f, commandTargetLifetime);
            OrbitCenterFollowLerp = Math.Max(0.1f, orbitCenterFollowLerp);
            MaxOrbitCenterDistance = Math.Max(0f, maxOrbitCenterDistance);
        }
    }

    public sealed class PeriodicDamageEffectDescriptor : IEffectModuleDescriptor
    {
        public uint ModuleTypeId => EffectModuleTypeIds.PeriodicDamage;
        public EffectExecutionRealm Realm => EffectExecutionRealm.Server;
        public double Interval { get; }
        public float DamagePerStack { get; }
        public DamageTags Tags { get; }

        public PeriodicDamageEffectDescriptor(double interval, float damagePerStack, DamageTags tags)
        {
            Interval = Math.Max(0.01d, interval);
            DamagePerStack = Math.Max(0f, damagePerStack);
            Tags = tags;
        }
    }

    public sealed class ActionBlockEffectDescriptor : IEffectModuleDescriptor
    {
        public uint ModuleTypeId => EffectModuleTypeIds.ActionBlock;
        public EffectExecutionRealm Realm => EffectExecutionRealm.Server;
        public EffectBlockFlags Flags { get; }
        public ActionBlockEffectDescriptor(EffectBlockFlags flags) => Flags = flags;
    }

    public sealed class StatusThresholdEffectDescriptor : IEffectModuleDescriptor
    {
        public uint ModuleTypeId => EffectModuleTypeIds.StatusThreshold;
        public EffectExecutionRealm Realm => EffectExecutionRealm.Server;
        public int Threshold { get; }
        public uint TriggeredStatusId { get; }
        public int TriggeredStacks { get; }
        public bool ConsumeSource { get; }

        public StatusThresholdEffectDescriptor(
            int threshold,
            uint triggeredStatusId,
            int triggeredStacks,
            bool consumeSource)
        {
            Threshold = Math.Max(1, threshold);
            TriggeredStatusId = triggeredStatusId;
            TriggeredStacks = Math.Max(1, triggeredStacks);
            ConsumeSource = consumeSource;
        }
    }

    public sealed class PresentationCueEffectDescriptor : IEffectModuleDescriptor
    {
        public uint ModuleTypeId => EffectModuleTypeIds.PresentationCue;
        public EffectExecutionRealm Realm => EffectExecutionRealm.Presentation;
        public uint CueId { get; }
        public PresentationCueEffectDescriptor(uint cueId) => CueId = cueId;
    }

    public sealed class AttributeModifierEffectDescriptor : IEffectModuleDescriptor
    {
        public uint ModuleTypeId => EffectModuleTypeIds.AttributeModifier;
        public EffectExecutionRealm Realm => EffectExecutionRealm.Server | EffectExecutionRealm.Owner;
        public int AttributeId { get; }
        public AttributeModifierOperation Operation { get; }
        public float ConstantValue { get; }
        public float ValuePerStack { get; }

        public AttributeModifierEffectDescriptor(
            int attributeId,
            AttributeModifierOperation operation,
            float constantValue,
            float valuePerStack)
        {
            AttributeId = attributeId;
            Operation = operation;
            ConstantValue = constantValue;
            ValuePerStack = valuePerStack;
        }
    }

    public sealed class ResourceCapacityModifierEffectDescriptor : IEffectModuleDescriptor
    {
        public uint ModuleTypeId => EffectModuleTypeIds.ResourceCapacityModifier;
        public EffectExecutionRealm Realm => EffectExecutionRealm.Server;
        public int ResourceId { get; }
        public AttributeModifierOperation Operation { get; }
        public float ConstantValue { get; }
        public float ValuePerStack { get; }
        public CapacityChangePolicy ChangePolicy { get; }

        public ResourceCapacityModifierEffectDescriptor(
            int resourceId,
            AttributeModifierOperation operation,
            float constantValue,
            float valuePerStack,
            CapacityChangePolicy changePolicy)
        {
            ResourceId = resourceId;
            Operation = operation;
            ConstantValue = constantValue;
            ValuePerStack = valuePerStack;
            ChangePolicy = changePolicy;
        }
    }

    /// <summary>
    /// 解锁类效果 —— 「获得武器 / 获得领域」血契（2001 血穿魔弹 / 3001 血飞魔剑 /
    /// 4001 血爆魔阵 / 5001 血光魔炮 / 6001 荒芜降临）。<br/>
    /// 安装（Install）时执行一次<b>永久解锁</b>：<br/>
    /// · Weapon：把活动主武器切到 <see cref="TargetId"/>（CombatAbilityHost.SetActiveWeapon）；<br/>
    /// · Field：激活常驻圆型领域（CircleFieldAuraDriver.SetActive）并写入 <see cref="FieldDamageInheritRatio"/>。
    /// </summary>
    /// <remarks>
    /// Realm = <b>Owner</b>：武器切换 / 领域激活都作用在「拥有该玩家的端」——施法计划在本端构建，
    /// 与伤害/冷却类血契（AbilityPlanModifier，同为 Owner）一致。血契局内不回收（EndTime=+∞），
    /// Dispose 无需回滚；本模块也不占叠层语义（一次性契，Stacks 恒为 1）。
    /// </remarks>
    public sealed class UnlockEffectDescriptor : IEffectModuleDescriptor
    {
        public uint ModuleTypeId => EffectModuleTypeIds.Unlock;
        public EffectExecutionRealm Realm => EffectExecutionRealm.Owner;

        /// <summary>解锁类型：Weapon = 切主武器；Field = 激活领域。</summary>
        public UnlockKind Kind { get; }
        /// <summary>目标武器 abilityId（Weapon 类）：狙击 110 / 步枪 120 / 导弹 130 / 激光 140。</summary>
        public uint TargetId { get; }
        /// <summary>领域基础伤害继承系数（Field 类，1 = 全额继承玩家基础伤害）；0 = 不覆盖默认。</summary>
        public float FieldDamageInheritRatio { get; }

        public UnlockEffectDescriptor(UnlockKind kind, uint targetId, float fieldDamageInheritRatio)
        {
            Kind = kind;
            TargetId = targetId;
            FieldDamageInheritRatio = Math.Max(0f, fieldDamageInheritRatio);
        }
    }

    public static class BuiltInEffectModuleFactories
    {
        public static EffectModuleRegistry CreateRegistry()
        {
            var registry = new EffectModuleRegistry();
            registry.Register(new AbilityPlanModifierFactory());
            registry.Register(new ApplyStatusOnHitFactory());
            registry.Register(new PeriodicDamageFactory());
            registry.Register(new ActionBlockFactory());
            registry.Register(new StatusThresholdFactory());
            registry.Register(new PresentationCueFactory());
            registry.Register(new AttributeModifierFactory());
            registry.Register(new ResourceCapacityModifierFactory());
            registry.Register(new EnemySpeedModifierFactory());
            registry.Register(new DetonateBurnFactory());
            registry.Register(new ChainLightningFactory());
            registry.Register(new ThunderStrikeFactory());
            registry.Register(new UnlockFactory());
            registry.Register(new FamiliarPactFactory());
            return registry;
        }
    }

    internal abstract class EffectRuntimeModuleBase : IEffectRuntimeModule
    {
        protected EffectRuntimeState State;
        public virtual void Install(in EffectRuntimeState state) => State = state;
        public virtual void Update(in EffectRuntimeState previous, in EffectRuntimeState current) => State = current;
        public virtual void Tick(double time) { }
        public virtual void Dispose() { }
    }

    internal sealed class AbilityPlanModifierFactory : IEffectModuleFactory
    {
        public uint ModuleTypeId => EffectModuleTypeIds.AbilityPlanModifier;
        public IEffectRuntimeModule Create(IEffectModuleDescriptor descriptor, in EffectRuntimeContext context)
        {
            if (!(descriptor is AbilityPlanModifierEffectDescriptor typed) ||
                context.Ports == null ||
                !context.Ports.TryGet(out ICombatAbilityModifierTarget target)) return null;
            return new AbilityPlanModifierRuntime(typed, target);
        }
    }

    internal sealed class AbilityPlanModifierRuntime : EffectRuntimeModuleBase, IAbilityCastModifier
    {
        private readonly AbilityPlanModifierEffectDescriptor m_Definition;
        private ICombatAbilityModifierTarget m_Target;
        public int Priority => 100;

        public AbilityPlanModifierRuntime(
            AbilityPlanModifierEffectDescriptor definition,
            ICombatAbilityModifierTarget target)
        {
            m_Definition = definition;
            m_Target = target;
        }

        public override void Install(in EffectRuntimeState state)
        {
            base.Install(state);
            m_Target.RegisterAbilityModifier(this);
        }

        public void Modify(AbilityCastPlan plan)
        {
            if (plan == null || (plan.Tags & m_Definition.RequiredTags) != m_Definition.RequiredTags) return;
            float value = m_Definition.ConstantValue + m_Definition.PerStackValue * State.Stacks;
            switch (m_Definition.Property)
            {
                case AbilityPlanProperty.Damage:
                    plan.Damage = Apply(plan.Damage, value, m_Definition.Operation);
                    break;
                case AbilityPlanProperty.ProjectileCount:
                    plan.ProjectileCount = Math.Max(1, (int)Math.Round(Apply(
                        plan.ProjectileCount, value, m_Definition.Operation)));
                    break;
                case AbilityPlanProperty.SpreadAngle:
                    plan.SpreadAngle = Apply(plan.SpreadAngle, value, m_Definition.Operation);
                    break;
                case AbilityPlanProperty.FanAngle:
                    plan.FanAngle = Apply(plan.FanAngle, value, m_Definition.Operation);
                    break;
                case AbilityPlanProperty.PierceCount:
                    plan.PierceCount = Math.Max(1, (int)Math.Round(Apply(
                        plan.PierceCount, value, m_Definition.Operation)));
                    break;
                case AbilityPlanProperty.TravelDistance:
                    plan.TravelDistance = Apply(plan.TravelDistance, value, m_Definition.Operation);
                    break;
                case AbilityPlanProperty.Cooldown:
                    plan.Cooldown = Math.Max(0f, Apply(plan.Cooldown, value, m_Definition.Operation));
                    break;
            }
            plan.Tags |= DamageTags.Pact;
        }

        private static float Apply(float current, float value, EffectNumericOperation operation)
        {
            switch (operation)
            {
                case EffectNumericOperation.MultiplyAddPerStack: return current * (1f + value);
                case EffectNumericOperation.MaxConstant: return Math.Max(current, value);
                default: return current + value;
            }
        }

        public override void Dispose()
        {
            m_Target?.UnregisterAbilityModifier(this);
            m_Target = null;
        }
    }

    internal sealed class ApplyStatusOnHitFactory : IEffectModuleFactory
    {
        public uint ModuleTypeId => EffectModuleTypeIds.ApplyStatusOnHit;
        public IEffectRuntimeModule Create(IEffectModuleDescriptor descriptor, in EffectRuntimeContext context)
        {
            if (!(descriptor is ApplyStatusOnHitEffectDescriptor typed) ||
                context.Ports == null ||
                !context.Ports.TryGet(out ICombatAbilityModifierTarget target)) return null;
            return new ApplyStatusOnHitRuntime(typed, target);
        }
    }

    internal sealed class ApplyStatusOnHitRuntime : EffectRuntimeModuleBase, IAbilityCastModifier
    {
        private readonly ApplyStatusOnHitEffectDescriptor m_Definition;
        private ICombatAbilityModifierTarget m_Target;
        public int Priority => 200;

        public ApplyStatusOnHitRuntime(
            ApplyStatusOnHitEffectDescriptor definition,
            ICombatAbilityModifierTarget target)
        {
            m_Definition = definition;
            m_Target = target;
        }

        public override void Install(in EffectRuntimeState state)
        {
            base.Install(state);
            m_Target.RegisterAbilityModifier(this);
        }

        public void Modify(AbilityCastPlan plan)
        {
            if (plan == null || (plan.Tags & m_Definition.RequiredTags) != m_Definition.RequiredTags) return;
            plan.OnHitStatuses.Add(new StatusEffectSpec(
                m_Definition.StatusId,
                m_Definition.StacksPerEffectStack * State.Stacks,
                m_Definition.Duration,
                m_Definition.Magnitude,
                m_Definition.Element));
            if (plan.Element == ElementId.None) plan.Element = m_Definition.Element;
            plan.Tags |= DamageTags.Pact;
        }

        public override void Dispose()
        {
            m_Target?.UnregisterAbilityModifier(this);
            m_Target = null;
        }
    }

    internal sealed class PeriodicDamageFactory : IEffectModuleFactory
    {
        public uint ModuleTypeId => EffectModuleTypeIds.PeriodicDamage;
        public IEffectRuntimeModule Create(IEffectModuleDescriptor descriptor, in EffectRuntimeContext context) =>
            descriptor is PeriodicDamageEffectDescriptor typed && context.Commands != null
                ? new PeriodicDamageRuntime(typed, context.Commands)
                : null;
    }

    internal sealed class PeriodicDamageRuntime : EffectRuntimeModuleBase
    {
        private readonly PeriodicDamageEffectDescriptor m_Definition;
        private readonly IEffectCommandSink m_Commands;
        private double m_NextTick;

        public PeriodicDamageRuntime(PeriodicDamageEffectDescriptor definition, IEffectCommandSink commands)
        {
            m_Definition = definition;
            m_Commands = commands;
        }

        public override void Install(in EffectRuntimeState state)
        {
            base.Install(state);
            m_NextTick = state.StartTime + m_Definition.Interval;
        }

        public override void Tick(double time)
        {
            if (time < m_NextTick || time >= State.EndTime) return;
            int dueTicks = Math.Min(8, 1 + (int)((time - m_NextTick) / m_Definition.Interval));
            m_NextTick += m_Definition.Interval * dueTicks;
            float magnitude = State.Magnitude > 0f ? State.Magnitude : 1f;
            float amount = m_Definition.DamagePerStack * magnitude * State.Stacks * dueTicks;
            if (amount > 0f) m_Commands.Enqueue(EffectCommand.PeriodicDamage(State, amount, m_Definition.Tags));
        }
    }

    internal sealed class ActionBlockFactory : IEffectModuleFactory
    {
        public uint ModuleTypeId => EffectModuleTypeIds.ActionBlock;
        public IEffectRuntimeModule Create(IEffectModuleDescriptor descriptor, in EffectRuntimeContext context) =>
            descriptor is ActionBlockEffectDescriptor typed ? new ActionBlockRuntime(typed.Flags) : null;
    }

    internal sealed class ActionBlockRuntime : EffectRuntimeModuleBase, IEffectBlockProvider
    {
        public EffectBlockFlags BlockFlags { get; }
        public ActionBlockRuntime(EffectBlockFlags flags) => BlockFlags = flags;
    }

    internal sealed class StatusThresholdFactory : IEffectModuleFactory
    {
        public uint ModuleTypeId => EffectModuleTypeIds.StatusThreshold;
        public IEffectRuntimeModule Create(IEffectModuleDescriptor descriptor, in EffectRuntimeContext context) =>
            descriptor is StatusThresholdEffectDescriptor typed && context.Commands != null
                ? new StatusThresholdRuntime(typed, context.Commands)
                : null;
    }

    internal sealed class StatusThresholdRuntime : EffectRuntimeModuleBase
    {
        private readonly StatusThresholdEffectDescriptor m_Definition;
        private readonly IEffectCommandSink m_Commands;
        private bool m_Triggered;

        public StatusThresholdRuntime(StatusThresholdEffectDescriptor definition, IEffectCommandSink commands)
        {
            m_Definition = definition;
            m_Commands = commands;
        }

        public override void Install(in EffectRuntimeState state)
        {
            base.Install(state);
            Evaluate();
        }

        public override void Update(in EffectRuntimeState previous, in EffectRuntimeState current)
        {
            base.Update(previous, current);
            if (current.Stacks < m_Definition.Threshold) m_Triggered = false;
            Evaluate();
        }

        private void Evaluate()
        {
            if (m_Triggered || State.Stacks < m_Definition.Threshold || m_Definition.TriggeredStatusId == 0) return;
            m_Triggered = true;
            m_Commands.Enqueue(EffectCommand.ApplyStatus(
                State, m_Definition.TriggeredStatusId, m_Definition.TriggeredStacks));
            if (m_Definition.ConsumeSource) m_Commands.Enqueue(EffectCommand.RemoveSource(State));
        }
    }

    internal sealed class PresentationCueFactory : IEffectModuleFactory
    {
        public uint ModuleTypeId => EffectModuleTypeIds.PresentationCue;
        public IEffectRuntimeModule Create(IEffectModuleDescriptor descriptor, in EffectRuntimeContext context) =>
            descriptor is PresentationCueEffectDescriptor ? new PresentationCueRuntime() : null;
    }

    internal sealed class PresentationCueRuntime : EffectRuntimeModuleBase
    {
    }

    internal sealed class AttributeModifierFactory : IEffectModuleFactory
    {
        public uint ModuleTypeId => EffectModuleTypeIds.AttributeModifier;

        public IEffectRuntimeModule Create(IEffectModuleDescriptor descriptor, in EffectRuntimeContext context)
        {
            if (!(descriptor is AttributeModifierEffectDescriptor typed) ||
                typed.AttributeId == 0 ||
                context.Ports == null ||
                !context.Ports.TryGet(out IAttributeModifierTarget target)) return null;
            return new AttributeModifierRuntime(typed, target);
        }
    }

    internal sealed class AttributeModifierRuntime : EffectRuntimeModuleBase, IAttributeModifier
    {
        private readonly AttributeModifierEffectDescriptor m_Definition;
        private IAttributeModifierTarget m_Target;

        public int AttributeId => m_Definition.AttributeId;
        public AttributeModifierOperation Operation => m_Definition.Operation;
        public float Value => m_Definition.ConstantValue + m_Definition.ValuePerStack * State.Stacks;

        public AttributeModifierRuntime(
            AttributeModifierEffectDescriptor definition,
            IAttributeModifierTarget target)
        {
            m_Definition = definition;
            m_Target = target;
        }

        public override void Install(in EffectRuntimeState state)
        {
            base.Install(state);
            m_Target.RegisterAttributeModifier(this);
        }

        public override void Dispose()
        {
            m_Target?.UnregisterAttributeModifier(this);
            m_Target = null;
        }
    }

    internal sealed class ResourceCapacityModifierFactory : IEffectModuleFactory
    {
        public uint ModuleTypeId => EffectModuleTypeIds.ResourceCapacityModifier;

        public IEffectRuntimeModule Create(IEffectModuleDescriptor descriptor, in EffectRuntimeContext context)
        {
            if (!(descriptor is ResourceCapacityModifierEffectDescriptor typed) ||
                typed.ResourceId == 0 ||
                context.Ports == null ||
                !context.Ports.TryGet(out IResourceCapacityModifierTarget target)) return null;
            return new ResourceCapacityModifierRuntime(typed, target);
        }
    }

    internal sealed class ResourceCapacityModifierRuntime : EffectRuntimeModuleBase, IResourceCapacityModifier
    {
        private readonly ResourceCapacityModifierEffectDescriptor m_Definition;
        private IResourceCapacityModifierTarget m_Target;

        public int ResourceId => m_Definition.ResourceId;
        public AttributeModifierOperation Operation => m_Definition.Operation;
        public float Value => m_Definition.ConstantValue + m_Definition.ValuePerStack * State.Stacks;
        public CapacityChangePolicy ChangePolicy => m_Definition.ChangePolicy;

        public ResourceCapacityModifierRuntime(
            ResourceCapacityModifierEffectDescriptor definition,
            IResourceCapacityModifierTarget target)
        {
            m_Definition = definition;
            m_Target = target;
        }

        public override void Install(in EffectRuntimeState state)
        {
            base.Install(state);
            m_Target.RegisterCapacityModifier(this);
        }

        public override void Update(in EffectRuntimeState previous, in EffectRuntimeState current)
        {
            base.Update(previous, current);
            m_Target?.UpdateCapacityModifier(this);
        }

        public override void Dispose()
        {
            m_Target?.UnregisterCapacityModifier(this);
            m_Target = null;
        }
    }

    /// <summary>
    /// 解锁工厂：武器解锁经 <see cref="IWeaponUnlockTarget"/>（CombatAbilityHost 实现），
    /// 领域激活经 <see cref="IFieldActivationTarget"/>（CircleFieldAuraDriver 实现）。
    /// 两个宿主与 GameplayEffectHost 挂在同一 GameObject，端口由 GameplayEffectHost.Awake 自动收集。
    /// </summary>
    internal sealed class UnlockFactory : IEffectModuleFactory
    {
        public uint ModuleTypeId => EffectModuleTypeIds.Unlock;

        public IEffectRuntimeModule Create(IEffectModuleDescriptor descriptor, in EffectRuntimeContext context)
        {
            if (!(descriptor is UnlockEffectDescriptor typed) || context.Ports == null) return null;
            switch (typed.Kind)
            {
                case UnlockKind.Weapon:
                    return context.Ports.TryGet(out IWeaponUnlockTarget weapon)
                        ? new WeaponUnlockRuntime(typed, weapon)
                        : null;
                case UnlockKind.Field:
                    return context.Ports.TryGet(out IFieldActivationTarget field)
                        ? new FieldUnlockRuntime(typed, field)
                        : null;
                default:
                    return null;
            }
        }
    }

    /// <summary>武器解锁运行时：「获得武器」血契生效时把活动主武器切到目标武器（仅一次）。</summary>
    internal sealed class WeaponUnlockRuntime : EffectRuntimeModuleBase
    {
        private readonly UnlockEffectDescriptor m_Definition;
        private readonly IWeaponUnlockTarget m_Target;

        public WeaponUnlockRuntime(UnlockEffectDescriptor definition, IWeaponUnlockTarget target)
        {
            m_Definition = definition;
            m_Target = target;
        }

        public override void Install(in EffectRuntimeState state)
        {
            base.Install(state);
            // 永久解锁：血契局内不回收，Dispose 不回滚（没有「换回旧武器」的撤销语义）。
            if (m_Definition.TargetId != 0 && m_Target != null) m_Target.TryUnlockWeapon(m_Definition.TargetId);
        }
    }

    /// <summary>领域激活运行时：「获得领域」血契（荒芜降临）生效时激活常驻圆型领域并写入伤害继承系数。</summary>
    internal sealed class FieldUnlockRuntime : EffectRuntimeModuleBase
    {
        private readonly UnlockEffectDescriptor m_Definition;
        private readonly IFieldActivationTarget m_Target;

        public FieldUnlockRuntime(UnlockEffectDescriptor definition, IFieldActivationTarget target)
        {
            m_Definition = definition;
            m_Target = target;
        }

        public override void Install(in EffectRuntimeState state)
        {
            base.Install(state);
            if (m_Target != null) m_Target.ActivateField(m_Definition.FieldDamageInheritRatio);
        }
    }
}
