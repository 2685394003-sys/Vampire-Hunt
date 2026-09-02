using System;
using VampireHunt.Contracts;

namespace VampireHunt.Effects
{
    /// <summary>
    /// 状态效果执行端口：由服务器侧的 <c>CombatStatusHost</c> 实现。
    /// 燃爆/闪电连锁/引雷这类"状态内部触发、但需要宿主结算"的效果，由状态模块经此端口调用宿主执行，
    /// 避免 Effects 层反向依赖具体宿主类。
    /// </summary>
    public interface IStatusEffectExecutor
    {
        /// <summary>燃爆：结算目标身上灼烧的剩余 DoT 总量 × multiplier，然后清除灼烧与燃爆自身。</summary>
        void ExecuteDetonateBurn(in EffectRuntimeState state, float multiplier);

        /// <summary>
        /// 闪电连锁：从目标（state.Target）向半径内其他敌人跳跃 <paramref name="jumps"/> 次，伤害逐跳 ×decay，
        /// 并把目标身上<b>可传导状态</b>（灼烧/霜寒/碎裂/炸裂）按「源层数 × <paramref name="elementMastery"/>」复制到连锁目标。
        /// </summary>
        void ExecuteChainLightning(
            in EffectRuntimeState state,
            int jumps,
            float radius,
            float decay,
            float chainDamage,
            float elementMastery);

        /// <summary>引雷：对目标造成一次固定伤害。</summary>
        void ExecuteThunderStrike(in EffectRuntimeState state, float damage);
    }

    /// <summary>敌人移速修改（霜寒减速）：Flat 方式挂到 IAttributeModifierTarget 的 EnemyStat.MoveSpeed。</summary>
    public sealed class EnemySpeedModifierEffectDescriptor : IEffectModuleDescriptor
    {
        public uint ModuleTypeId => EffectModuleTypeIds.EnemySpeedModifier;
        public EffectExecutionRealm Realm => EffectExecutionRealm.Server | EffectExecutionRealm.Owner;
        public int AttributeId { get; }
        public float SlowPerStack { get; }

        public EnemySpeedModifierEffectDescriptor(int attributeId, float slowPerStack)
        {
            AttributeId = attributeId;
            SlowPerStack = Math.Max(0f, slowPerStack);
        }
    }

    /// <summary>燃爆（buffs_010）：结算灼烧剩余伤害并清除灼烧。</summary>
    public sealed class DetonateBurnEffectDescriptor : IEffectModuleDescriptor
    {
        public uint ModuleTypeId => EffectModuleTypeIds.DetonateBurn;
        public EffectExecutionRealm Realm => EffectExecutionRealm.Server;
        public float Multiplier { get; }

        public DetonateBurnEffectDescriptor(float multiplier)
        {
            Multiplier = Math.Max(0f, multiplier);
        }
    }

    /// <summary>闪电连锁（buffs_007）：获得闪电层时向周围敌人连锁。</summary>
    public sealed class ChainLightningEffectDescriptor : IEffectModuleDescriptor
    {
        public uint ModuleTypeId => EffectModuleTypeIds.ChainLightning;
        public EffectExecutionRealm Realm => EffectExecutionRealm.Server;
        public float Radius { get; }
        public float Decay { get; }
        /// <summary>连锁伤害基础值：每跳 = 此值 × 武器可传导性 × 逐跳衰减。</summary>
        public float ChainDamage { get; }
        /// <summary>基础传导数量：每次连锁的目标数 = 此值 + 当前闪电层数。</summary>
        public int BaseJumps { get; }

        public ChainLightningEffectDescriptor(float radius, float decay, float chainDamage, int baseJumps)
        {
            Radius = Math.Max(0.1f, radius);
            Decay = Math.Max(0f, decay);
            ChainDamage = Math.Max(0f, chainDamage);
            BaseJumps = Math.Max(0, baseJumps);
        }
    }

    /// <summary>引雷（buffs_011）：闪电满层触发的一次性伤害。</summary>
    public sealed class ThunderStrikeEffectDescriptor : IEffectModuleDescriptor
    {
        public uint ModuleTypeId => EffectModuleTypeIds.ThunderStrike;
        public EffectExecutionRealm Realm => EffectExecutionRealm.Server;
        public float Damage { get; }

        public ThunderStrikeEffectDescriptor(float damage)
        {
            Damage = Math.Max(0f, damage);
        }
    }

    internal sealed class EnemySpeedModifierFactory : IEffectModuleFactory
    {
        public uint ModuleTypeId => EffectModuleTypeIds.EnemySpeedModifier;

        public IEffectRuntimeModule Create(IEffectModuleDescriptor descriptor, in EffectRuntimeContext context)
        {
            if (!(descriptor is EnemySpeedModifierEffectDescriptor typed) ||
                typed.AttributeId == 0 ||
                context.Ports == null ||
                !context.Ports.TryGet(out IAttributeModifierTarget target)) return null;
            return new EnemySpeedModifierRuntime(typed, target);
        }
    }

    internal sealed class EnemySpeedModifierRuntime : EffectRuntimeModuleBase, IAttributeModifier
    {
        private readonly EnemySpeedModifierEffectDescriptor m_Definition;
        private IAttributeModifierTarget m_Target;

        public int AttributeId => m_Definition.AttributeId;
        public AttributeModifierOperation Operation => AttributeModifierOperation.Flat;
        public float Value => -m_Definition.SlowPerStack * State.Stacks;

        public EnemySpeedModifierRuntime(
            EnemySpeedModifierEffectDescriptor definition,
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

    internal sealed class DetonateBurnFactory : IEffectModuleFactory
    {
        public uint ModuleTypeId => EffectModuleTypeIds.DetonateBurn;

        public IEffectRuntimeModule Create(IEffectModuleDescriptor descriptor, in EffectRuntimeContext context)
        {
            if (!(descriptor is DetonateBurnEffectDescriptor typed) ||
                context.Ports == null ||
                !context.Ports.TryGet(out IStatusEffectExecutor executor)) return null;
            return new DetonateBurnRuntime(typed, executor);
        }
    }

    internal sealed class DetonateBurnRuntime : EffectRuntimeModuleBase
    {
        private readonly DetonateBurnEffectDescriptor m_Definition;
        private readonly IStatusEffectExecutor m_Executor;

        public DetonateBurnRuntime(DetonateBurnEffectDescriptor definition, IStatusEffectExecutor executor)
        {
            m_Definition = definition;
            m_Executor = executor;
        }

        public override void Install(in EffectRuntimeState state)
        {
            base.Install(state);
            m_Executor.ExecuteDetonateBurn(state, m_Definition.Multiplier);
        }
    }

    internal sealed class ChainLightningFactory : IEffectModuleFactory
    {
        public uint ModuleTypeId => EffectModuleTypeIds.ChainLightning;

        public IEffectRuntimeModule Create(IEffectModuleDescriptor descriptor, in EffectRuntimeContext context)
        {
            if (!(descriptor is ChainLightningEffectDescriptor typed) ||
                context.Ports == null ||
                !context.Ports.TryGet(out IStatusEffectExecutor executor)) return null;
            return new ChainLightningRuntime(typed, executor);
        }
    }

    internal sealed class ChainLightningRuntime : EffectRuntimeModuleBase
    {
        private readonly ChainLightningEffectDescriptor m_Definition;
        private readonly IStatusEffectExecutor m_Executor;

        public ChainLightningRuntime(ChainLightningEffectDescriptor definition, IStatusEffectExecutor executor)
        {
            m_Definition = definition;
            m_Executor = executor;
        }

        public override void Install(in EffectRuntimeState state)
        {
            base.Install(state);
            // 首次挂上闪电：把「0 → floor(当前层)」视为跨越的整数层，逐层传导一次。
            TriggerCrossings(0, state);
        }

        public override void Update(in EffectRuntimeState previous, in EffectRuntimeState current)
        {
            base.Update(previous, current);
            // 多人联机友好：闪电层数是怪身上共享累计值（多玩家注入叠加），
            // 只有当层数<b>跨越一个整数层</b>时才传导一次，而不是每次叠层都传导。
            TriggerCrossings((int)Math.Floor(previous.Stacks), current);
        }

        /// <summary>
        /// 从「上一个已触发的整数层（exclusive）」到「当前层」之间每跨越一个整数层，就执行一次连锁。
        /// 例如 previous=0.8 → current=1.4：跨越了 1 层 → 传导一次。
        /// </summary>
        private void TriggerCrossings(int previousFloor, in EffectRuntimeState state)
        {
            int currentFloor = (int)Math.Floor(state.Stacks);
            if (currentFloor <= previousFloor) return;
            int jumps = m_Definition.BaseJumps + currentFloor;
            for (int crossed = previousFloor + 1; crossed <= currentFloor; crossed++)
                m_Executor.ExecuteChainLightning(
                    state, jumps, m_Definition.Radius, m_Definition.Decay, m_Definition.ChainDamage, state.ElementMastery);
        }
    }

    internal sealed class ThunderStrikeFactory : IEffectModuleFactory
    {
        public uint ModuleTypeId => EffectModuleTypeIds.ThunderStrike;

        public IEffectRuntimeModule Create(IEffectModuleDescriptor descriptor, in EffectRuntimeContext context)
        {
            if (!(descriptor is ThunderStrikeEffectDescriptor typed) ||
                context.Ports == null ||
                !context.Ports.TryGet(out IStatusEffectExecutor executor)) return null;
            return new ThunderStrikeRuntime(typed, executor);
        }
    }

    internal sealed class ThunderStrikeRuntime : EffectRuntimeModuleBase
    {
        private readonly ThunderStrikeEffectDescriptor m_Definition;
        private readonly IStatusEffectExecutor m_Executor;

        public ThunderStrikeRuntime(ThunderStrikeEffectDescriptor definition, IStatusEffectExecutor executor)
        {
            m_Definition = definition;
            m_Executor = executor;
        }

        public override void Install(in EffectRuntimeState state)
        {
            base.Install(state);
            m_Executor.ExecuteThunderStrike(state, m_Definition.Damage);
        }
    }
}
