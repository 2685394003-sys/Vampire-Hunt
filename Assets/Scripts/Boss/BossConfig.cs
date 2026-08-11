using UnityEngine;

[DisallowMultipleComponent]
public sealed class BossConfig : MonoBehaviour
{
    [Header("Boss - 基础生命 / Base Health")]
    [Min(1)] public int maxHealth = 100;
    [Range(0.01f, 0.99f)] public float phase1HealthRate = 0.70f;
    [Range(0.01f, 0.99f)] public float phase2HealthRate = 0.30f;
    [Range(0.01f, 0.99f)] public float phase3HealthRate = 0.10f;
    public bool invulnerableDuringPhaseChange = true;
    [Min(0f)] public float phaseChangeDuration = 1.5f;
    [Min(0.02f)] public float phaseBlinkInterval = 0.12f;
    public bool teleportAfterPhaseChange;
    public bool stationaryAfterFirstPhase = true;
    [Range(0.01f, 1f)] public float phaseSlowMotionScale = 0.25f;
    [Min(0f)] public float phaseSlowMotionRealtime = 0.45f;
    [Min(0f)] public float phaseTransitionKnockback = 7f;
    [Min(0f)] public float deathDisableDelay = 2f;

    [Header("Boss - 出生与战斗区域 / Spawn & Combat Area")]
    public bool randomSpawnOnStart;
    public Vector3 arenaCenter = Vector3.zero;
    public Vector2 arenaHalfSize = new(16f, 16f);
    [Min(0f)] public float spawnMinDistanceFromPlayer = 8f;
    [Min(1)] public int spawnPositionAttempts = 24;
    [Min(0f)] public float spawnObstacleCheckRadius = 0.8f;
    public LayerMask obstacleLayer;
    public bool clearObstaclesOnPhaseChange;
    [Min(0f)] public float phaseClearObstacleRadius = 4f;

    [Header("Boss - 移动阶段 / Movement Phases")]
    [Min(0f)] public float initialActionDelay = 1.5f;
    [Min(0f)] public float moveSpeed = 2.4f;
    [Min(0f)] public float phase1MoveMultiplier = 1.05f;
    [Min(0f)] public float phase2MoveMultiplier = 1.18f;
    [Min(0f)] public float phase3MoveMultiplier = 1.35f;
    [Min(0f)] public float stoppingDistance = 2.6f;
    [Min(0f)] public float turnSpeed = 10f;
    [Range(0f, 0.5f)] public float viewportPadding = 0.05f;
    [Min(0f)] public float offscreenAttackInterval = 2.5f;
    [Min(0f)] public float globalAttackInterval = 0.35f;

    [Header("Boss - 玩家与表现 / Player & Presentation")]
    public LayerMask playerLayer = 1 << 6;
    public bool createDebugVisualIfMissing = true;
    public Color debugBossColor = new(0.55f, 0.03f, 0.08f, 1f);
    public Color warningColor = new(1f, 0.08f, 0.08f, 0.9f);
    public Color projectileColor = new(0.65f, 0.02f, 0.12f, 1f);
    [Min(0f)] public float telegraphHeight = 0.05f;
    [Min(0.01f)] public float telegraphLineWidth = 0.1f;
    public Material telegraphMaterial;
    public Material projectileMaterial;
    public GameObject projectilePrefab;
    public AudioClip phaseChangeClip;
    public AudioClip attackClip;
    public AudioClip deathClip;
    [Tooltip("所有技能与预警的世界坐标 Y 下限。-0.99 可确保严格满足 Y > -1。")]
    public float minimumEffectHeight = -0.99f;

    [Header("Boss - 左右护卫")]
    [Min(1)] public int guardMaxHealth = 25;
    [Min(0f)] public float guardHitFlashDuration = 0.08f;
    [Range(0f, 1f)] public float brokenGuardAlpha = 0.18f;
    public bool guardIndependentMovement = true;
    [Min(0.1f)] public float guardFormationDistance = 2f;
    [Min(0f)] public float guardFormationHeight = 0.6f;
    [Min(0.1f)] public float guardFollowSpeed = 4.5f;

    [Header("Boss - 转阶段环境")]
    public bool createPhaseBloodPool = true;
    [Min(0.1f)] public float bloodPoolRadius = 1.4f;
    [Min(0f)] public float bloodPoolSpawnDistance = 4f;
    [Min(0)] public int phaseRewardDamage = 1;
    [Min(0)] public int phaseRewardSpeed = 1;
    [Min(0)] public int phaseRewardMaxHealth = 10;
    public bool createPhase3Rain = true;

    [Header("Animator 参数 / Animator Params (must match Controller exactly)")]
    public string phaseParameter = "Phase";
    public string phaseChangeTrigger = "PhaseChange";
    public string deathTrigger = "Death";
    public string format1Trigger = "Format1";
    public string format2Trigger = "Format2";
    public string format3Trigger = "Format3";
    public string format4Trigger = "Format4";
    public string format5Trigger = "Format5";
    public string format6Trigger = "Format6";

    [Header("运行时调试 / Runtime Debug")]
    public bool showDebugPanel = true;
    public bool logCombatEvents = true;
    public bool drawCombatGizmos = true;
    [Min(1)] public int debugDamageAmount = 10;
    public Vector2 debugPanelPosition = new(12f, 12f);

    [Header("格式1 - 双护卫渐进横扫 / Progressive Guard Sweep")]
    [Min(0)] public int format1Damage = 1;
    [Min(0f)] public float format1WarningTime = 0.9f;
    [Min(0.1f)] public float format1Radius = 1.8f;
    [Min(0.5f)] public float format1SweepLength = 7f;
    [Min(0.1f)] public float format1SweepWidth = 1.5f;
    [Min(2)] public int format1SweepSteps = 10;
    [Min(0f)] public float format1SweepStepInterval = 0.05f;
    [Min(0f)] public float format1Knockback = 4f;
    [Min(0f)] public float format1Cooldown = 3.5f;
    [Min(0f)] public float format1Weight = 1f;

    [Header("格式2 - 旋转迷宫弹幕 / Rotating Maze Barrage")]
    [Min(0)] public int format2Damage = 1;
    [Min(1)] public int format2ProjectileCount = 10;
    [Min(1)] public int format2ProjectileArms = 4;
    public float format2RotationPerWave = 17f;
    [Min(0f)] public float format2PreDelay = 0.25f;
    [Min(0f)] public float format2ProjectileInterval = 0.16f;
    [Min(0f)] public float format2ProjectileSpeed = 7f;
    [Min(0.02f)] public float format2ProjectileRadius = 0.22f;
    [Min(0f)] public float format2ProjectileLife = 6f;
    [Min(0f)] public float format2Knockback = 2.5f;
    [Min(0f)] public float format2Cooldown = 2.8f;
    [Min(0f)] public float format2Weight = 1.25f;

    [Header("格式3 - 十字切割网格(二阶段新增) / Cross Slash Grid (Phase 2)")]
    [Min(0)] public int format3Damage = 1;
    [Min(0f)] public float format3WarningTime = 1.1f;
    [Min(0.1f)] public float format3Radius = 7f;
    [Min(0.1f)] public float format3HalfLength = 7f;
    [Min(0.1f)] public float format3Width = 1.15f;
    [Range(0.05f, 1f)] public float format3FillAlpha = 0.55f;
    [Min(0f)] public float format3Knockback = 0f;
    [Min(0f)] public float format3Cooldown = 5f;
    [Min(0f)] public float format3Weight = 0.8f;

    [Header("格式4 - 蓄力全屏斩击(一阶段解锁) / Charged Full-screen Slash (Phase 1)")]
    [Min(0)] public int format4Damage = 1;
    [Min(0f)] public float format4WarningTime = 1.6f;
    [Min(0.1f)] public float format4Radius = 12f;
    [Min(0.1f)] public float format4Length = 12f;
    [Min(0.1f)] public float format4Width = 3.5f;
    [Min(0f)] public float format4Knockback = 7f;
    [Min(0f)] public float format4Cooldown = 10.5f;
    [Min(0f)] public float format4Weight = 0.45f;

    [Header("格式5 - 契约倒计时加速(仅触发一次) / Contract Countdown Acceleration (one-time)")]
    public bool enableFormat5Countdown = true;
    [Range(0.01f, 0.99f)] public float format5TriggerHealthRate = 0.20f;
    [Min(0.1f)] public float format5CountdownSeconds = 30f;
    [Min(0.01f)] public float format5CountdownRate = 2f;

    [Header("格式6 - 红色长方形冲刺(三阶段新增) / Red Rectangle Dash (Phase 3)")]
    [Min(0)] public int format6Damage = 1;
    [Min(0f)] public float format6WarningTime = 0.8f;
    [Min(0.1f)] public float format6Width = 1.6f;
    [Min(0.1f)] public float format6Distance = 9f;
    [Min(0.1f)] public float format6Speed = 14f;
    [Min(0f)] public float format6ActiveDuration = 1.2f;
    [Min(0.02f)] public float format6DamageInterval = 0.2f;
    [Min(0f)] public float format6Knockback = 6f;
    [Min(0f)] public float format6Cooldown = 5.5f;
    [Min(0f)] public float format6Weight = 1f;

    private void OnValidate()
    {
        phase1HealthRate = Mathf.Clamp(phase1HealthRate, 0.02f, 0.99f);
        phase2HealthRate = Mathf.Clamp(phase2HealthRate, 0.01f, phase1HealthRate - 0.01f);
        phase3HealthRate = Mathf.Clamp(phase3HealthRate, 0.001f, phase2HealthRate - 0.01f);
        format5TriggerHealthRate = Mathf.Clamp01(format5TriggerHealthRate);
        minimumEffectHeight = Mathf.Max(minimumEffectHeight, -0.99f);
        telegraphHeight = Mathf.Max(telegraphHeight, minimumEffectHeight);
    }

    public float GetEffectHeight()
    {
        return Mathf.Max(telegraphHeight, minimumEffectHeight, -0.99f);
    }

    public float GetMoveSpeed(int phase)
    {
        float multiplier = phase switch
        {
            1 => phase1MoveMultiplier,
            2 => phase2MoveMultiplier,
            3 => phase3MoveMultiplier,
            _ => 1f
        };

        return moveSpeed * multiplier;
    }
}
