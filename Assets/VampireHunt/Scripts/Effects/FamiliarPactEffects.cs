using System;
using VampireHunt.Contracts;

namespace VampireHunt.Effects
{
    /// <summary>
    /// 使魔血契的调制操作（familiar pact operation）。
    /// </summary>
    public enum FamiliarPactOp : byte
    {
        /// <summary>召唤：激活对应类别的使魔（获得契 7001 血剑降生 / 8001 影卫苏醒）。仅执行一次。</summary>
        Activate = 0,
        /// <summary>伤害倍率因子（乘法）：7002/8010 伤害 +25%/层；7008/8012 狂潮防膨胀 ×0.7（全体近似口径）。</summary>
        DamageScale = 1,
        /// <summary>命中/射击间隔因子（乘法）：7007 撞击使魔同目标命中间隔 -20%/层；8002 射击使魔整套冷却 -20%/层。</summary>
        IntervalScale = 2,
        /// <summary>使魔数量增量（加法）：7003/8011 +1/层；7008/8012 一次性 +5。</summary>
        CountAdd = 3,
        /// <summary>武器档位覆写（Gunner，8006~8009 四选一）：value = FamiliarWeaponId（1 狙击/2 步枪/3 激光/4 导弹）。</summary>
        WeaponTier = 4,
        /// <summary>元素转化（7004 冰/7005 火/7006 雷、8003 雷/8004 火/8005 冰，契表互斥三选一）：覆写元素+命中状态+叠层效率。</summary>
        ConvertElement = 5
    }

    /// <summary>
    /// 使魔调制效果参数 —— 血契 7001~7008（撞击使魔）/ 8001~8012（射击使魔）的运行时描述。
    /// </summary>
    /// <remarks>
    /// Realm = <b>Server</b>：两个使魔控制器只在服务器推进状态机与结算（<c>IsServerAuthoritative</c>），
    /// 契必须只在服务器安装，客户端不重复执行。效果落点 = 与 GameplayEffectHost 同物体的
    /// <see cref="IFamiliarPactTarget"/> 实现（按 <see cref="Kind"/> 匹配撞击/射击控制器）。
    /// 数值契（Damage/Interval/Count）随叠层 Update 时<b>先注销再重算</b>；档位与元素转化契为覆写型。
    /// </remarks>
    public sealed class FamiliarPactEffectDescriptor : IEffectModuleDescriptor
    {
        public uint ModuleTypeId => EffectModuleTypeIds.FamiliarPact;
        public EffectExecutionRealm Realm => EffectExecutionRealm.Server;

        /// <summary>目标使魔类别：7xxx → Impact（撞击）/ 8xxx → Gunner（射击）。</summary>
        public FamiliarKind Kind { get; }
        /// <summary>调制操作。</summary>
        public FamiliarPactOp Op { get; }

        /// <summary>
        /// 操作数值，语义随 <see cref="Op"/>：
        /// DamageScale/IntervalScale = <b>单层增量比例</b>（因子 = 1 + value×层数，可为负）；
        /// CountAdd = 每层增量（总数 = 基础 + value×层数）；WeaponTier = 档位 id（一次性）。
        /// </summary>
        public float Value { get; }

        // ── ConvertElement 专属 ──
        /// <summary>转化目标元素（ConvertElement）。</summary>
        public ElementId Element { get; }
        /// <summary>命中附加状态 id（ConvertElement）：冰→Frost(4) / 火→Burn(2) / 雷→Lightning(7)。</summary>
        public uint StatusId { get; }
        /// <summary>每次命中基础层数（ConvertElement，契内 1；叠层效率 ×stackEff 后生效）。</summary>
        public int StatusStacks { get; }
        /// <summary>状态时长秒（ConvertElement：冰 5 / 火 4 / 雷 3，与引擎状态默认一致）。</summary>
        public float StatusDuration { get; }
        /// <summary>叠层效率系数（ConvertElement，契内 ×2）：命中挂载层数 = 基础层数 × 此系数。</summary>
        public float StackEffMultiplier { get; }

        public FamiliarPactEffectDescriptor(
            FamiliarKind kind,
            FamiliarPactOp op,
            float value,
            ElementId element = ElementId.None,
            uint statusId = 0,
            int statusStacks = 0,
            float statusDuration = 0f,
            float stackEffMultiplier = 1f)
        {
            Kind = kind;
            Op = op;
            Value = value;
            Element = element;
            StatusId = statusId;
            StatusStacks = Math.Max(0, statusStacks);
            StatusDuration = Math.Max(0f, statusDuration);
            StackEffMultiplier = Math.Max(0f, stackEffMultiplier);
        }
    }

    /// <summary>
    /// 使魔调制工厂：按 <see cref="FamiliarPactEffectDescriptor.Kind"/> 匹配同物体上的
    /// <see cref="IFamiliarPactTarget"/>（撞击或射击控制器）。两者都挂在玩家根物体且均实现该端口，
    /// 契数据（7xxx/8xxx）已定死 kind，不会错配。
    /// </summary>
    internal sealed class FamiliarPactFactory : IEffectModuleFactory
    {
        public uint ModuleTypeId => EffectModuleTypeIds.FamiliarPact;

        public IEffectRuntimeModule Create(IEffectModuleDescriptor descriptor, in EffectRuntimeContext context)
        {
            if (!(descriptor is FamiliarPactEffectDescriptor typed) || context.Ports == null) return null;
            // ⚠️ 撞击(Impact)与射击(Gunner)两个控制器都实现 IFamiliarPactTarget 且挂在玩家同一 GameObject：
            // 端口收集器的「首个匹配」语义会固定命中其中一个 Kind，导致 7xxx/8xxx 必有一半落空。
            // 必须走 EffectPortCollection 的谓词重载，按 Kind 精确定位本契对应的控制器。
            if (!(context.Ports is EffectPortCollection ports) ||
                !ports.TryGet<IFamiliarPactTarget>(target => target.Kind == typed.Kind, out IFamiliarPactTarget target))
                return null;
            return new FamiliarPactRuntime(typed, target);
        }
    }

    internal sealed class FamiliarPactRuntime : EffectRuntimeModuleBase
    {
        private readonly FamiliarPactEffectDescriptor m_Definition;
        private IFamiliarPactTarget m_Target;

        public FamiliarPactRuntime(FamiliarPactEffectDescriptor definition, IFamiliarPactTarget target)
        {
            m_Definition = definition;
            m_Target = target;
        }

        public override void Install(in EffectRuntimeState state)
        {
            base.Install(state);
            Apply();
        }

        public override void Update(in EffectRuntimeState previous, in EffectRuntimeState current)
        {
            base.Update(previous, current);
            // 叠层变化：先整体注销本契的旧调制（让控制器回到资产基线），再按新层数重算。
            m_Target?.UnregisterAll(this);
            Apply();
        }

        public override void Dispose()
        {
            m_Target?.UnregisterAll(this);
            m_Target = null;
        }

        private void Apply()
        {
            if (m_Target == null) return;
            switch (m_Definition.Op)
            {
                case FamiliarPactOp.Activate:
                    m_Target.ActivateFamiliar();
                    break;
                case FamiliarPactOp.DamageScale:
                    m_Target.RegisterDamageScale(this, 1f + m_Definition.Value * State.Stacks);
                    break;
                case FamiliarPactOp.IntervalScale:
                    m_Target.RegisterIntervalScale(this, 1f + m_Definition.Value * State.Stacks);
                    break;
                case FamiliarPactOp.CountAdd:
                    m_Target.RegisterCountAdd(this, (int)Math.Round(m_Definition.Value * State.Stacks));
                    break;
                case FamiliarPactOp.WeaponTier:
                    m_Target.SetWeaponTier(this, (int)Math.Round(m_Definition.Value));
                    break;
                case FamiliarPactOp.ConvertElement:
                    m_Target.ConvertElement(this, m_Definition.Element, m_Definition.StatusId,
                        m_Definition.StatusStacks, m_Definition.StatusDuration, m_Definition.StackEffMultiplier);
                    break;
            }
        }
    }
}
