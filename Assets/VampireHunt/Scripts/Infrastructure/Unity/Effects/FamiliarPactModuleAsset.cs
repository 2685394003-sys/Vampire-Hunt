using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Effects;

namespace VampireHunt.Infrastructure.Unity.Effects
{
    /// <summary>
    /// 使魔调制模块（familiar pact module）—— 血契 7001~7008（撞击使魔）/ 8001~8012（射击使魔）的效果模块资产。
    /// </summary>
    /// <remarks>
    /// <b>与运行时的关系</b>：FamiliarPact 模块运行时（VampireHunt.Effects 层）按 <see cref="Kind"/>
    /// 定位玩家身上的 <c>IFamiliarPactTarget</c> 实现 —— <c>ImpactFamiliarController</c>（撞击）/
    /// <c>GunnerFamiliarController</c>（射击），把本模块的参数注册成对该控制器使魔群的调制。
    /// 契间乘法叠加（伤害/间隔因子）、加法叠加（数量）、覆写（武器档位/元素转化）。
    /// </remarks>
    [CreateAssetMenu(fileName = "FamiliarPact", menuName = "Vampire Hunt/Effects/Familiar Pact")]
    public sealed class FamiliarPactModuleAsset : EffectModuleAsset
    {
        [Header("目标")]
        [Tooltip("目标使魔类别：Impact = 撞击使魔（7xxx 血剑系）；Gunner = 射击使魔（8xxx 影卫系）。")]
        [SerializeField] private FamiliarKind kind = FamiliarKind.Impact;

        [Header("操作")]
        [Tooltip("调制操作：Activate=召唤（获得契）；DamageScale=伤害因子；IntervalScale=命中/射击间隔因子；" +
                 "CountAdd=数量增量；WeaponTier=换武器档位（Gunner）；ConvertElement=元素转化。")]
        [SerializeField] private FamiliarPactOp op = FamiliarPactOp.Activate;

        [Header("数值（语义随操作）")]
        [Tooltip("DamageScale/IntervalScale = 单层增量比例（因子 = 1 + value×层数，可为负，如 0.25 伤害 +25%/层、-0.2 间隔 -20%/层）；" +
                 "CountAdd = 每层增量（如 1 = +1/层；狂潮一次性 +5）；WeaponTier = 档位 id（1 狙击/2 步枪/3 激光/4 导弹）。")]
        [SerializeField] private float value;

        [Header("元素转化（ConvertElement 专属）")]
        [Tooltip("转化目标元素：冰 / 火 / 雷。")]
        [SerializeField] private ElementId element = ElementId.None;
        [Tooltip("命中附加状态 id（引擎 StatusId）：冰→Frost(4) / 火→Burn(2) / 雷→Lightning(7)。")]
        [SerializeField, Min(0)] private uint statusId;
        [Tooltip("每次命中基础层数（转化契内 1）。")]
        [SerializeField, Min(0)] private int statusStacks = 1;
        [Tooltip("状态时长（秒），与引擎状态默认一致：冰 5 / 火 4 / 雷 3。")]
        [SerializeField, Min(0f)] private float statusDuration = 4f;
        [Tooltip("叠层效率系数：命中挂载层数 = 基础层数 × 此系数（转化契 ×2）。")]
        [SerializeField, Min(0f)] private float stackEffMultiplier = 1f;

        public override IEffectModuleDescriptor ToDescriptor() =>
            new FamiliarPactEffectDescriptor(kind, op, value, element, statusId, statusStacks,
                statusDuration, stackEffMultiplier);
    }
}
