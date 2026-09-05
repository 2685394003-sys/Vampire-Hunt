using System;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Player.Abilities.Familiar;

namespace VampireHunt.Infrastructure.Unity
{
    /// <summary>
    /// 撞击水滴使魔（impact familiar）的配置资产：Inspector 上的全部可调槽位，
    /// 组装成 <see cref="ImpactFamiliarDefinition"/> 交给控制器使用。
    /// </summary>
    [CreateAssetMenu(menuName = "Vampire Hunt/Combat/Impact Familiar", fileName = "ImpactFamiliar")]
    public sealed class ImpactFamiliarAsset : ScriptableObject
    {
        [Serializable]
        private sealed class StatusEntry
        {
            public uint statusId;
            [Min(0f)] public float stacks = 1f;
            [Min(0f)] public float duration;
            [Min(0f)] public float magnitude;
            public ElementId element = ElementId.None;
            [Min(0f)] public float elementMastery = 1f;

            public StatusEffectSpec ToSpec() =>
                new StatusEffectSpec(statusId, stacks, duration, magnitude, element, elementMastery);
        }

        [Header("身份")]
        [Tooltip("能力 id（使魔默认 161；圆型领域是 160）。用于伤害来源标记与监控过滤。")]
        [SerializeField] private uint abilityId = 161;
        [Tooltip("伤害标签。带 Familiar 标签的伤害不会被记回候选目标，避免使魔自触发。")]
        [SerializeField] private DamageTags weaponTag = DamageTags.Familiar;

        [Header("待机：环绕玩家")]
        [Tooltip("环绕半径（米）。")]
        [SerializeField, Min(0.1f)] private float orbitRadius = 2.2f;
        [Tooltip("环绕角速度（度/秒），正负决定旋转方向。")]
        [SerializeField] private float orbitSpeed = 120f;
        [Tooltip("环绕高度（米，相对玩家脚下）。")]
        [SerializeField] private float orbitHeight = 1.2f;
        [Tooltip("上下浮动幅度（米）。")]
        [SerializeField, Min(0f)] private float bobAmplitude = 0.15f;
        [Tooltip("上下浮动频率（次/秒）。")]
        [SerializeField, Min(0f)] private float bobFrequency = 1.5f;
        [Tooltip("跟随玩家的平滑系数（越大跟得越紧）。")]
        [SerializeField, Min(0.1f)] private float followLerp = 12f;

        [Header("索敌（仅记录玩家命中过的敌人）")]
        [Tooltip("索敌范围（米）：玩家命中的敌人只有在此范围内才会被登记为候选目标。")]
        [SerializeField, Min(0.5f)] private float acquireRange = 30f;
        [Tooltip("候选目标有效期（秒）：玩家最后一次命中该敌人后的保留时间，到期失效、使魔返回待机。")]
        [SerializeField, Min(0f)] private float pendingTargetLifetime = 3f;
        [Tooltip("敌人所在层（layer mask）。")]
        [SerializeField] private LayerMask targetMask = 1 << 8;
        [Tooltip("单次范围查询最多处理的目标数，防止极端饱和场景掉帧。")]
        [SerializeField, Min(1)] private int maxTargets = 32;

        [Header("冲刺（穿插的一个单程）")]
        [Tooltip("冲刺速度（米/秒）。")]
        [SerializeField, Min(0.1f)] private float dashSpeed = 22f;
        [Tooltip("单程冲刺最长时间（秒）：「还没撞上目标」时的兜底，超时掉头，防止追不上时空转。")]
        [SerializeField, Min(0.05f)] private float maxDashDuration = 2.5f;
        [Tooltip("单程冲刺最远距离（米）：「还没撞上目标」时的兜底。需 ≥ 目标距离 + 越过量。")]
        [SerializeField, Min(0.5f)] private float maxDashDistance = 40f;
        [Tooltip("判定撞上目标的水平距离（米）：只比 XZ 平面，不比高度，避免高个子 Boss 判不到。")]
        [SerializeField, Min(0.05f)] private float arriveDistance = 1.2f;
        [Tooltip("越过量（米）：撞上目标后沿锁定方向继续飞这么远才掉头。这是「穿过目标」而不是「停在脚底」的关键。")]
        [SerializeField, Min(0.5f)] private float overshootDistance = 4f;

        [Header("掉头（两个单程之间）")]
        [Tooltip("掉头时长（秒）：期间继续滑行并转向目标，不会静止停住。")]
        [SerializeField, Min(0f)] private float turnDuration = 0.25f;
        [Tooltip("掉头期间的速度 = 冲刺速度 × 此比例（0 = 原地掉头，1 = 全速划弧）。")]
        [SerializeField, Range(0f, 1f)] private float turnSpeedRatio = 0.4f;
        [Tooltip("掉头期间朝向转回目标的插值速度（越大掉头越干脆）。")]
        [SerializeField, Min(0.1f)] private float turnLerp = 12f;
        [Tooltip("对同一目标最多穿插几个单程（0 = 不限，直到目标死亡 / 玩家换目标 / 候选过期）。")]
        [SerializeField, Min(0)] private int maxPassesPerTarget = 0;
        [Tooltip("玩家命中了另一只敌人时，是否立刻结束当前单程去追新目标。false = 必须把当前单程飞完才换。")]
        [SerializeField] private bool switchTargetOnNewCandidate = true;
        [Tooltip("候选目标过期后是否停止穿插返回待机（false = 咬住目标直到它死）。")]
        [SerializeField] private bool stopWhenPendingExpired = true;

        [Header("撞击判定")]
        [Tooltip("撞击判定半径（米）：冲刺/掉头途中用这个半径做球形查询。无穿透次数上限，路径上的敌人一律结算。")]
        [SerializeField, Min(0.05f)] private float impactRadius = 0.9f;

        [Header("返回")]
        [Tooltip("返回轨道的速度（米/秒）。")]
        [SerializeField, Min(0.1f)] private float returnSpeed = 14f;
        [Tooltip("判定回到轨道的距离（米）。")]
        [SerializeField, Min(0.05f)] private float returnArriveDistance = 0.25f;

        [Header("伤害")]
        [Tooltip("基础伤害继承系数（1 = 全额继承玩家基础伤害）。")]
        [SerializeField, Min(0f)] private float baseDamageInheritRatio = 1f;
        [Tooltip("撞击伤害倍率：最终伤害 = 玩家基础伤害 × 继承系数 × 倍率。")]
        [SerializeField, Min(0f)] private float damageMultiplier = 1.5f;
        [Tooltip("击退倍率（× 全局击退力）。")]
        [SerializeField, Min(0f)] private float knockbackMultiplier = 1f;
        [Tooltip("同一敌人的重复命中间隔（秒）。0 = 不设冷却，接触的每一帧都结算（伤害极高，慎用）。这是唯一的伤害节流阀。")]
        [SerializeField, Min(0f)] private float hitCooldownPerTarget = 0.2f;
        [Tooltip("元素（影响状态挂载的元素归属）。")]
        [SerializeField] private ElementId element = ElementId.None;
        [Tooltip("命中附加状态。")]
        [SerializeField] private StatusEntry[] onHitStatuses = Array.Empty<StatusEntry>();
        [Tooltip("buff 触发数量系数：每次触发挂载层数 = 配置层数 × 此系数（四舍五入、最少 1）。")]
        [SerializeField, Min(0f)] private float buffTriggerCountMultiplier = 1f;

        [Header("表现")]
        [Tooltip("水滴视觉的整体缩放。")]
        [SerializeField, Min(0.01f)] private float visualScale = 1f;
        [Tooltip("水滴沿运动方向的拉伸倍率（模拟水滴被拉长）。")]
        [SerializeField, Min(1f)] private float stretchFactor = 1.6f;

        /// <summary>组装成运行时使用的纯数据定义。</summary>
        public ImpactFamiliarDefinition CreateDefinition()
        {
            var statusSpecs = new StatusEffectSpec[onHitStatuses?.Length ?? 0];
            for (int i = 0; i < statusSpecs.Length; i++)
                statusSpecs[i] = onHitStatuses[i] != null ? onHitStatuses[i].ToSpec() : default;

            return new ImpactFamiliarDefinition(
                abilityId,
                weaponTag,
                orbitRadius,
                orbitSpeed,
                orbitHeight,
                bobAmplitude,
                bobFrequency,
                followLerp,
                acquireRange,
                pendingTargetLifetime,
                targetMask,
                maxTargets,
                dashSpeed,
                maxDashDuration,
                maxDashDistance,
                arriveDistance,
                overshootDistance,
                turnDuration,
                turnSpeedRatio,
                turnLerp,
                maxPassesPerTarget,
                switchTargetOnNewCandidate,
                stopWhenPendingExpired,
                impactRadius,
                returnSpeed,
                returnArriveDistance,
                baseDamageInheritRatio,
                damageMultiplier,
                knockbackMultiplier,
                hitCooldownPerTarget,
                element,
                statusSpecs,
                buffTriggerCountMultiplier,
                visualScale,
                stretchFactor);
        }
    }
}
