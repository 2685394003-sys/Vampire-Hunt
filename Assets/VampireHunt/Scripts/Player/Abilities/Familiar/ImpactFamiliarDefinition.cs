using UnityEngine;
using VampireHunt.Contracts;

namespace VampireHunt.Player.Abilities.Familiar
{
    /// <summary>
    /// 撞击水滴使魔（impact familiar）的全部可调参数：纯数据结构，由 <c>ImpactFamiliarAsset</c> 组装后交给
    /// <c>ImpactFamiliarBrain</c>（单只使魔状态机）与 <c>ImpactFamiliarController</c>（多只管理 + 服务器结算）。
    /// </summary>
    /// <remarks>
    /// 使魔属于「非玩家发射型 / 其他」线路，与圆型领域(160)同族。
    /// 它**不是施法型能力**，不占用 AbilitySlot，因此不走 CombatAbilityHost 的施法链路，
    /// 而是由控制器每帧自行驱动状态机、直接构造 DamageRequest 走可信命中（trusted hit）结算。
    /// </remarks>
    /// <remarks>
    /// <b>穿插（shuttle）模型</b>：撞击目标后不停顿、不返回，而是<b>穿过目标继续飞</b>
    /// <c>OvershootDistance</c> 米 → 掉头（<c>TurnDuration</c>）→ 再次冲刺，如此来回穿插，
    /// 直到目标死亡、玩家改变目标、候选过期或达到 <c>MaxPassesPerTarget</c>。
    /// </remarks>
    public readonly struct ImpactFamiliarDefinition
    {
        // ── 身份 ──────────────────────────────────────────────
        /// <summary>能力 id（使魔默认 161；圆型领域是 160）。用于伤害来源标记与监控过滤。</summary>
        public uint AbilityId { get; }
        /// <summary>伤害标签（默认 Familiar）。带此标签的伤害不会被记回候选目标，避免自触发。</summary>
        public DamageTags WeaponTag { get; }

        // ── 待机：环绕玩家 ────────────────────────────────────
        /// <summary>环绕半径（米）。</summary>
        public float OrbitRadius { get; }
        /// <summary>环绕角速度（度/秒），正负决定旋转方向。</summary>
        public float OrbitSpeed { get; }
        /// <summary>环绕高度（米，相对玩家脚下）。</summary>
        public float OrbitHeight { get; }
        /// <summary>上下浮动幅度（米）。</summary>
        public float BobAmplitude { get; }
        /// <summary>上下浮动频率（次/秒）。</summary>
        public float BobFrequency { get; }
        /// <summary>跟随玩家的平滑系数（越大跟得越紧）。</summary>
        public float FollowLerp { get; }

        // ── 索敌 ──────────────────────────────────────────────
        /// <summary>索敌范围（米）：只有玩家命中的敌人且在此范围内才会被登记为候选目标。</summary>
        public float AcquireRange { get; }
        /// <summary>
        /// 候选目标有效期（秒）：玩家最后一次命中该敌人后的保留时间。
        /// 到期即失效（<c>StopWhenPendingExpired=true</c> 时）使魔停止穿插、返回待机 ——
        /// 这样玩家停手后使魔不会无限追着旧目标打。每次新的命中都会刷新这个计时。
        /// </summary>
        public float PendingTargetLifetime { get; }
        /// <summary>敌人所在层（layer mask）。</summary>
        public LayerMask TargetMask { get; }
        /// <summary>单次范围查询最多处理的目标数，防止极端饱和场景掉帧。</summary>
        public int MaxTargets { get; }

        // ── 冲刺（穿插的一个单程） ────────────────────────────
        /// <summary>冲刺速度（米/秒）。</summary>
        public float DashSpeed { get; }
        /// <summary>单程冲刺最长时间（秒）：超时强制掉头，防止追不上时卡死。</summary>
        public float MaxDashDuration { get; }
        /// <summary>单程冲刺最远距离（米）：超出强制掉头。需 ≥ 目标距离 + <see cref="OvershootDistance"/>。</summary>
        public float MaxDashDistance { get; }
        /// <summary>判定撞上目标的距离（米）：进入此距离即视为命中，此后锁定方向直穿出去。</summary>
        public float ArriveDistance { get; }
        /// <summary>
        /// 越过量（米）：撞上目标后沿锁定方向<b>继续飞</b>这么远才结束本单程、进入掉头。
        /// 这是「穿过目标」而非「停在脚底」的关键参数。
        /// </summary>
        public float OvershootDistance { get; }

        // ── 掉头（两个单程之间） ──────────────────────────────
        /// <summary>掉头时长（秒）：期间以 <see cref="TurnSpeedRatio"/> 的速度继续滑行并转向目标，不静止。</summary>
        public float TurnDuration { get; }
        /// <summary>掉头期间的速度 = <see cref="DashSpeed"/> × 此比例（0 = 原地掉头，1 = 全速划弧）。</summary>
        public float TurnSpeedRatio { get; }
        /// <summary>掉头期间朝向转回目标的插值速度（越大掉头越干脆）。</summary>
        public float TurnLerp { get; }
        /// <summary>对同一目标最多穿插几个单程（0 = 不限，直到目标死亡 / 玩家换目标 / 候选过期）。</summary>
        public int MaxPassesPerTarget { get; }
        /// <summary>
        /// 玩家改变了目标（命中了另一只敌人）时，是否<b>立即</b>结束当前单程去追新目标。
        /// false = 必须把当前这一整个单程飞完才允许换。
        /// </summary>
        public bool SwitchTargetOnNewCandidate { get; }
        /// <summary>候选目标过期后是否停止穿插、返回待机（false = 咬住目标直到它死）。</summary>
        public bool StopWhenPendingExpired { get; }

        // ── 撞击判定 ──────────────────────────────────────────
        /// <summary>
        /// 撞击判定半径（米）：冲刺/掉头途中用这个半径做球形查询，
        /// <b>路径上扫到的敌人一律结算伤害，无穿透次数上限</b>。
        /// </summary>
        public float ImpactRadius { get; }

        // ── 返回 ──────────────────────────────────────────────
        /// <summary>返回轨道的速度（米/秒）。</summary>
        public float ReturnSpeed { get; }
        /// <summary>判定回到轨道的距离（米）。</summary>
        public float ReturnArriveDistance { get; }

        // ── 伤害 ──────────────────────────────────────────────
        /// <summary>基础伤害继承系数（1 = 全额继承玩家基础伤害）。</summary>
        public float BaseDamageInheritRatio { get; }
        /// <summary>撞击伤害倍率（最终伤害 = 玩家基础伤害 × 继承系数 × 倍率）。</summary>
        public float DamageMultiplier { get; }
        /// <summary>击退倍率（× 全局击退力）。</summary>
        public float KnockbackMultiplier { get; }
        /// <summary>
        /// 同一敌人的重复命中间隔（秒）。<b>0 = 不设冷却</b>，即接触的每一帧都结算（伤害极高，慎用）。
        /// 这是唯一的伤害节流阀：路径上的敌人必定受伤，但不会被同一段接触每帧刷伤害。
        /// </summary>
        public float HitCooldownPerTarget { get; }
        /// <summary>元素（影响状态挂载的元素归属）。</summary>
        public ElementId Element { get; }
        /// <summary>命中附加状态。</summary>
        public StatusEffectSpec[] OnHitStatuses { get; }
        /// <summary>buff 触发数量系数：每次触发挂载层数 = 配置层数 × 此系数（四舍五入、最少 1）。</summary>
        public float BuffTriggerCountMultiplier { get; }

        // ── 表现 ──────────────────────────────────────────────
        /// <summary>水滴视觉的整体缩放。</summary>
        public float VisualScale { get; }
        /// <summary>水滴沿运动方向的拉伸倍率（模拟水滴被拉长）。</summary>
        public float StretchFactor { get; }

        public ImpactFamiliarDefinition(
            uint abilityId,
            DamageTags weaponTag,
            float orbitRadius,
            float orbitSpeed,
            float orbitHeight,
            float bobAmplitude,
            float bobFrequency,
            float followLerp,
            float acquireRange,
            float pendingTargetLifetime,
            LayerMask targetMask,
            int maxTargets,
            float dashSpeed,
            float maxDashDuration,
            float maxDashDistance,
            float arriveDistance,
            float overshootDistance,
            float turnDuration,
            float turnSpeedRatio,
            float turnLerp,
            int maxPassesPerTarget,
            bool switchTargetOnNewCandidate,
            bool stopWhenPendingExpired,
            float impactRadius,
            float returnSpeed,
            float returnArriveDistance,
            float baseDamageInheritRatio,
            float damageMultiplier,
            float knockbackMultiplier,
            float hitCooldownPerTarget,
            ElementId element,
            StatusEffectSpec[] onHitStatuses,
            float buffTriggerCountMultiplier,
            float visualScale,
            float stretchFactor)
        {
            AbilityId = abilityId;
            WeaponTag = weaponTag;
            OrbitRadius = Mathf.Max(0.1f, orbitRadius);
            OrbitSpeed = orbitSpeed;
            OrbitHeight = orbitHeight;
            BobAmplitude = Mathf.Max(0f, bobAmplitude);
            BobFrequency = Mathf.Max(0f, bobFrequency);
            FollowLerp = Mathf.Max(0.1f, followLerp);
            AcquireRange = Mathf.Max(0.5f, acquireRange);
            PendingTargetLifetime = Mathf.Max(0f, pendingTargetLifetime);
            TargetMask = targetMask;
            MaxTargets = Mathf.Max(1, maxTargets);
            DashSpeed = Mathf.Max(0.1f, dashSpeed);
            MaxDashDuration = Mathf.Max(0.05f, maxDashDuration);
            MaxDashDistance = Mathf.Max(0.5f, maxDashDistance);
            ArriveDistance = Mathf.Max(0.05f, arriveDistance);
            OvershootDistance = Mathf.Max(0.5f, overshootDistance);
            TurnDuration = Mathf.Max(0f, turnDuration);
            TurnSpeedRatio = Mathf.Clamp01(turnSpeedRatio);
            TurnLerp = Mathf.Max(0.1f, turnLerp);
            MaxPassesPerTarget = Mathf.Max(0, maxPassesPerTarget);
            SwitchTargetOnNewCandidate = switchTargetOnNewCandidate;
            StopWhenPendingExpired = stopWhenPendingExpired;
            ImpactRadius = Mathf.Max(0.05f, impactRadius);
            ReturnSpeed = Mathf.Max(0.1f, returnSpeed);
            ReturnArriveDistance = Mathf.Max(0.05f, returnArriveDistance);
            BaseDamageInheritRatio = Mathf.Max(0f, baseDamageInheritRatio);
            DamageMultiplier = Mathf.Max(0f, damageMultiplier);
            KnockbackMultiplier = Mathf.Max(0f, knockbackMultiplier);
            HitCooldownPerTarget = Mathf.Max(0f, hitCooldownPerTarget);
            Element = element;
            OnHitStatuses = onHitStatuses ?? System.Array.Empty<StatusEffectSpec>();
            BuffTriggerCountMultiplier = Mathf.Max(0f, buffTriggerCountMultiplier);
            VisualScale = Mathf.Max(0.01f, visualScale);
            StretchFactor = Mathf.Max(1f, stretchFactor);
        }
    }
}
