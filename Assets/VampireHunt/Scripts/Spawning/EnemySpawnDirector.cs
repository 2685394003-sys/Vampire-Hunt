using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;
using VampireHunt.Bootstrap;
using VampireHunt.Infrastructure.Netcode;
using VampireHunt.Infrastructure.Unity;
using VampireHunt.Progression;
using VampireHunt.Run;

namespace VampireHunt.Spawning
{
    /// <summary>Single server-side entry point for regular enemy spawning.</summary>
    [DisallowMultipleComponent]
    public sealed class EnemySpawnDirector : MonoBehaviour
    {
        private const int SpawnCandidateAttempts = 12;

        [Header("Dependencies")]
        [SerializeField] private VampireHuntGameManager runManager;
        [SerializeField] private EnemyAffixRunState enemyAffixState;
        [FormerlySerializedAs("meleeArchetype")]
        [SerializeField] private EnemyArchetypeAsset fallbackArchetype;
        [SerializeField] private EnemySpawnCatalogAsset spawnCatalog;

        [Header("Budget")]
        [Min(1)] [SerializeField] private int softEnemyCap = 20;
        [Min(1)] [SerializeField] private int softSpawnBudget = 20;
        [Min(0.1f)] [SerializeField] private float spawnInterval = 1.5f;
        [Min(0f)] [SerializeField] private float initialDelay = 1f;
        [SerializeField] private int runSeed = 1337;

        [Header("Spawn Rate Modifiers")]
        [Tooltip("Boss 战期间刷怪速率倍率（相对探索期 1.0）。0.4 = 探索期的 40%。")]
        [Range(0f, 2f)] [SerializeField] private float bossPhaseSpawnRateMultiplier = 0.4f;
        [Tooltip("玩家越靠近 Boss 刷怪越快，最近时达到的最高倍率（≥1）。")]
        [Min(1f)] [SerializeField] private float proximityMaxSpawnRateMultiplier = 1.5f;
        [Tooltip("距离 Boss 超过该半径后，距离加成失效（倍率回到 1.0）。")]
        [Min(1f)] [SerializeField] private float proximityBoostRadius = 15f;
        [Tooltip("玩家距离 Boss 超过该距离后完全停止刷怪。")]
        [Min(1f)] [SerializeField] private float spawnMaxBossDistance = 45f;

        [Header("Runtime Spawn Rate Multiplier (Debug)")]
        [Tooltip("运行时刷怪倍率（监控面板滑条实时设置）。默认 1 = 不变；范围 0.1 ~ 20（上限 20 倍）。")]
        [Range(0.1f, 20f)] public float runtimeSpawnRateMultiplier = 1f;

        [Header("Spawn Rate Ramp (Time Driven)")]
        [Tooltip("开启后，刷怪速率倍率随本局探索时长自动增长；关闭则时间项恒为 1（等同旧行为）。")]
        [SerializeField] private bool enableTimeRamp = true;
        [Tooltip("从起始倍率增长到终点倍率所需的时间（分钟），按游戏内探索时长计（unscaledTime，不受 timeScale 与菜单暂停影响）。")]
        [Min(0.01f)] [SerializeField] private float rampDurationMinutes = 20f;
        [Tooltip("探索开始时的时间倍率。")]
        [Min(0.01f)] [SerializeField] private float rampStartMultiplier = 1f;
        [Tooltip("到达 rampDurationMinutes 后的时间倍率；到达后保持该值不再增长。")]
        [Min(0.01f)] [SerializeField] private float rampEndMultiplier = 20f;
        [Tooltip("曲线指数：1 = 线性匀速；2 = 前慢后快（推荐，前 10 分钟只到约 5 倍，后 10 分钟冲到 20 倍）；0.5 = 前快后慢。")]
        [Min(0.05f)] [SerializeField] private float rampCurveExponent = 2f;

        [Header("Enemy Health Ramp (Time Driven)")]
        [Tooltip("开启后，新刷出的普通怪（不含 Boss）最大生命值随本局探索时长自动增长；关闭则时间项恒为 1（等同旧行为）。")]
        [SerializeField] private bool enableHealthRamp = true;
        [Tooltip("从起始倍率增长到终点倍率所需的时间（分钟），与刷怪速率共用同一条探索计时基线。")]
        [Min(0.01f)] [SerializeField] private float healthRampDurationMinutes = 20f;
        [Tooltip("探索开始时刷出的怪，血量相对基础值的倍率。")]
        [Min(0.01f)] [SerializeField] private float healthRampStartMultiplier = 1f;
        [Tooltip("到达 healthRampDurationMinutes 后刷出的怪的血量倍率；到达后保持该值不再增长。")]
        [Min(0.01f)] [SerializeField] private float healthRampEndMultiplier = 2f;
        [Tooltip("曲线指数：1 = 线性匀速（10 分钟正好到终点的一半，可预测）；2 = 前慢后快；0.5 = 前快后慢。\n" +
                 "作用范围：只作用于 MaxHealth，怪物攻击力不随时间增长；且与副契「血肉增生」的加成是相乘关系 —— " +
                 "实际血量 = 原型基础血量 × 本倍率 × (1 + 副契加成)。Boss 走独立链路（BossEncounterAggregate），不受影响。")]
        [Min(0.05f)] [SerializeField] private float healthRampCurveExponent = 1f;

        [Header("Spawn Ring")]
        [Min(1f)] [SerializeField] private float minimumPlayerDistance = 12f;
        [Min(1f)] [SerializeField] private float maximumPlayerDistance = 20f;
        [SerializeField] private LayerMask groundMask = -1;
        [SerializeField] private LayerMask blockingMask = 0;
        [Min(0.1f)] [SerializeField] private float navMeshSampleRadius = 2f;
        [SerializeField] private int navMeshAreaMask = NavMesh.AllAreas;

        private sealed class ActiveEnemyRecord
        {
            public EnemyNetworkActor Actor;
            public EnemyArchetypeAsset Archetype;
        }

        private readonly List<ActiveEnemyRecord> m_ActiveEnemies = new List<ActiveEnemyRecord>();
        private readonly List<EnemySpawnEntryAsset> m_EligibleEntries = new List<EnemySpawnEntryAsset>();
        private NavMeshPath m_SpawnPath;
        private float m_NextSpawnTime;
        private float m_ExplorationStartedTime;
        private ulong m_NextEntityId = 1UL << 32;
        private uint m_SpawnSequence;
        private bool m_MissingConfigurationReported;
        private bool m_WasExploring;
        private BossEncounterDirector m_BossDirector;

        private void Awake()
        {
            m_SpawnPath = new NavMeshPath();
            if (runManager == null) runManager = GetComponent<VampireHuntGameManager>();
            if (runManager == null) runManager = FindAnyObjectByType<VampireHuntGameManager>();
            if (enemyAffixState == null) enemyAffixState = GetComponent<EnemyAffixRunState>();
            if (enemyAffixState == null) enemyAffixState = FindAnyObjectByType<EnemyAffixRunState>();
            maximumPlayerDistance = Mathf.Max(minimumPlayerDistance, maximumPlayerDistance);
            softSpawnBudget = Mathf.Max(1, softSpawnBudget);
        }

        private void OnEnable()
        {
            m_NextSpawnTime = Time.unscaledTime + initialDelay;
        }

        private void Update()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening || !manager.IsServer) return;
            // 单人模式菜单暂停时不刷新怪（Spawn 计时用 Time.unscaledTime，不受 Time.timeScale 影响）。
            if (MenuPauseController.IsPaused) return;
            RunPhase phase = runManager != null ? runManager.CurrentSnapshot.Phase : RunPhase.Lobby;
            bool shouldSpawn = phase == RunPhase.Exploring || phase == RunPhase.BossEncounter || phase == RunPhase.BossPhaseTransition;
            if (!shouldSpawn)
            {
                m_WasExploring = false;
                return;
            }
            if (!m_WasExploring)
            {
                m_WasExploring = true;
                m_ExplorationStartedTime = Time.unscaledTime;
                m_SpawnSequence = 0;
            }

            RemoveDespawnedEnemies();
            if (m_ActiveEnemies.Count >= softEnemyCap ||
                GetActiveSpawnCost() >= softSpawnBudget ||
                Time.unscaledTime < m_NextSpawnTime) return;

            // 刷怪速率倍率 = 时间增长 × Boss 战衰减 × 距离 Boss 加成 × 运行时倍率(滑条)；实际间隔 = 基础间隔 ÷ 倍率
            float phaseMultiplier = phase == RunPhase.Exploring ? 1f : bossPhaseSpawnRateMultiplier;
            float rateMultiplier = GetTimeRampMultiplier() * phaseMultiplier
                * GetProximityMultiplier(manager) * Mathf.Clamp(runtimeSpawnRateMultiplier, 0.1f, 20f);
            if (rateMultiplier <= 0f) return;  // 远离 Boss（>spawnMaxBossDistance）不刷怪
            float effectiveInterval = spawnInterval / Mathf.Max(0.001f, rateMultiplier);
            m_NextSpawnTime = Time.unscaledTime + effectiveInterval;

            TrySpawnEnemy(manager);
        }

        /// <summary>
        /// 时间驱动的刷怪倍率：随本局探索时长从 rampStartMultiplier 增长到 rampEndMultiplier，
        /// 到达 rampDurationMinutes 后保持终点值。曲线由 rampCurveExponent 塑形。
        /// </summary>
        private float GetTimeRampMultiplier()
        {
            if (!enableTimeRamp) return 1f;
            if (rampDurationMinutes <= 0f) return rampEndMultiplier;

            float elapsedMinutes = Mathf.Max(0f, Time.unscaledTime - m_ExplorationStartedTime) / 60f;
            float t = Mathf.Clamp01(elapsedMinutes / rampDurationMinutes);
            float shaped = rampCurveExponent <= 0f ? t : Mathf.Pow(t, rampCurveExponent);
            return Mathf.Lerp(rampStartMultiplier, rampEndMultiplier, shaped);
        }

        /// <summary>当前生效的时间驱动刷怪倍率（调试/监控面板读取用）。</summary>
        public float CurrentTimeRampMultiplier => GetTimeRampMultiplier();

        /// <summary>当前生效的时间驱动怪物血量倍率（调试/监控面板读取用）。</summary>
        public float CurrentHealthRampMultiplier => GetHealthRampMultiplier();

        /// <summary>
        /// 时间驱动的怪物血量倍率：随本局探索时长从 healthRampStartMultiplier 增长到 healthRampEndMultiplier，
        /// 到达 healthRampDurationMinutes 后保持终点值。曲线由 healthRampCurveExponent 塑形。
        /// 与刷怪速率共用同一条计时基线（m_ExplorationStartedTime），但倍率与曲线各自独立配置。
        /// </summary>
        private float GetHealthRampMultiplier()
        {
            if (!enableHealthRamp) return 1f;
            if (healthRampDurationMinutes <= 0f) return healthRampEndMultiplier;

            float elapsedMinutes = Mathf.Max(0f, Time.unscaledTime - m_ExplorationStartedTime) / 60f;
            float t = Mathf.Clamp01(elapsedMinutes / healthRampDurationMinutes);
            float shaped = healthRampCurveExponent <= 0f ? t : Mathf.Pow(t, healthRampCurveExponent);
            return Mathf.Lerp(healthRampStartMultiplier, healthRampEndMultiplier, shaped);
        }

        private float GetProximityMultiplier(NetworkManager manager)
        {
            if (m_BossDirector == null) m_BossDirector = FindAnyObjectByType<BossEncounterDirector>();
            if (m_BossDirector == null) return 1f;
            if (!TryGetPlayerCentroid(manager, out Vector3 center)) return 1f;

            float distance = Vector3.Distance(center, m_BossDirector.transform.position);

            // 距离 Boss 超过 spawnMaxBossDistance → 返回 0（不再刷怪）
            if (distance > spawnMaxBossDistance) return 0f;

            // 距离加成：越靠近 Boss 倍率越高（proximityBoostRadius 内从 1.0 升到 max）
            if (proximityBoostRadius <= 0f || proximityMaxSpawnRateMultiplier <= 1f) return 1f;
            float t = Mathf.Clamp01(distance / proximityBoostRadius);
            return Mathf.Lerp(proximityMaxSpawnRateMultiplier, 1f, t);
        }

        private void TrySpawnEnemy(NetworkManager manager)
        {
            int remainingBudget = Mathf.Max(0, softSpawnBudget - GetActiveSpawnCost());
            float elapsedSeconds = Mathf.Max(0f, Time.unscaledTime - m_ExplorationStartedTime);
            if (!TrySelectArchetype(elapsedSeconds, remainingBudget, out EnemyArchetypeAsset archetype) ||
                archetype == null || archetype.NetworkPrefab == null)
            {
                if (!m_MissingConfigurationReported)
                {
                    m_MissingConfigurationReported = true;
                    Debug.LogError("[EnemySpawnDirector] No eligible enemy archetype or network prefab is configured.", this);
                }
                return;
            }

            if (!TryGetPlayerCentroid(manager, out Vector3 center)) return;
            if (!TryFindSpawnPosition(center, out Vector3 spawnPosition)) return;

            NetworkObject instance = Instantiate(archetype.NetworkPrefab, spawnPosition, Quaternion.identity);
            if (!instance.TryGetComponent<EnemyNetworkActor>(out var actor))
            {
                Debug.LogError("[EnemySpawnDirector] Enemy prefab has no EnemyNetworkActor.", instance);
                Destroy(instance.gameObject);
                return;
            }

            actor.PrepareServerSpawn(
                ++m_NextEntityId,
                enemyAffixState != null
                    ? enemyAffixState.CaptureSpawnSnapshot()
                    : EnemyAffixSpawnSnapshot.Empty,
                GetHealthRampMultiplier());
            instance.Spawn();
            m_ActiveEnemies.Add(new ActiveEnemyRecord { Actor = actor, Archetype = archetype });
        }

        private bool TrySelectArchetype(
            float elapsedSeconds,
            int remainingBudget,
            out EnemyArchetypeAsset archetype)
        {
            archetype = null;
            m_EligibleEntries.Clear();
            float totalWeight = 0f;
            EnemySpawnEntryAsset[] entries = spawnCatalog != null
                ? spawnCatalog.Entries
                : System.Array.Empty<EnemySpawnEntryAsset>();
            for (int i = 0; i < entries.Length; i++)
            {
                EnemySpawnEntryAsset entry = entries[i];
                EnemyArchetypeAsset candidate = entry?.Archetype;
                if (candidate == null || candidate.NetworkPrefab == null ||
                    entry.Weight <= 0f || entry.MinimumElapsedSeconds > elapsedSeconds ||
                    candidate.SpawnCost > remainingBudget ||
                    CountActive(candidate) >= entry.MaximumConcurrent)
                    continue;
                m_EligibleEntries.Add(entry);
                totalWeight += entry.Weight;
            }

            if (m_EligibleEntries.Count == 0)
            {
                if (fallbackArchetype == null || fallbackArchetype.NetworkPrefab == null ||
                    fallbackArchetype.SpawnCost > remainingBudget)
                    return false;
                archetype = fallbackArchetype;
                return true;
            }

            float roll = HashToUnitInterval(runSeed, ++m_SpawnSequence) * totalWeight;
            for (int i = 0; i < m_EligibleEntries.Count; i++)
            {
                EnemySpawnEntryAsset entry = m_EligibleEntries[i];
                roll -= entry.Weight;
                if (roll > 0f) continue;
                archetype = entry.Archetype;
                return true;
            }
            archetype = m_EligibleEntries[m_EligibleEntries.Count - 1].Archetype;
            return true;
        }

        private int GetActiveSpawnCost()
        {
            int total = 0;
            for (int i = 0; i < m_ActiveEnemies.Count; i++)
            {
                EnemyArchetypeAsset archetype = m_ActiveEnemies[i].Archetype;
                if (archetype != null) total += archetype.SpawnCost;
            }
            return total;
        }

        private int CountActive(EnemyArchetypeAsset archetype)
        {
            int count = 0;
            for (int i = 0; i < m_ActiveEnemies.Count; i++)
                if (m_ActiveEnemies[i].Archetype == archetype) count++;
            return count;
        }

        private static float HashToUnitInterval(int seed, uint sequence)
        {
            unchecked
            {
                uint value = (uint)seed;
                value ^= sequence + 0x9e3779b9u + (value << 6) + (value >> 2);
                value ^= value >> 16;
                value *= 0x7feb352du;
                value ^= value >> 15;
                value *= 0x846ca68bu;
                value ^= value >> 16;
                return (value & 0x00ffffffu) / 16777216f;
            }
        }

        private bool TryGetPlayerCentroid(NetworkManager manager, out Vector3 center)
        {
            center = Vector3.zero;
            int count = 0;
            var clients = manager.ConnectedClientsList;
            for (int i = 0; i < clients.Count; i++)
            {
                NetworkObject player = clients[i].PlayerObject;
                if (player == null || !player.IsSpawned) continue;
                center += player.transform.position;
                count++;
            }

            if (count == 0) return false;
            center /= count;
            return true;
        }

        private bool TryFindSpawnPosition(Vector3 center, out Vector3 position)
        {
            for (int attempt = 0; attempt < SpawnCandidateAttempts; attempt++)
            {
                Vector2 circle = Random.insideUnitCircle.normalized;
                float distance = Random.Range(minimumPlayerDistance, maximumPlayerDistance);
                Vector3 candidate = center + new Vector3(circle.x, 0f, circle.y) * distance;
                Vector3 rayOrigin = candidate + Vector3.up * 40f;

                if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 80f, groundMask, QueryTriggerInteraction.Ignore))
                {
                    candidate = hit.point + Vector3.up * 0.05f;
                }

                Vector3 bottom = candidate + Vector3.up * 0.25f;
                Vector3 top = candidate + Vector3.up * 1.55f;
                if (Physics.CheckCapsule(bottom, top, 0.45f, blockingMask, QueryTriggerInteraction.Ignore)) continue;

                if (!NavMesh.SamplePosition(
                        candidate,
                        out NavMeshHit spawnHit,
                        navMeshSampleRadius,
                        navMeshAreaMask)) continue;
                if (!NavMesh.SamplePosition(
                        center,
                        out NavMeshHit targetHit,
                        Mathf.Max(3f, navMeshSampleRadius),
                        navMeshAreaMask)) continue;
                if (!NavMesh.CalculatePath(spawnHit.position, targetHit.position, navMeshAreaMask, m_SpawnPath) ||
                    m_SpawnPath.status != NavMeshPathStatus.PathComplete) continue;

                position = spawnHit.position;
                return true;
            }

            position = default;
            return false;
        }

        private void RemoveDespawnedEnemies()
        {
            for (int i = m_ActiveEnemies.Count - 1; i >= 0; i--)
            {
                EnemyNetworkActor enemy = m_ActiveEnemies[i].Actor;
                if (enemy == null || !enemy.IsSpawned)
                {
                    m_ActiveEnemies.RemoveAt(i);
                }
            }
        }
    }
}
