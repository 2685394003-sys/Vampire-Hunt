using UnityEngine;
using VampireHunt.Effects;

namespace VampireHunt.Infrastructure.Unity.Effects
{
    /// <summary>
    /// 使魔指针指令模块（familiar command module）—— 血契「牵丝之契」(pactId 316) 的效果模块资产。<br/>
    /// 把「使魔围绕鼠标 + 左键指派鼠标附近敌人」的五个可调数值挂到血契上，策划可以直接在这里改。
    /// </summary>
    /// <remarks>
    /// <b>与运行时的关系</b>：血契系统引擎侧尚未实装，所以本模块只是<b>数据载体</b>。
    /// 实际行为由 <c>ImpactFamiliarController</c> / <c>GunnerFamiliarController</c> 的同名字段承载，
    /// 两边数值保持一致即可（当前用调试按键 Digit6 验证行为）。
    /// ⚠️ 数值若不一致，以<b>控制器（prefab 上的字段）</b>为准 —— 那是真正在跑的那份。
    /// </remarks>
    [CreateAssetMenu(fileName = "FamiliarCommand", menuName = "Vampire Hunt/Effects/Familiar Command")]
    public sealed class FamiliarCommandModuleAsset : EffectModuleAsset
    {
        [Header("行为开关")]
        [Tooltip("是否开启「围绕鼠标」。血契选中即为 true。")]
        [SerializeField] private bool pointerOrbit = true;

        [Header("指令索敌")]
        [Tooltip("按住左键时，以鼠标为圆心、这个半径内的敌人会被指派为使魔目标（米）。")]
        [SerializeField, Min(0.5f)] private float commandRadius = 3f;
        [Tooltip("指令目标的有效期（秒）：松手后多久回归「谁被打中就追谁」的被动索敌。\n" +
                 "调小 = 更听指挥、松手即散；调大 = 咬得更久。")]
        [SerializeField, Min(0.05f)] private float commandTargetLifetime = 1f;

        [Header("环绕中心跟随")]
        [Tooltip("环绕中心跟随鼠标的平滑速度：越大跟得越紧（鼠标一甩队列就到），越小越飘。")]
        [SerializeField, Min(0.1f)] private float orbitCenterFollowLerp = 6f;
        [Tooltip("环绕中心离玩家的最大距离（米）：鼠标拖太远时把队列夹在这个半径上。0 = 不限制。")]
        [SerializeField, Min(0f)] private float maxOrbitCenterDistance = 15f;

        public override IEffectModuleDescriptor ToDescriptor() =>
            new FamiliarCommandEffectDescriptor(pointerOrbit, commandRadius, commandTargetLifetime,
                orbitCenterFollowLerp, maxOrbitCenterDistance);
    }
}
