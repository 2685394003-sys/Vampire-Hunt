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
    }

    public enum AbilityPlanProperty : byte
    {
        Damage = 0,
        ProjectileCount = 1,
        SpreadAngle = 2,
        PierceCount = 3,
        TravelDistance = 4,
        Cooldown = 5
    }

    public enum EffectNumericOperation : byte
    {
        AddPerStack = 0,
        MultiplyAddPerStack = 1,
        MaxConstant = 2
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
}
