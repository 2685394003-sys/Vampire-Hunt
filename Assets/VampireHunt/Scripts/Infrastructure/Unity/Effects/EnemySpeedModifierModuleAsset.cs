using UnityEngine;
using VampireHunt.Effects;

namespace VampireHunt.Infrastructure.Unity.Effects
{
    /// <summary>
    /// 敌人移速修改（霜寒减速）。Flat 方式作用到敌人的 EnemyStat.MoveSpeed（attributeId=1），
    /// 减速总量 = slowPerStack × 当前层数。走 IAttributeModifierTarget（敌人侧 EnemyNetworkActor 实现）。
    /// </summary>
    [CreateAssetMenu(fileName = "EnemySpeedModifier", menuName = "Vampire Hunt/Effects/Enemy Speed Modifier")]
    public sealed class EnemySpeedModifierModuleAsset : EffectModuleAsset
    {
        [Tooltip("EnemyStat.MoveSpeed = 1")]
        [SerializeField, Min(1)] private int attributeId = 1;
        [Tooltip("每层减速的移速减量（表格霜寒 p2 slowAmount，默认 2）。")]
        [SerializeField, Min(0f)] private float slowPerStack = 2f;

        public override IEffectModuleDescriptor ToDescriptor() =>
            new EnemySpeedModifierEffectDescriptor(attributeId, slowPerStack);
    }
}
