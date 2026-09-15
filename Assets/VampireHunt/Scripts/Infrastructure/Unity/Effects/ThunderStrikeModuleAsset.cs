using UnityEngine;
using VampireHunt.Effects;

namespace VampireHunt.Infrastructure.Unity.Effects
{
    /// <summary>
    /// 引雷（buffs_011）：闪电叠满后触发的一次性伤害。伤害 = damage（固定基数，默认 15 = 玩家基础伤害 10 × 表格引雷 p1=1.5）。
    /// </summary>
    [CreateAssetMenu(fileName = "ThunderStrike", menuName = "Vampire Hunt/Effects/Thunder Strike")]
    public sealed class ThunderStrikeModuleAsset : EffectModuleAsset
    {
        [Tooltip("引雷伤害（默认 15 = 10 × 1.5；后续按测试调）。")]
        [SerializeField, Min(0f)] private float damage = 15f;

        public override IEffectModuleDescriptor ToDescriptor() =>
            new ThunderStrikeEffectDescriptor(damage);
    }
}
