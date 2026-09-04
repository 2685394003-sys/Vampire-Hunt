using System;
using UnityEngine;
using VampireHunt.Contracts;

namespace VampireHunt.Player.Abilities.Familiar
{
    /// <summary>射击僚机使魔的武器档位：初始手枪，后续由血契升级为狙击 / 步枪 / 激光。</summary>
    public enum FamiliarWeaponId : byte
    {
        /// <summary>手枪（pistol）：初始档位，中速连发、短冷却。</summary>
        Pistol = 0,
        /// <summary>狙击（sniper）：单发高伤、长冷却、站位远。</summary>
        Sniper = 1,
        /// <summary>步枪（auto rifle）：多连发、短间隔、单发偏低。</summary>
        AutoRifle = 2,
        /// <summary>激光（laser）：持续细射流，间隔极短、射程远。</summary>
        Laser = 3,
        /// <summary>导弹（missile）：少发高伤带溅射，冷却长。</summary>
        Missile = 4
    }

    /// <summary>
    /// 射击僚机使魔某一档武器的全部参数（weapon profile）。<br/>
    /// 字段口径<b>对标玩家武器系统</b>（<c>ProjectileWeaponAbilityAsset</c>）：
    /// 伤害倍率、射程、散布、穿透、击退、元素、命中状态都一一对应，
    /// 再补上使魔专属的连发（burst）与冷却（cooldown）参数。
    /// </summary>
    [Serializable]
    public sealed class GunnerFamiliarWeaponProfile
    {
        [Header("身份")]
        [Tooltip("武器档位 id：血契升级时按这个 id 查找并切换。")]
        public FamiliarWeaponId weaponId = FamiliarWeaponId.Pistol;
        [Tooltip("显示名（仅备注用，不参与逻辑）。")]
        public string displayName = "手枪";

        [Header("伤害")]
        [Tooltip("每发子弹的伤害倍率：单发伤害 = 玩家基础伤害 × 基础伤害继承系数 × 此倍率。")]
        public float damageMultiplier = 2f;
        [Tooltip("击退倍率（× 全局击退力）。方向 = 使魔射击方向。")]
        public float knockbackMultiplier = 1f;
        [Tooltip("元素（影响状态挂载的元素归属）。")]
        public ElementId element = ElementId.None;
        [Tooltip("命中附加状态。")]
        public StatusEffectSpec[] onHitStatuses = Array.Empty<StatusEffectSpec>();
        [Tooltip("附加伤害标签（damage tags）。\n⚠️ 不要填玩家武器标签（Sniper/AutoRifle/Laser...）：那是给玩家的，填了会让玩家的命中被使魔误判成自触发而漏登记候选目标。留 None 最安全。")]
        public DamageTags extraDamageTags = DamageTags.None;

        [Header("站位与射程")]
        [Tooltip("交战距离（米）：使魔会飞到距目标这个距离的位置开火。狙击远、手枪近。")]
        public float range = 10f;
        [Tooltip("开火射线的最大长度（米）。通常 ≥ 交战距离，留出目标移动的余量。")]
        public float maxFireDistance = 14f;
        [Tooltip("枪口前移距离（米）：射线起点从使魔中心往前挪，避免起点穿进使魔自己的碰撞体。")]
        public float muzzleForwardOffset = 0.5f;

        [Header("开火节奏")]
        [Tooltip("一套（volley）打几发。手枪 2 / 步枪 4 / 狙击 1。")]
        public int burstCount = 2;
        [Tooltip("连发间隔（秒）：一套之内两发之间的间隔。")]
        public float burstInterval = 0.12f;
        [Tooltip("射击冷却（秒）：打完一整套之后的冷却，冷却结束才允许打下一套或换目标。")]
        public float fireCooldown = 0.8f;

        [Header("弹道")]
        [Tooltip("散布角（度）：每发在朝向上随机偏转的角度上限。0 = 精准。")]
        public float spreadAngle = 2f;
        [Tooltip("穿透目标数：一条射线最多结算几个敌人（1 = 只打第一个）。")]
        public int pierceCount = 1;
        [Tooltip("射线判定半径（米）：用球形扫描（sphere cast）代替细射线，宽容度更高。")]
        public float projectileRadius = 0.25f;
        [Tooltip("曳光弹视觉飞行速度（米/秒）。仅表现用，伤害是即时结算（hitscan）。\n" +
                 "调慢更容易看清弹道（35 左右很明显），调快更接近真实枪感。不影响伤害。")]
        public float projectileSpeed = 40f;
        [Tooltip("曳光弹缩放：在预制体自身尺寸上再乘一层。狙击/激光想做成细长光束就调大 Z 方向的观感（等比缩放）。\n" +
                 "最终缩放 = 预制体尺寸 × 控制器 Tracer Scale Multiplier × 此值。")]
        public float tracerScale = 1f;

        [Header("表现（可留空）")]
        [Tooltip("枪口闪光特效预制体（prefab），留空则没有。")]
        public GameObject muzzleVfxPrefab;
        [Tooltip("曳光弹预制体（prefab），留空则没有弹道表现。")]
        public GameObject tracerPrefab;

        /// <summary>单发伤害 = 玩家基础伤害 × 继承系数 × 武器倍率（由控制器调用）。</summary>
        public float ComputeDamage(float playerBaseDamage, float inheritRatio) =>
            playerBaseDamage * inheritRatio * damageMultiplier;
    }

    /// <summary>
    /// 射击僚机使魔（gunner familiar）的全部可调参数：纯数据，由 <c>GunnerFamiliarAsset</c> 组装后交给
    /// <c>GunnerFamiliarBrain</c>（单只使魔状态机）与 <c>GunnerFamiliarController</c>（多只管理 + 服务器结算）。
    /// </summary>
    /// <remarks>
    /// 与水滴撞击使魔(161)同族：<b>不主动索敌</b>，只认玩家命中过的敌人（<c>AcquireRange</c> 内）。
    /// 区别在于战斗方式——水滴是贴身穿插撞击，射手是<b>飞到交战距离外悬停开火</b>。
    /// </remarks>
    public readonly struct GunnerFamiliarDefinition
    {
        // ── 身份 ──────────────────────────────────────────────
        /// <summary>能力 id（射击使魔 162；圆型领域 160、水滴使魔 161）。</summary>
        public uint AbilityId { get; }
        /// <summary>
        /// 伤害标签（默认 Familiar）。带此标签的伤害不会被登记回候选目标，避免自触发。
        /// ⚠️ 必须保持 Familiar：这是控制器过滤「使魔自己造成的伤害」的唯一依据。
        /// </summary>
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

        // ── 索敌（与水滴使魔完全一致） ────────────────────────
        /// <summary>索敌范围（米）：玩家命中的敌人只有在此范围内才会被登记为候选目标。</summary>
        public float AcquireRange { get; }
        /// <summary>
        /// 候选目标有效期（秒）：玩家最后一次命中该敌人后的保留时间，每次新命中都会刷新。
        /// 到期即失效（<c>StopWhenPendingExpired=true</c> 时）使魔打完当前一套就收手返回。
        /// </summary>
        public float PendingTargetLifetime { get; }
        /// <summary>敌人所在层（layer mask）。</summary>
        public LayerMask TargetMask { get; }
        /// <summary>单次范围查询最多处理的目标数，防止极端饱和场景掉帧。</summary>
        public int MaxTargets { get; }

        // ── 移动 ──────────────────────────────────────────────
        /// <summary>飞行速度（米/秒）：赶往站位、维持站位都用这个速度。</summary>
        public float MoveSpeed { get; }
        /// <summary>移动平滑系数（越大跟站位跟得越紧，越小越飘）。</summary>
        public float MoveLerp { get; }
        /// <summary>
        /// 站位容差（米）：与目标的距离落在 [交战距离 ± 容差] 内即视为到位，可以开火。
        /// 太小会让使魔在阈值上来回抖动、迟迟不开火；建议 0.5~1.5。
        /// </summary>
        public float RangeTolerance { get; }
        /// <summary>
        /// 绕目标环绕的角速度（度/秒）：到位后使魔会绕着目标缓慢侧移（strafe），
        /// 多只使魔因此自然散开成包围阵型，不会挤在同一个点上。
        /// </summary>
        public float StrafeSpeed { get; }
        /// <summary>
        /// 接近超时（秒）：目标跑远或被地形卡住时，追这么久还不到位就直接开打，
        /// 防止使魔干追不打。0 = 必须严格到位才开火。
        /// </summary>
        public float MaxApproachDuration { get; }

        // ── 开火流程 ──────────────────────────────────────────
        /// <summary>瞄准前摇（秒）：到位后先锁定瞄准这么久再开第一枪，给玩家反应与视觉预告。</summary>
        public float AimDuration { get; }
        /// <summary>朝向转向目标的插值速度（越大瞄准越干脆）。</summary>
        public float AimLerp { get; }

        // ── 退出条件 ──────────────────────────────────────────
        /// <summary>
        /// 对同一目标最多打几<b>套</b>（0 = 不限，直到目标死亡 / 玩家换目标 / 候选过期）。
        /// 「套」= 一次完整的 接近 → 瞄准 → 连发 → 冷却。
        /// </summary>
        public int MaxVolleysPerTarget { get; }
        /// <summary>
        /// 玩家改变了目标（命中了另一只敌人）时是否切换。<br/>
        /// true = 在<b>冷却结束的决策点</b>让位给新目标（不会打断正在进行的那套）；<br/>
        /// false = 咬住当前目标直到它死或候选过期。
        /// </summary>
        public bool SwitchTargetOnNewCandidate { get; }
        /// <summary>候选目标过期后是否停止射击返回待机（false = 咬住目标直到它死）。</summary>
        public bool StopWhenPendingExpired { get; }

        // ── 返回 ──────────────────────────────────────────────
        /// <summary>返回轨道的速度（米/秒）。</summary>
        public float ReturnSpeed { get; }
        /// <summary>判定回到轨道的距离（米）。</summary>
        public float ReturnArriveDistance { get; }

        // ── 伤害 ──────────────────────────────────────────────
        /// <summary>基础伤害继承系数（1 = 全额继承玩家基础伤害）。</summary>
        public float BaseDamageInheritRatio { get; }
        /// <summary>
        /// buff 触发数量系数：每次触发挂载层数 = 配置层数 × 此系数（四舍五入、最少 1）。
        /// </summary>
        public float BuffTriggerCountMultiplier { get; }

        // ── 当前武器档位 ──────────────────────────────────────
        /// <summary>当前武器档位（profile）：血契升级时整体替换这个引用。</summary>
        public GunnerFamiliarWeaponProfile Weapon { get; }

        // ── 表现 ──────────────────────────────────────────────
        /// <summary>使魔视觉的整体缩放。</summary>
        public float VisualScale { get; }

        public GunnerFamiliarDefinition(
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
            float moveSpeed,
            float moveLerp,
            float rangeTolerance,
            float strafeSpeed,
            float maxApproachDuration,
            float aimDuration,
            float aimLerp,
            int maxVolleysPerTarget,
            bool switchTargetOnNewCandidate,
            bool stopWhenPendingExpired,
            float returnSpeed,
            float returnArriveDistance,
            float baseDamageInheritRatio,
            float buffTriggerCountMultiplier,
            GunnerFamiliarWeaponProfile weapon,
            float visualScale)
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
            MoveSpeed = Mathf.Max(0.1f, moveSpeed);
            MoveLerp = Mathf.Max(0.1f, moveLerp);
            RangeTolerance = Mathf.Max(0.05f, rangeTolerance);
            StrafeSpeed = strafeSpeed;
            MaxApproachDuration = Mathf.Max(0f, maxApproachDuration);
            AimDuration = Mathf.Max(0f, aimDuration);
            AimLerp = Mathf.Max(0.1f, aimLerp);
            MaxVolleysPerTarget = Mathf.Max(0, maxVolleysPerTarget);
            SwitchTargetOnNewCandidate = switchTargetOnNewCandidate;
            StopWhenPendingExpired = stopWhenPendingExpired;
            ReturnSpeed = Mathf.Max(0.1f, returnSpeed);
            ReturnArriveDistance = Mathf.Max(0.05f, returnArriveDistance);
            BaseDamageInheritRatio = Mathf.Max(0f, baseDamageInheritRatio);
            BuffTriggerCountMultiplier = Mathf.Max(0f, buffTriggerCountMultiplier);
            Weapon = weapon ?? CreateFallbackWeapon();
            VisualScale = Mathf.Max(0.01f, visualScale);
        }

        /// <summary>资产没配武器档位时的兜底：一把最朴素的手枪，保证逻辑不会空引用。</summary>
        public static GunnerFamiliarWeaponProfile CreateFallbackWeapon() => new GunnerFamiliarWeaponProfile();
    }
}
