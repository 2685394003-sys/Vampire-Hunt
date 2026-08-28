using System;
using UnityEngine;
using VampireHunt.Boss.Encounter;

namespace VampireHunt.Infrastructure.Unity.Boss
{
    [Serializable]
    public sealed class BossStageConfigRow
    {
        [Min(1f)] public float GuardHealth = 100f;
        [Min(1f)] public float BattleHealth = 300f;
        [Min(1)] public int AbilityPhaseNumber = 1;
    }

    [CreateAssetMenu(fileName = "BossEncounter", menuName = "Vampire Hunt/Boss/Encounter Config")]
    public sealed class BossEncounterConfigAsset : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string bossName = "猩红之主";

        [Header("Three Stage Data Rows")]
        [SerializeField] private BossStageConfigRow[] stages =
        {
            new BossStageConfigRow { GuardHealth = 100f, BattleHealth = 300f, AbilityPhaseNumber = 1 },
            new BossStageConfigRow { GuardHealth = 180f, BattleHealth = 450f, AbilityPhaseNumber = 2 },
            new BossStageConfigRow { GuardHealth = 280f, BattleHealth = 650f, AbilityPhaseNumber = 3 }
        };

        [Header("Roaming")]
        [Min(1)] [SerializeField] private int roamingAbilityPhaseNumber = 4;
        [Min(0f)] [SerializeField] private float detectionRange = 18f;
        [Min(0.1f)] [SerializeField] private float evadeDistance = 9f;
        [Min(0.1f)] [SerializeField] private float desiredPlayerDistance = 7f;
        [Min(0.1f)] [SerializeField] private float normalMoveSpeed = 5f;
        [Min(0.1f)] [SerializeField] private float maxMirrorSpeed = 14f;
        [Min(0.05f)] [SerializeField] private float accelerationSeconds = 0.1f;
        [Tooltip("Boss 停下时减速到 0 所需秒数（越大停下时滑行越远）。")]
        [Min(0.05f)] [SerializeField] private float decelerationSeconds = 0.5f;
        [Tooltip("玩家距离 Boss 越近 Boss 移动越快。该半径内倍率从 1.0 升到最大值（半径外 = 1.0，与玩家普通速度一致）。")]
        [Min(1f)] [SerializeField] private float bossMoveProximityRadius = 15f;
        [Tooltip("玩家贴脸 Boss 时 Boss 移动速度的最高倍率（相对玩家普通速度）。")]
        [Min(1f)] [SerializeField] private float bossMoveProximityMaxMultiplier = 1.5f;
        [Min(0.1f)] [SerializeField] private float spawnMinDistance = 16f;
        [Min(0.1f)] [SerializeField] private float spawnMaxDistance = 32f;

        [Header("Battle Movement")]
        [Tooltip("Boss 战斗中绕玩家切向随机切换顺/逆时针的间隔（秒）。")]
        [Min(0.1f)] [SerializeField] private float battleOrbitSwitchInterval = 3f;
        [Tooltip("Boss 战斗中和玩家保持的期望距离下限（米）。")]
        [Min(1f)] [SerializeField] private float battleDesiredDistanceMin = 5f;
        [Tooltip("Boss 战斗中和玩家保持的期望距离上限（米）。")]
        [Min(1f)] [SerializeField] private float battleDesiredDistanceMax = 9f;
        [Tooltip("Boss 战斗中期望距离随机漂移的间隔（秒）。")]
        [Min(0.1f)] [SerializeField] private float battleDistanceDriftInterval = 2f;
        [Tooltip("距离弹簧系数：距离偏离期望值时拉回的强度（越小拉回越强）。")]
        [Min(0.1f)] [SerializeField] private float battleDistanceSpring = 3f;
        [Tooltip("Boss 血量 HUD 显示距离：玩家距离 Boss 超过该距离后隐藏血条。")]
        [Min(1f)] [SerializeField] private float bossHudShowDistance = 20f;
        [Tooltip("玩家停止对 Boss 造成伤害该秒数后，Boss 格挡条开始持续恢复。")]
        [Min(0f)] [SerializeField] private float guardRegenDelaySeconds = 10f;
        [Tooltip("格挡条恢复速度（每秒恢复多少点格挡）。")]
        [Min(0.1f)] [SerializeField] private float guardRegenPerSecond = 5f;

        [Header("Stagger / Execution")]
        [Min(0.1f)] [SerializeField] private float staggerTriggerDistance = 6f;
        [Min(0.1f)] [SerializeField] private float staggerEffectDuration = 3.2f;
        [Min(0.1f)] [SerializeField] private float executionWindowDuration = 3f;
        [SerializeField] private uint[] staggerAbilityIds = { 2001u, 2002u, 2003u };

        [Header("Stage Transition")]
        [Min(0.1f)] [SerializeField] private float phaseTransitionDuration = 2f;
        [SerializeField] private uint phaseAuraAbilityId = 2090u;

        [Header("Stage Clear Bonus (击破阶段加时)")]
        [Tooltip("击破各阶段时给整局 run 倒计时加的秒数；index 0/1/2 对应阶段 1/2/3。阶段3击破即通关，一般不加时。")]
        [SerializeField] private float[] stageClearBonusSeconds = { 360f, 480f, 0f };

        [Header("Frenzy Unlock")]
        [SerializeField] private uint frenzyAbilityId = 2060u;
        [SerializeField] private uint requiredFrenzyPactId = 200u;
        [Range(0.01f, 0.99f)] [SerializeField] private float frenzyHealthThreshold = 0.2f;

        [Header("Server Validation")]
        [Min(1f)] [SerializeField] private float maxTrustedHitDamage = 10000f;

        public string BossName => bossName;
        public int RoamingAbilityPhaseNumber => roamingAbilityPhaseNumber;
        public float DetectionRange => detectionRange;
        public float EvadeDistance => evadeDistance;
        public float DesiredPlayerDistance => desiredPlayerDistance;
        public float NormalMoveSpeed => normalMoveSpeed;
        public float MaxMirrorSpeed => maxMirrorSpeed;
        /// <summary>Boss 起步加速到目标速度所需秒数（越小起步越灵敏）。</summary>
        public float AccelerationSeconds => accelerationSeconds;
        /// <summary>Boss 停下时减速到 0 所需秒数（越大滑行越远）。</summary>
        public float DecelerationSeconds => decelerationSeconds;
        /// <summary>玩家距离 Boss 越近 Boss 越快，该半径内倍率从 1.0 升到最大值。</summary>
        public float BossMoveProximityRadius => bossMoveProximityRadius;
        /// <summary>玩家贴脸 Boss 时 Boss 移动速度的最高倍率。</summary>
        public float BossMoveProximityMaxMultiplier => bossMoveProximityMaxMultiplier;
        public float BattleOrbitSwitchInterval => battleOrbitSwitchInterval;
        public float BattleDesiredDistanceMin => battleDesiredDistanceMin;
        public float BattleDesiredDistanceMax => battleDesiredDistanceMax;
        public float BattleDistanceDriftInterval => battleDistanceDriftInterval;
        public float BattleDistanceSpring => battleDistanceSpring;
        /// <summary>Boss 血量 HUD 显示距离（超过则隐藏血条）。</summary>
        public float BossHudShowDistance => bossHudShowDistance;
        /// <summary>玩家停止伤害该秒数后，Boss 格挡条开始持续恢复。</summary>
        public float GuardRegenDelaySeconds => guardRegenDelaySeconds;
        /// <summary>格挡条恢复速度（点/秒）。</summary>
        public float GuardRegenPerSecond => guardRegenPerSecond;
        public float SpawnMinDistance => spawnMinDistance;
        public float SpawnMaxDistance => spawnMaxDistance;
        public float StaggerEffectDuration => staggerEffectDuration;
        public float ExecutionWindowDuration => executionWindowDuration;
        public float PhaseTransitionDuration => phaseTransitionDuration;
        public uint PhaseAuraAbilityId => phaseAuraAbilityId;
        /// <summary>击破指定阶段时给整局 run 倒计时加的秒数（阶段 1=360s/6min，阶段 2=480s/8min，阶段 3=0）。</summary>
        public float GetStageClearBonusSeconds(int stageNumber)
        {
            // 已保存的旧配置资产可能没有该数组（反序列化为 null）→ 回退到设计默认值，保证加时机制始终生效
            var arr = stageClearBonusSeconds ?? new float[] { 360f, 480f, 0f };
            int index = Mathf.Clamp(stageNumber - 1, 0, arr.Length - 1);
            return arr[index];
        }
        public uint FrenzyAbilityId => frenzyAbilityId;
        public uint RequiredFrenzyPactId => requiredFrenzyPactId;
        public float FrenzyHealthThreshold => frenzyHealthThreshold;
        public float MaxTrustedHitDamage => maxTrustedHitDamage;
        public int StageCount => stages?.Length ?? 0;

        public int GetAbilityPhaseNumber(int stageNumber)
        {
            int index = Mathf.Clamp(stageNumber - 1, 0, stages.Length - 1);
            return stages[index].AbilityPhaseNumber;
        }

        public uint GetStaggerAbilityId(uint randomSeed)
        {
            if (staggerAbilityIds == null || staggerAbilityIds.Length == 0) return 0;
            return staggerAbilityIds[randomSeed % (uint)staggerAbilityIds.Length];
        }

        public BossEncounterRules CreateRules()
        {
            if (stages == null || stages.Length != 3)
                throw new InvalidOperationException("Boss Encounter Config must contain exactly three stage rows.");
            var rows = new BossStageRules[3];
            for (int i = 0; i < rows.Length; i++)
            {
                BossStageConfigRow row = stages[i] ?? new BossStageConfigRow();
                rows[i] = new BossStageRules(row.GuardHealth, row.BattleHealth, row.AbilityPhaseNumber);
            }
            return new BossEncounterRules(rows, staggerTriggerDistance);
        }

        private void OnValidate()
        {
            if (stages == null || stages.Length != 3)
                Array.Resize(ref stages, 3);
            for (int i = 0; i < stages.Length; i++) stages[i] ??= new BossStageConfigRow();
            roamingAbilityPhaseNumber = Mathf.Max(1, roamingAbilityPhaseNumber);
            detectionRange = Mathf.Max(0f, detectionRange);
            evadeDistance = Mathf.Max(0.1f, evadeDistance);
            desiredPlayerDistance = Mathf.Max(0.1f, desiredPlayerDistance);
            normalMoveSpeed = Mathf.Max(0.1f, normalMoveSpeed);
            maxMirrorSpeed = Mathf.Max(normalMoveSpeed, maxMirrorSpeed);
            accelerationSeconds = Mathf.Max(0.05f, accelerationSeconds);
            decelerationSeconds = Mathf.Max(0.05f, decelerationSeconds);
            bossMoveProximityRadius = Mathf.Max(1f, bossMoveProximityRadius);
            bossMoveProximityMaxMultiplier = Mathf.Max(1f, bossMoveProximityMaxMultiplier);
            battleOrbitSwitchInterval = Mathf.Max(0.1f, battleOrbitSwitchInterval);
            battleDesiredDistanceMin = Mathf.Max(1f, battleDesiredDistanceMin);
            battleDesiredDistanceMax = Mathf.Max(battleDesiredDistanceMin, battleDesiredDistanceMax);
            battleDistanceDriftInterval = Mathf.Max(0.1f, battleDistanceDriftInterval);
            battleDistanceSpring = Mathf.Max(0.1f, battleDistanceSpring);
            bossHudShowDistance = Mathf.Max(1f, bossHudShowDistance);
            guardRegenDelaySeconds = Mathf.Max(0f, guardRegenDelaySeconds);
            guardRegenPerSecond = Mathf.Max(0.1f, guardRegenPerSecond);
            spawnMinDistance = Mathf.Max(0.1f, spawnMinDistance);
            spawnMaxDistance = Mathf.Max(spawnMinDistance, spawnMaxDistance);
            staggerTriggerDistance = Mathf.Max(0.1f, staggerTriggerDistance);
            staggerEffectDuration = Mathf.Max(0.1f, staggerEffectDuration);
            executionWindowDuration = Mathf.Max(0.1f, executionWindowDuration);
            phaseTransitionDuration = Mathf.Max(0.1f, phaseTransitionDuration);
            stageClearBonusSeconds ??= new float[] { 360f, 480f, 0f };
            if (stageClearBonusSeconds.Length != 3) Array.Resize(ref stageClearBonusSeconds, 3);
            frenzyHealthThreshold = Mathf.Clamp(frenzyHealthThreshold, 0.01f, 0.99f);
            maxTrustedHitDamage = Mathf.Max(1f, maxTrustedHitDamage);
            staggerAbilityIds ??= Array.Empty<uint>();
        }
    }
}
