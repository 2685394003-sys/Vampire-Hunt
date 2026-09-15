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
        [Min(0.1f)] [SerializeField] private float spawnMinDistance = 16f;
        [Min(0.1f)] [SerializeField] private float spawnMaxDistance = 32f;

        [Header("Stagger / Execution")]
        [Min(0.1f)] [SerializeField] private float staggerTriggerDistance = 6f;
        [Min(0.1f)] [SerializeField] private float staggerEffectDuration = 3.2f;
        [Min(0.1f)] [SerializeField] private float executionWindowDuration = 3f;
        [SerializeField] private uint[] staggerAbilityIds = { 2001u, 2002u, 2003u };

        [Header("Stage Transition")]
        [Min(0.1f)] [SerializeField] private float phaseTransitionDuration = 2f;
        [SerializeField] private uint phaseAuraAbilityId = 2090u;

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
        public float SpawnMinDistance => spawnMinDistance;
        public float SpawnMaxDistance => spawnMaxDistance;
        public float StaggerEffectDuration => staggerEffectDuration;
        public float ExecutionWindowDuration => executionWindowDuration;
        public float PhaseTransitionDuration => phaseTransitionDuration;
        public uint PhaseAuraAbilityId => phaseAuraAbilityId;
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
            spawnMinDistance = Mathf.Max(0.1f, spawnMinDistance);
            spawnMaxDistance = Mathf.Max(spawnMinDistance, spawnMaxDistance);
            staggerTriggerDistance = Mathf.Max(0.1f, staggerTriggerDistance);
            staggerEffectDuration = Mathf.Max(0.1f, staggerEffectDuration);
            executionWindowDuration = Mathf.Max(0.1f, executionWindowDuration);
            phaseTransitionDuration = Mathf.Max(0.1f, phaseTransitionDuration);
            frenzyHealthThreshold = Mathf.Clamp(frenzyHealthThreshold, 0.01f, 0.99f);
            maxTrustedHitDamage = Mathf.Max(1f, maxTrustedHitDamage);
            staggerAbilityIds ??= Array.Empty<uint>();
        }
    }
}
