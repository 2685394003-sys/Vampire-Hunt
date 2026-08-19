using System;
using System.Collections.Generic;
using UnityEngine;
using VampireHunt.Core;
using VampireHunt.Spawning.Contracts;
using VampireHunt.Spawning.Domain;
using EntityId = VampireHunt.Core.EntityId;

/// <summary>
/// Unity composition adapter for the one authoritative EnemySpawnDirector.
/// It owns no pressure/capacity rules; those are delegated to the Domain
/// director.  Legacy spawner components obtain this shared instance instead of
/// running independent timers or spawning directly.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(-500)]
public sealed class EnemySpawnDirectorAdapter : MonoBehaviour, IEnemySpawnDirectorBinding
{
    private static EnemySpawnDirectorAdapter active;

    [SerializeField] private MonsterSpawnConfig config;
    [SerializeField] private Transform spawnParent;
    [SerializeField] private BossHealth boss;

    private readonly List<PlayerNetworkState> players = new(4);
    private readonly List<GameObject> activeEnemies = new();
    private readonly List<WorldPosition> targetPositions = new(4);
    private readonly List<WorldPosition> wavePositions = new();
    private IEnemySpawnDirector director;
    private bool externallyBound;
    private LegacySpawnLocationQuery locationQuery;
    private LegacySpawnGate spawnGate;
    private LegacyEnemySpawner spawner;
    private FlowFieldManager flowField;
    private double elapsedSeconds;
    private double resumeAt;
    private bool initialized;

    public MonsterSpawnConfig Config => config != null ? config : config = MonsterSpawnConfig.LoadDefault();
    public int ActiveCount
    {
        get
        {
            CleanupActive();
            int pooled = Config != null && Config.EnemyPrefab != null
                ? NetworkSpawnUtility.GetActivePooledCount(Config.EnemyPrefab)
                : 0;
            return Mathf.Max(activeEnemies.Count, pooled);
        }
    }

    public static EnemySpawnDirectorAdapter Active => active;
    public IEnemySpawnDirector SpawnDirector => director;

    private void Awake()
    {
        if (active != null && active != this)
        {
            enabled = false;
            return;
        }

        active = this;
    }

    private void Start() => Initialize();

    private void OnDestroy()
    {
        if (active == this) active = null;
    }

    private void Update()
    {
        if (externallyBound || !enabled || !NetworkAuthority.IsServerOrOffline() || !EnsureAuthority()) return;
        Tick(Time.deltaTime);
    }

    public void SetConfig(MonsterSpawnConfig value)
    {
        config = value;
        initialized = false;
        Initialize();
    }

    public void SetSpawnParent(Transform value) => spawnParent = value;
    public void SetBoss(BossHealth value) => boss = value;

    /// <summary>
    /// Lets Bootstrap install the already-composed director. The fallback
    /// director created for an un-migrated scene is replaced and no longer
    /// receives ticks, so there is still one authority.
    /// </summary>
    public bool TryBind(IEnemySpawnDirector value)
    {
        if (value == null) return false;
        director = value;
        externallyBound = true;
        initialized = true;
        return true;
    }

    public static EnemySpawnDirectorAdapter GetOrCreate(
        MonsterSpawnConfig value = null,
        Transform parent = null,
        BossHealth bossValue = null)
    {
        EnemySpawnDirectorAdapter result = active;
        if (result == null)
        {
            result = FindFirstObjectByType<EnemySpawnDirectorAdapter>();
            if (result != null)
            {
                // A scene may retain a disabled compatibility component after
                // a domain reload. Promote it instead of leaving the run with
                // no ticking director.
                active = result;
                result.enabled = true;
            }
        }
        if (result == null)
        {
            GameObject host = new GameObject("[EnemySpawnDirector]");
            result = host.AddComponent<EnemySpawnDirectorAdapter>();
        }

        if (value != null && result.config != value)
            result.SetConfig(value);
        if (parent != null) result.spawnParent = parent;
        if (bossValue != null) result.boss = bossValue;
        result.Initialize();
        return result;
    }

    public SpawnTickResult Tick(float deltaTime)
    {
        if (!EnsureAuthority()) return SpawnTickResult.Empty(false);
        Initialize();
        MonsterSpawnConfig settings = Config;
        if (!externallyBound && (settings == null || settings.EnemyPrefab == null))
            return SpawnTickResult.Empty(true);

        float dt = Mathf.Max(0f, deltaTime);
        elapsedSeconds += dt;
        if (settings != null && elapsedSeconds < settings.FirstSpawnDelay)
            return SpawnTickResult.Empty(true);

        CollectTargets();
        if (targetPositions.Count == 0) return SpawnTickResult.Empty(true);

        wavePositions.Clear();
        SpawnContext context = new SpawnContext(
            elapsedSeconds,
            dt,
            0,
            targetPositions,
            ActiveCount);
        return director.Tick(context);
    }

    public SpawnTickResult SpawnNow()
    {
        Initialize();
        MonsterSpawnConfig settings = Config;
        if (!externallyBound && settings == null) return SpawnTickResult.Empty(true);
        if (settings != null) elapsedSeconds = Math.Max(elapsedSeconds, settings.FirstSpawnDelay);
        return Tick(Mathf.Max(settings != null ? settings.SpawnInterval : 0.1f, 0.1f));
    }

    public void ResetForRun()
    {
        elapsedSeconds = 0d;
        resumeAt = 0d;
        director?.ResetForRun();
        activeEnemies.Clear();
        wavePositions.Clear();
    }

    private void Initialize()
    {
        if (initialized || Config == null) return;

        if (externallyBound)
        {
            initialized = true;
            return;
        }

        flowField = FindFirstObjectByType<FlowFieldManager>();
        if (boss == null) boss = FindFirstObjectByType<BossHealth>();
        EnemySpawnSpec spec = BuildSpec(Config);
        locationQuery = new LegacySpawnLocationQuery(this);
        spawner = new LegacyEnemySpawner(this);
        spawnGate = new LegacySpawnGate(this);
        director = new EnemySpawnDirector(
            spec,
            new SpawnPressurePolicy(spec),
            new SpawnCandidateSampler(locationQuery, maxAttempts: Mathf.Max(1, Config.MaxSampleAttemptsPerMonster)),
            spawner,
            spawnGate);

        NetworkSpawnUtility.ConfigurePool(Config.EnemyPrefab, Config.PoolPrewarmCount, Config.MaxAlive);
        initialized = true;
    }

    private bool EnsureAuthority()
    {
        return NetworkAuthority.IsServerOrOffline() &&
               (active == null || active == this);
    }

    private void CollectTargets()
    {
        targetPositions.Clear();
        NetworkPlayerRegistry.GetAlivePlayers(players);
        for (int i = 0; i < players.Count; i++)
        {
            PlayerNetworkState player = players[i];
            if (player != null) targetPositions.Add(ToWorldPosition(player.transform.position));
        }

        if (targetPositions.Count == 0)
        {
            if (flowField == null) flowField = FindFirstObjectByType<FlowFieldManager>();
            if (flowField != null && flowField.player != null)
                targetPositions.Add(ToWorldPosition(flowField.player.position));
        }
    }

    private EntityId Spawn(SpawnRequest request)
    {
        CleanupActive();
        Vector3 position = ToVector3(request.Position);
        Vector3 face = ToVector3(request.Candidate.Target) - position;
        face.y = 0f;
        if (face.sqrMagnitude < 0.0001f) face = Vector3.forward;

        GameObject instance = NetworkSpawnUtility.Spawn(
            Config.EnemyPrefab,
            position,
            Quaternion.LookRotation(face.normalized, Vector3.up),
            spawnParent);
        if (instance == null) return EntityId.Invalid;

        activeEnemies.Add(instance);
        wavePositions.Add(request.Candidate.Position);
        EnemyHealth health = instance.GetComponentInChildren<EnemyHealth>();
        if (health != null && health.Runtime != null && health.Runtime.Id.IsValid)
            return health.Runtime.Id;

        return EnemyLegacyEntityIds.Allocate();
    }

    private void CleanupActive()
    {
        for (int i = activeEnemies.Count - 1; i >= 0; i--)
        {
            if (activeEnemies[i] == null || !activeEnemies[i].activeInHierarchy)
                activeEnemies.RemoveAt(i);
        }
    }

    private bool IsBossBlocking()
    {
        if (boss == null) boss = FindFirstObjectByType<BossHealth>();
        if (boss == null) return false;
        if (boss.IsDead && Config != null && Config.StopWhenBossDies) return true;
        if (Config == null || !Config.PauseDuringBossTransition) return false;
        if (boss.IsInvulnerable)
        {
            resumeAt = Math.Max(
                resumeAt,
                elapsedSeconds + Math.Max(0f, Config.ResumeDelayAfterPhase));
            return true;
        }

        return elapsedSeconds < resumeAt;
    }

    private bool IsWalkable(Vector3 position)
    {
        if (flowField == null) flowField = FindFirstObjectByType<FlowFieldManager>();
        if (flowField == null) return true;
        Vector2Int cell = flowField.WorldToGrid(position);
        return flowField.IsInGrid(cell.x, cell.y) &&
               flowField.GetCellState(cell.x, cell.y) == CellState.Walkable;
    }

    private static EnemySpawnSpec BuildSpec(MonsterSpawnConfig value)
    {
        float growthPerMinute = value.WaveGrowthInterval <= 0f
            ? 0f
            : value.MonstersAddedPerGrowth * 60f / value.WaveGrowthInterval;
        return new EnemySpawnSpec(
            value.EnemyPrefab != null ? value.EnemyPrefab.name : "default",
            value.MaxAlive,
            value.PoolPrewarmCount,
            value.SpawnInterval,
            value.MaxSpawnRadius,
            value.MonstersPerWave,
            growthPerMinute,
            value.MaxMonstersPerWave,
            value.FirstSpawnDelay,
            value.MinSpawnRadius,
            value.MaxSampleAttemptsPerMonster,
            value.SpawnHeightOffset,
            value.MinWaveSpawnSeparation,
            value.BossDirectionProbability,
            value.BossDirectionHalfAngle,
            value.PauseDuringBossTransition,
            value.StopWhenBossDies,
            value.ResumeDelayAfterPhase,
            value.RequireWalkableCell,
            value.SpawnBlockingLayers.value,
            value.SpawnClearanceRadius);
    }

    private static Vector3 ToVector3(WorldPosition position) =>
        new Vector3(position.X, position.Y, position.Z);

    private static WorldPosition ToWorldPosition(Vector3 position) =>
        new WorldPosition(position.x, position.y, position.z);

    private sealed class LegacyEnemySpawner : IEnemySpawner
    {
        private readonly EnemySpawnDirectorAdapter owner;
        public LegacyEnemySpawner(EnemySpawnDirectorAdapter owner) { this.owner = owner; }
        public int ActiveCount => owner.ActiveCount;
        public EntityId Spawn(SpawnRequest request) => owner.Spawn(request);
    }

    private sealed class LegacySpawnGate : ISpawnGate
    {
        private readonly EnemySpawnDirectorAdapter owner;
        public LegacySpawnGate(EnemySpawnDirectorAdapter owner) { this.owner = owner; }
        public bool CanSpawn(SpawnContext context) => !owner.IsBossBlocking();
    }

    private sealed class LegacySpawnLocationQuery : ISpawnLocationQuery
    {
        private readonly EnemySpawnDirectorAdapter owner;
        public LegacySpawnLocationQuery(EnemySpawnDirectorAdapter owner) { this.owner = owner; }

        public WorldPosition SampleAround(WorldPosition target)
        {
            MonsterSpawnConfig settings = owner.Config;
            Vector3 targetPosition = ToVector3(target);
            Vector3 direction = UnityEngine.Random.insideUnitSphere;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;
            direction.Normalize();

            bool useBossDirection = owner.boss != null &&
                !owner.boss.IsDead && UnityEngine.Random.value < settings.BossDirectionProbability;
            if (useBossDirection)
            {
                Vector3 towardBoss = owner.boss.transform.position - targetPosition;
                towardBoss.y = 0f;
                if (towardBoss.sqrMagnitude > 0.0001f)
                {
                    float angle = UnityEngine.Random.Range(
                        -settings.BossDirectionHalfAngle,
                        settings.BossDirectionHalfAngle);
                    direction = Quaternion.Euler(0f, angle, 0f) * towardBoss.normalized;
                }
            }

            float radius = Mathf.Sqrt(UnityEngine.Random.Range(
                settings.MinSpawnRadius * settings.MinSpawnRadius,
                settings.MaxSpawnRadius * settings.MaxSpawnRadius));
            Vector3 candidate = targetPosition + direction * radius;
            candidate.y = targetPosition.y + settings.SpawnHeightOffset;
            return ToWorldPosition(candidate);
        }

        public bool IsValid(WorldPosition value)
        {
            Vector3 position = ToVector3(value);
            MonsterSpawnConfig settings = owner.Config;
            NetworkPlayerRegistry.GetAlivePlayers(owner.players);
            float minimumDistanceSquared = settings.MinSpawnRadius * settings.MinSpawnRadius;
            for (int i = 0; i < owner.players.Count; i++)
            {
                PlayerNetworkState player = owner.players[i];
                if (player != null && (player.transform.position - position).sqrMagnitude < minimumDistanceSquared)
                    return false;
            }

            for (int i = 0; i < owner.wavePositions.Count; i++)
            {
                if ((ToVector3(owner.wavePositions[i]) - position).sqrMagnitude <
                    settings.MinWaveSpawnSeparation * settings.MinWaveSpawnSeparation)
                    return false;
            }

            if (settings.RequireWalkableCell && !owner.IsWalkable(position)) return false;
            return settings.SpawnClearanceRadius <= 0f || !Physics.CheckSphere(
                position + Vector3.up * settings.SpawnClearanceRadius,
                settings.SpawnClearanceRadius,
                settings.SpawnBlockingLayers,
                QueryTriggerInteraction.Ignore);
        }
    }
}
