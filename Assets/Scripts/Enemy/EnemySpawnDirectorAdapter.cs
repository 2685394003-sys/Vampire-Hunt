using System;
using System.Collections.Generic;
using UnityEngine;
using VampireHunt.Core;
using VampireHunt.Spawning.Contracts;

/// <summary>
/// Serialized Unity bridge for the one Bootstrap-owned EnemySpawnDirector.
///
/// This component intentionally contains no pressure policy, pool, prefab
/// instantiation, or Update tick. RuntimeEnemySpawnSimulation is the only
/// authority that advances spawning; this shell exists so existing scenes and
/// legacy context-menu callers can bind to that director without changing their
/// serialized component references in one destructive step.
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
    private readonly List<WorldPosition> targetPositions = new(4);
    private IEnemySpawnDirector director;
    private bool externallyBound;

    public MonsterSpawnConfig Config =>
        config != null ? config : config = MonsterSpawnConfig.LoadDefault();

    /// <summary>
    /// Retained for old Inspector/debug consumers. The authoritative pool lives
    /// in Bootstrap and exposes its count to the Domain director directly.
    /// </summary>
    public int ActiveCount => 0;

    public static EnemySpawnDirectorAdapter Active => active;
    public IEnemySpawnDirector SpawnDirector => director;
    public bool IsBound => externallyBound && director != null;

    private void Awake()
    {
        if (active != null && active != this)
        {
            enabled = false;
            return;
        }

        active = this;
    }

    private void OnDestroy()
    {
        if (active == this) active = null;
    }

    private void Update()
    {
        // Deliberately empty. Bootstrap owns the simulation-loop tick.
    }

    public void SetConfig(MonsterSpawnConfig value) => config = value;
    public void SetSpawnParent(Transform value) => spawnParent = value;
    public void SetBoss(BossHealth value) => boss = value;

    /// <summary>Installs the already-composed authoritative director.</summary>
    public bool TryBind(IEnemySpawnDirector value)
    {
        if (value == null) return false;
        if (director != null && !ReferenceEquals(director, value)) return false;
        director = value;
        externallyBound = true;
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
            if (result != null) active = result;
        }

        if (result == null)
        {
            GameObject host = new GameObject("[EnemySpawnDirectorBinding]");
            result = host.AddComponent<EnemySpawnDirectorAdapter>();
        }

        if (value != null) result.config = value;
        if (parent != null) result.spawnParent = parent;
        if (bossValue != null) result.boss = bossValue;
        return result;
    }

    /// <summary>
    /// Compatibility/manual bridge only. It is never called by Update; normal
    /// runs use RuntimeEnemySpawnSimulation's explicit server tick order.
    /// </summary>
    public SpawnTickResult Tick(float deltaTime)
    {
        if (!IsBound || !NetworkAuthority.IsServerOrOffline())
            return SpawnTickResult.Empty(true);

        targetPositions.Clear();
        NetworkPlayerRegistry.GetAlivePlayers(players);
        for (int i = 0; i < players.Count; i++)
        {
            PlayerNetworkState value = players[i];
            if (value == null) continue;
            Vector3 position = value.transform.position;
            targetPositions.Add(new WorldPosition(position.x, position.y, position.z));
        }

        if (targetPositions.Count == 0) return SpawnTickResult.Empty(true);
        return director.Tick(new SpawnContext(
            0d,
            Mathf.Max(0f, deltaTime),
            0,
            targetPositions,
            0));
    }

    [System.Obsolete("Spawning is advanced by RuntimeEnemySpawnSimulation in Bootstrap.")]
    public SpawnTickResult SpawnNow() => Tick(0.1f);

    public void ResetForRun() => director?.ResetForRun();
}
