namespace VampireHunt.Contracts
{
    /// <summary>
    /// 使魔类别（familiar kind）：血契的 7xxx（撞击水滴使魔，血剑系）与 8xxx（射击僚机使魔，影卫系）两条线。
    /// </summary>
    /// <remarks>
    /// 7xxx 撞击：<c>ImpactFamiliarController</c>（能力 id 161），冲锋穿插撞击；
    /// 8xxx 射击：<c>GunnerFamiliarController</c>（能力 id 162），悬停开火，含武器档位（手枪→狙击/步枪/激光/导弹）。
    /// </remarks>
    public enum FamiliarKind : byte
    {
        /// <summary>撞击水滴使魔（血剑系，7001~7008）。</summary>
        Impact = 0,
        /// <summary>射击僚机使魔（影卫系，8001~8012）。</summary>
        Gunner = 1
    }

    /// <summary>
    /// 使魔血契调制端口（familiar pact target）：撞击使魔（7xxx）与射击使魔（8xxx）类血契的效果落点。
    /// </summary>
    /// <remarks>
    /// 由 <see cref="VampireHunt.Infrastructure.Netcode.Abilities.Familiar.ImpactFamiliarController"/>（Impact）与
    /// <see cref="VampireHunt.Infrastructure.Netcode.Abilities.Familiar.GunnerFamiliarController"/>（Gunner）实现。
    /// 两个控制器与 GameplayEffectHost 挂在同一 GameObject（玩家根物体），FamiliarPact 模块
    /// （EffectModuleTypeIds.FamiliarPact）的工厂通过端口机制（EffectPortCollection.TryGet）按
    /// <see cref="Kind"/> 自动定位到对应控制器，无需显式接线。
    /// <para>
    /// <b>调制语义</b>：模块运行时以自身为 source 注册/更新/注销，控制器持有各注册表；
    /// 任何变更都会触发控制器「从配置资产重建定义并把全部契系数乘/加上去」→ 重建使魔群。
    /// 契间系数<b>乘法叠加</b>（与 plan/stat 层一致）；元素转化契与武器档位契为<b>覆写型</b>
    /// （契表互斥保证单局唯一，后装者覆盖先装者）。
    /// </para>
    /// </remarks>
    public interface IFamiliarPactTarget
    {
        /// <summary>本端口服务的使魔类别（契按此匹配，7xxx→Impact / 8xxx→Gunner）。</summary>
        FamiliarKind Kind { get; }

        /// <summary>使魔当前是否已激活（获得契 7001/8001 生效后为 true）。</summary>
        bool IsActive { get; }

        /// <summary>
        /// 召唤（获得契 7001 血剑降生 / 8001 影卫苏醒）：激活本类使魔，按 prefab 数量生成并进入环绕队列。
        /// 仅执行一次、永久生效（血契局内不回收）。
        /// </summary>
        void ActivateFamiliar();

        /// <summary>
        /// 注册伤害倍率<b>因子</b>（乘法，1 = 不变）。契值 = Π(1 + perStack × 层数)：
        /// 7002/8010 伤害 +25%/层 → 层3 = 1.75；7008/8012 狂潮防膨胀 ×0.7（近似口径：全体统一，见契资产描述）。
        /// 最终伤害 = 玩家基础伤害 × 继承系数 × 档位/默认倍率 × 本因子。
        /// </summary>
        void RegisterDamageScale(object source, float factor);

        /// <summary>
        /// 注册攻速/命中间隔<b>因子</b>（乘法，1 = 不变）：7007 撞击使魔同目标命中间隔 -20%/层、
        /// 8002 射击使魔整套冷却 -20%/层。最终间隔 = 资产默认值 × Πfactor。
        /// </summary>
        void RegisterIntervalScale(object source, float factor);

        /// <summary>
        /// 注册使魔数量<b>增量</b>（加法）：7003/8011 数量 +1/层、7008/8012 狂潮一次性 +5。
        /// 实际数量 = prefab 基础数量 + Σ增量（下限 1）。
        /// </summary>
        void RegisterCountAdd(object source, int add);

        /// <summary>
        /// 武器档位覆写（仅 Gunner；8006 魔弹→狙击 / 8007 魔剑→步枪 / 8008 魔炮→激光 / 8009 魔阵→导弹）。
        /// <paramref name="weaponTier"/> = <c>FamiliarWeaponId</c> 数值（0 手枪 1 狙击 2 步枪 3 激光 4 导弹）。
        /// 一次性契，契表互斥四选一。Impact 控制器不实现该覆写（契数据保证 7xxx 不会到达）。
        /// </summary>
        void SetWeaponTier(object source, int weaponTier);

        /// <summary>
        /// 元素转化（转化契 7004 冰/7005 火/7006 雷、8003 雷/8004 火/8005 冰，六张契表互斥三选一）：
        /// 覆写使魔攻击元素并挂对应状态（冰→霜寒 Frost、火→灼烧 Burn、雷→闪电 Lightning），
        /// 每次命中基础层数 <paramref name="statusStacks"/> × <paramref name="stackEffMultiplier"/>（转化契 = ×2）。
        /// ⚠️ 契内「对应元素系伤害 +50% / 连锁跳数 +1」段引擎暂无元素伤害通道，随 F 组排期，本端口只承载使魔侧。
        /// </summary>
        void ConvertElement(object source, ElementId element, uint statusId,
            int statusStacks, float statusDuration, float stackEffMultiplier);

        /// <summary>注销 <paramref name="source"/> 在本控制器上的全部调制（契回滚/叠层变化重算时调用）。</summary>
        void UnregisterAll(object source);
    }
}
