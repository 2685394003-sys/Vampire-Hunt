using UnityEngine;
using VampireHunt.Effects;

namespace VampireHunt.Infrastructure.Unity.Effects
{
    /// <summary>
    /// 解锁类模块（unlock module）——「获得武器 / 获得领域」血契的效果模块资产。
    /// 血契生效（Install）时执行一次：
    /// · Weapon：把活动主武器切到 <c>targetId</c>（狙击 110 / 步枪 120 / 导弹 130 / 激光 140）；
    /// · Field：激活常驻圆型领域并写入 <c>fieldDamageInheritRatio</c>（荒芜降临 6001 = 0.3）。
    /// </summary>
    /// <remarks>
    /// 对应血契：2001 血穿魔弹 / 3001 血飞魔剑 / 4001 血爆魔阵 / 5001 血光魔炮 / 6001 荒芜降临。
    /// 依赖端口：武器 → CombatAbilityHost（IWeaponUnlockTarget）；领域 → CircleFieldAuraDriver（IFieldActivationTarget）。
    /// </remarks>
    [CreateAssetMenu(fileName = "Unlock", menuName = "Vampire Hunt/Effects/Unlock")]
    public sealed class UnlockModuleAsset : EffectModuleAsset
    {
        [Header("解锁类型")]
        [Tooltip("Weapon = 切换主武器（获得武器）；Field = 激活圆型领域（获得领域）。")]
        [SerializeField] private UnlockKind kind = UnlockKind.Weapon;

        [Header("Weapon 目标")]
        [Tooltip("要解锁的主武器 id（引擎 abilityId）：狙击 110 / 步枪 120 / 导弹 130 / 激光 140。")]
        [SerializeField, Min(1)] private uint targetId = 110;

        [Header("Field 参数")]
        [Tooltip("领域基础伤害继承系数（1 = 全额继承玩家基础伤害）；0 = 不覆盖领域资产默认值。荒芜降临 6001 = 0.3。")]
        [SerializeField, Min(0f)] private float fieldDamageInheritRatio = 0.3f;

        public override IEffectModuleDescriptor ToDescriptor() =>
            new UnlockEffectDescriptor(kind, targetId, fieldDamageInheritRatio);
    }
}
