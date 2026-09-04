using System;
using System.Collections.Generic;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Player.Abilities.Familiar;

namespace VampireHunt.Infrastructure.Unity
{
    /// <summary>
    /// 射击僚机使魔（gunner familiar）的配置资产：Inspector 上的全部可调槽位，
    /// 组装成 <see cref="GunnerFamiliarDefinition"/> 交给控制器使用。
    /// </summary>
    /// <remarks>
    /// <b>武器档位</b>：<c>weaponProfiles</c> 里配好手枪 / 狙击 / 步枪 / 激光等档位，
    /// 血契升级时调用控制器的 <c>SetWeapon(FamiliarWeaponId)</c> 整体切换。
    /// 字段口径对标玩家武器（<c>ProjectileWeaponAbilityAsset</c>）：伤害倍率、射程、散布、穿透、击退、元素、命中状态。
    /// </remarks>
    [CreateAssetMenu(menuName = "Vampire Hunt/Combat/Gunner Familiar", fileName = "GunnerFamiliar")]
    public sealed class GunnerFamiliarAsset : ScriptableObject
    {
        [Header("身份")]
        [Tooltip("能力 id（射击使魔默认 162；圆型领域 160、水滴使魔 161）。用于伤害来源标记与监控过滤。")]
        [SerializeField] private uint abilityId = 162;
        [Tooltip("伤害标签。必须保持 Familiar：这是过滤「使魔自己造成的伤害」的唯一依据，改了会导致自触发循环。")]
        [SerializeField] private DamageTags weaponTag = DamageTags.Familiar;

        [Header("待机：环绕玩家")]
        [Tooltip("环绕半径（米）。")]
        [SerializeField, Min(0.1f)] private float orbitRadius = 2.6f;
        [Tooltip("环绕角速度（度/秒），正负决定旋转方向。")]
        [SerializeField] private float orbitSpeed = 110f;
        [Tooltip("环绕高度（米，相对玩家脚下）。")]
        [SerializeField] private float orbitHeight = 1.4f;
        [Tooltip("上下浮动幅度（米）。")]
        [SerializeField, Min(0f)] private float bobAmplitude = 0.12f;
        [Tooltip("上下浮动频率（次/秒）。")]
        [SerializeField, Min(0f)] private float bobFrequency = 1.2f;
        [Tooltip("跟随玩家的平滑系数（越大跟得越紧）。")]
        [SerializeField, Min(0.1f)] private float followLerp = 10f;

        [Header("索敌（仅记录玩家命中过的敌人）")]
        [Tooltip("索敌范围（米）：玩家命中的敌人只有在此范围内才会被登记为候选目标。")]
        [SerializeField, Min(0.5f)] private float acquireRange = 30f;
        [Tooltip("候选目标有效期（秒）：玩家最后一次命中该敌人后的保留时间，每次新命中都会刷新。")]
        [SerializeField, Min(0f)] private float pendingTargetLifetime = 3f;
        [Tooltip("敌人所在层（layer mask）。")]
        [SerializeField] private LayerMask targetMask = 1 << 8;
        [Tooltip("单次范围查询最多处理的目标数，防止极端饱和场景掉帧。")]
        [SerializeField, Min(1)] private int maxTargets = 32;

        [Header("移动")]
        [Tooltip("飞行速度（米/秒）：赶往站位、维持站位都用这个速度。")]
        [SerializeField, Min(0.1f)] private float moveSpeed = 12f;
        [Tooltip("移动平滑系数（越大跟站位跟得越紧，越小越飘）。")]
        [SerializeField, Min(0.1f)] private float moveLerp = 4f;
        [Tooltip("站位容差（米）：与目标距离落在 [交战距离 ± 容差] 内即到位开火。太小会抖动不开火，建议 0.5~1.5。")]
        [SerializeField, Min(0.05f)] private float rangeTolerance = 1f;
        [Tooltip("绕目标环绕的角速度（度/秒）：到位后绕目标缓慢侧移，多只使魔因此自然散开成包围阵型。")]
        [SerializeField] private float strafeSpeed = 25f;
        [Tooltip("接近超时（秒）：追这么久还不到位就直接开打，防止干追不打。0 = 必须严格到位。")]
        [SerializeField, Min(0f)] private float maxApproachDuration = 2.5f;

        [Header("开火流程")]
        [Tooltip("瞄准前摇（秒）：到位后先锁定瞄准这么久再开第一枪，给玩家反应与视觉预告。")]
        [SerializeField, Min(0f)] private float aimDuration = 0.25f;
        [Tooltip("朝向转向目标的插值速度（越大瞄准越干脆）。")]
        [SerializeField, Min(0.1f)] private float aimLerp = 10f;

        [Header("退出条件")]
        [Tooltip("对同一目标最多打几套（0 = 不限）。一套 = 接近 → 瞄准 → 连发 → 冷却。")]
        [SerializeField, Min(0)] private int maxVolleysPerTarget = 0;
        [Tooltip("玩家命中了另一只敌人时是否切换：true = 在当前这套打完后的冷却结束点让位给新目标，绝不打断进行中的射击。")]
        [SerializeField] private bool switchTargetOnNewCandidate = true;
        [Tooltip("候选目标过期后是否停止射击返回待机（false = 咬住目标直到它死）。")]
        [SerializeField] private bool stopWhenPendingExpired = true;

        [Header("返回")]
        [Tooltip("返回轨道的速度（米/秒）。")]
        [SerializeField, Min(0.1f)] private float returnSpeed = 12f;
        [Tooltip("判定回到轨道的距离（米）。")]
        [SerializeField, Min(0.05f)] private float returnArriveDistance = 0.3f;

        [Header("伤害")]
        [Tooltip("基础伤害继承系数（1 = 全额继承玩家基础伤害）。")]
        [SerializeField, Min(0f)] private float baseDamageInheritRatio = 1f;
        [Tooltip("buff 触发数量系数：每次触发挂载层数 = 配置层数 × 此系数（四舍五入、最少 1）。")]
        [SerializeField, Min(0f)] private float buffTriggerCountMultiplier = 1f;

        [Header("武器档位")]
        [Tooltip("开局使用的武器档位。血契升级时按 id 在这里查找并整体切换。")]
        [SerializeField] private FamiliarWeaponId defaultWeaponId = FamiliarWeaponId.Pistol;
        [Tooltip("所有武器档位。至少要配一个与「开局档位」同 id 的档位，否则会退回内置手枪默认值。")]
        [SerializeField] private GunnerFamiliarWeaponProfile[] weaponProfiles = DefaultProfiles();

        [Header("表现")]
        [Tooltip("使魔视觉的整体缩放。")]
        [SerializeField, Min(0.01f)] private float visualScale = 1f;

        /// <summary>开局武器档位 id。</summary>
        public FamiliarWeaponId DefaultWeaponId => defaultWeaponId;
        /// <summary>全部武器档位（只读，用于血契升级时查找）。</summary>
        public IReadOnlyList<GunnerFamiliarWeaponProfile> WeaponProfiles => weaponProfiles;

        /// <summary>按 id 查找武器档位，找不到返回 null。</summary>
        public GunnerFamiliarWeaponProfile FindWeapon(FamiliarWeaponId id)
        {
            if (weaponProfiles == null) return null;
            for (int i = 0; i < weaponProfiles.Length; i++)
            {
                GunnerFamiliarWeaponProfile profile = weaponProfiles[i];
                if (profile != null && profile.weaponId == id) return profile;
            }
            return null;
        }

        /// <summary>组装成运行时使用的纯数据定义（使用开局武器档位）。</summary>
        public GunnerFamiliarDefinition CreateDefinition() => CreateDefinition(defaultWeaponId);

        /// <summary>组装成运行时使用的纯数据定义（指定武器档位）。</summary>
        public GunnerFamiliarDefinition CreateDefinition(FamiliarWeaponId weaponId)
        {
            return new GunnerFamiliarDefinition(
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
                moveSpeed,
                moveLerp,
                rangeTolerance,
                strafeSpeed,
                maxApproachDuration,
                aimDuration,
                aimLerp,
                maxVolleysPerTarget,
                switchTargetOnNewCandidate,
                stopWhenPendingExpired,
                returnSpeed,
                returnArriveDistance,
                baseDamageInheritRatio,
                buffTriggerCountMultiplier,
                CloneWeapon(FindWeapon(weaponId)),
                visualScale);
        }

        /// <summary>
        /// 拷贝一份武器档位：Definition 里存拷贝而非资产引用，
        /// 这样血契运行期改倍率不会写回资产（改档位走 SetWeapon 换引用）。
        /// </summary>
        private static GunnerFamiliarWeaponProfile CloneWeapon(GunnerFamiliarWeaponProfile source)
        {
            if (source == null) return new GunnerFamiliarWeaponProfile();
            var copy = new GunnerFamiliarWeaponProfile
            {
                weaponId = source.weaponId,
                displayName = source.displayName,
                damageMultiplier = source.damageMultiplier,
                knockbackMultiplier = source.knockbackMultiplier,
                element = source.element,
                onHitStatuses = source.onHitStatuses != null
                    ? (StatusEffectSpec[])source.onHitStatuses.Clone()
                    : Array.Empty<StatusEffectSpec>(),
                extraDamageTags = source.extraDamageTags,
                range = source.range,
                maxFireDistance = source.maxFireDistance,
                muzzleForwardOffset = source.muzzleForwardOffset,
                burstCount = source.burstCount,
                burstInterval = source.burstInterval,
                fireCooldown = source.fireCooldown,
                spreadAngle = source.spreadAngle,
                pierceCount = source.pierceCount,
                projectileRadius = source.projectileRadius,
                projectileSpeed = source.projectileSpeed,
                tracerScale = source.tracerScale,
                muzzleVfxPrefab = source.muzzleVfxPrefab,
                tracerPrefab = source.tracerPrefab
            };
            return copy;
        }

        /// <summary>四档基础武器：手枪（初始）/ 狙击 / 步枪 / 激光。数值可按实机测试在 Inspector 里改。</summary>
        private static GunnerFamiliarWeaponProfile[] DefaultProfiles()
        {
            return new[]
            {
                // 手枪：中规中矩的初始档，2 连发、0.8 秒冷却。
                new GunnerFamiliarWeaponProfile
                {
                    weaponId = FamiliarWeaponId.Pistol, displayName = "手枪",
                    damageMultiplier = 2f, burstCount = 2, burstInterval = 0.12f, fireCooldown = 0.8f,
                    range = 10f, maxFireDistance = 14f, spreadAngle = 2f, pierceCount = 1
                },
                // 狙击：单发高伤、站位远、冷却长。
                new GunnerFamiliarWeaponProfile
                {
                    weaponId = FamiliarWeaponId.Sniper, displayName = "狙击",
                    damageMultiplier = 4.5f, burstCount = 1, burstInterval = 0f, fireCooldown = 1.8f,
                    range = 16f, maxFireDistance = 22f, spreadAngle = 0f, pierceCount = 999
                },
                // 步枪：4 连发、单发偏低、冷却短，靠弹量堆输出。
                new GunnerFamiliarWeaponProfile
                {
                    weaponId = FamiliarWeaponId.AutoRifle, displayName = "步枪",
                    damageMultiplier = 0.9f, burstCount = 4, burstInterval = 0.09f, fireCooldown = 0.7f,
                    range = 9f, maxFireDistance = 13f, spreadAngle = 5f, pierceCount = 5
                },
                // 激光：6 连发的细射流、间隔极短、站位最远。
                new GunnerFamiliarWeaponProfile
                {
                    weaponId = FamiliarWeaponId.Laser, displayName = "激光",
                    damageMultiplier = 0.7f, burstCount = 6, burstInterval = 0.06f, fireCooldown = 0.6f,
                    range = 12f, maxFireDistance = 18f, spreadAngle = 0.5f, pierceCount = 10
                }
            };
        }
    }
}
