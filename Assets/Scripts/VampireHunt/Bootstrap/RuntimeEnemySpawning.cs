using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Boss.Contracts;
using VampireHunt.Core;
using VampireHunt.Enemies.Contracts;
using VampireHunt.Navigation.Contracts;
using VampireHunt.Spawning.Contracts;
using EntityId = VampireHunt.Core.EntityId;

namespace VampireHunt.Bootstrap
{
    /// <summary>
    /// Unity/NGO pool and instantiation adapter behind the sole spawn director.
    /// Every take is rebound to a new authoritative runtime/EntityId.
    /// </summary>
    internal sealed class RuntimeEnemySpawner :
        IEnemySpawner,
        IEnemyLifetimePort,
        INetworkPrefabInstanceHandler,
        IDisposable
    {
        private readonly GameCompositionContext context;
        private readonly RuntimeBindingCoordinator bindings;
        private readonly NetworkManager networkManager;
        private readonly GameObject prefab;
        private readonly Transform poolRoot;
        private readonly Stack<GameObject> available = new();
        private readonly Dictionary<EntityId, GameObject> instancesById = new();
        private readonly List<GameObject> instances = new();
        private readonly int maxRetained;
        private bool handlerRegistered;
        private bool disposed;

        public RuntimeEnemySpawner(
            GameCompositionContext context,
            RuntimeBindingCoordinator bindings,
            NetworkManager networkManager)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            this.bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
            this.networkManager = networkManager;
            prefab = context.ConfigCatalog?.EnemyDefinition?.Prefab;
            maxRetained = Math.Max(1, context.Specs.EnemySpawn.MaxAlive);

            GameObject root = new("[RuntimeEnemyPool]");
            Transform owner = networkManager != null
                ? networkManager.transform
                : context.SceneBindings?.GameplayRoot;
            root.transform.SetParent(owner, false);
            poolRoot = root.transform;

            if (prefab != null)
            {
                int prewarm = Math.Min(context.Specs.EnemySpawn.PoolPrewarm, maxRetained);
                for (int i = 0; i < prewarm; i++)
                {
                    GameObject instance = CreateInstance();
                    if (instance != null) available.Push(instance);
                }

                if (context.RuntimeMode == RuntimeMode.Netcode &&
                    networkManager != null &&
                    prefab.GetComponent<NetworkObject>() != null)
                    handlerRegistered = networkManager.PrefabHandler.AddHandler(prefab, this);
            }
        }

        public int ActiveCount
        {
            get
            {
                Cleanup();
                return instances.Count;
            }
        }

        public EntityId Spawn(SpawnRequest request)
        {
            if (disposed || prefab == null) return EntityId.Invalid;
            if (context.RuntimeMode == RuntimeMode.Netcode &&
                (networkManager == null || !networkManager.IsServer))
                return EntityId.Invalid;

            Vector3 position = ToVector3(request.Position);
            Vector3 forward = ToVector3(request.Candidate.Target) - position;
            forward.y = 0f;
            Quaternion rotation = forward.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(forward.normalized, Vector3.up)
                : Quaternion.identity;
            GameObject instance = Take(position, rotation, notifyLifecycle: false);
            if (instance == null) return EntityId.Invalid;
            NetworkObject networkObject = instance.GetComponent<NetworkObject>();

            if (context.RuntimeMode == RuntimeMode.Netcode && networkObject == null)
            {
                Return(instance);
                return EntityId.Invalid;
            }

            EntityId id = bindings.BindSpawnedEnemy(instance, networkObject);
            if (!id.IsValid)
            {
                Return(instance);
                return EntityId.Invalid;
            }

            if (context.RuntimeMode == RuntimeMode.Netcode)
            {
                networkObject.Spawn(true);
                bindings.RefreshNetworkBindings();
            }

            instances.Add(instance);
            instancesById[id] = instance;
            return id;
        }

        public void Release(EntityId entityId)
        {
            if (disposed || !entityId.IsValid ||
                !instancesById.TryGetValue(entityId, out GameObject instance))
                return;

            NetworkObject networkObject = instance != null
                ? instance.GetComponent<NetworkObject>()
                : null;
            if (context.RuntimeMode == RuntimeMode.Netcode &&
                networkObject != null && networkObject.IsSpawned &&
                networkManager != null && networkManager.IsServer)
                networkObject.Despawn(false);

            if (instancesById.ContainsKey(entityId)) Return(instance);
        }

        public NetworkObject Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation)
        {
            GameObject instance = Take(position, rotation, notifyLifecycle: true);
            return instance != null ? instance.GetComponent<NetworkObject>() : null;
        }

        public void Destroy(NetworkObject networkObject)
        {
            if (networkObject != null) Return(networkObject.gameObject);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (handlerRegistered && networkManager != null && prefab != null)
            {
                networkManager.PrefabHandler.RemoveHandler(prefab);
                handlerRegistered = false;
            }

            GameObject[] active = instances.ToArray();
            for (int i = active.Length - 1; i >= 0; i--)
            {
                GameObject instance = active[i];
                if (instance == null) continue;
                NetworkObject networkObject = instance.GetComponent<NetworkObject>();
                if (networkObject != null && networkObject.IsSpawned &&
                    networkManager != null && networkManager.IsServer)
                    networkObject.Despawn(false);
                UnityEngine.Object.Destroy(instance);
            }
            instances.Clear();
            instancesById.Clear();
            while (available.Count > 0)
            {
                GameObject instance = available.Pop();
                if (instance != null) UnityEngine.Object.Destroy(instance);
            }
            if (poolRoot != null) UnityEngine.Object.Destroy(poolRoot.gameObject);
        }

        private void Cleanup()
        {
            for (int i = instances.Count - 1; i >= 0; i--)
            {
                GameObject instance = instances[i];
                if (instance == null || !instance.activeInHierarchy)
                {
                    instances.RemoveAt(i);
                    RemoveIdentityMapping(instance);
                }
            }
        }

        public void CopyActivePositions(List<WorldPosition> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            Cleanup();
            for (int i = 0; i < instances.Count; i++)
            {
                GameObject instance = instances[i];
                if (instance == null) continue;
                Vector3 position = instance.transform.position;
                destination.Add(new WorldPosition(position.x, position.y, position.z));
            }
        }

        private static Vector3 ToVector3(WorldPosition value) =>
            new(value.X, value.Y, value.Z);

        private GameObject CreateInstance()
        {
            if (prefab == null) return null;
            GameObject instance = UnityEngine.Object.Instantiate(prefab, poolRoot);
            instance.SetActive(false);
            return instance;
        }

        private GameObject Take(Vector3 position, Quaternion rotation, bool notifyLifecycle)
        {
            GameObject instance = null;
            while (available.Count > 0 && instance == null) instance = available.Pop();
            instance ??= CreateInstance();
            if (instance == null) return null;

            instance.transform.SetParent(null, false);
            instance.transform.SetPositionAndRotation(position, rotation);
            instance.SetActive(true);
            if (notifyLifecycle) NotifyTaken(instance);
            return instance;
        }

        private void Return(GameObject instance)
        {
            if (instance == null) return;
            RemoveIdentity(instance);
            bindings.ReleaseEnemy(instance);
            NotifyReturned(instance);
            instance.SetActive(false);
            instance.transform.SetParent(poolRoot, false);
            if (!disposed && available.Count < maxRetained)
                available.Push(instance);
            else
                UnityEngine.Object.Destroy(instance);
        }

        private void RemoveIdentity(GameObject instance)
        {
            if (instance == null)
            {
                List<EntityId> missing = null;
                foreach (KeyValuePair<EntityId, GameObject> pair in instancesById)
                {
                    if (pair.Value != null) continue;
                    missing ??= new List<EntityId>();
                    missing.Add(pair.Key);
                }
                if (missing != null)
                    for (int i = 0; i < missing.Count; i++) instancesById.Remove(missing[i]);
                return;
            }

            for (int i = instances.Count - 1; i >= 0; i--)
                if (ReferenceEquals(instances[i], instance)) instances.RemoveAt(i);

            RemoveIdentityMapping(instance);
        }

        private void RemoveIdentityMapping(GameObject instance)
        {
            if (instance == null)
            {
                List<EntityId> missing = null;
                foreach (KeyValuePair<EntityId, GameObject> pair in instancesById)
                {
                    if (pair.Value != null) continue;
                    missing ??= new List<EntityId>();
                    missing.Add(pair.Key);
                }
                if (missing != null)
                    for (int i = 0; i < missing.Count; i++) instancesById.Remove(missing[i]);
                return;
            }

            EntityId remove = EntityId.Invalid;
            foreach (KeyValuePair<EntityId, GameObject> pair in instancesById)
            {
                if (!ReferenceEquals(pair.Value, instance)) continue;
                remove = pair.Key;
                break;
            }
            if (remove.IsValid) instancesById.Remove(remove);
        }

        private static void NotifyTaken(GameObject instance)
        {
            MonoBehaviour[] behaviours = instance.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
                if (behaviours[i] is IEnemyPoolLifecycle lifecycle)
                    lifecycle.OnTakenFromEnemyPool();
        }

        private static void NotifyReturned(GameObject instance)
        {
            MonoBehaviour[] behaviours = instance.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
                if (behaviours[i] is IEnemyPoolLifecycle lifecycle)
                    lifecycle.OnReturnedToEnemyPool();
        }
    }

    /// <summary>
    /// Bootstrap-owned Boss projection used by spawning. Spawning never sees a
    /// Boss MonoBehaviour or domain implementation; it only consumes this
    /// composition-time projection.
    /// </summary>
    internal sealed class RuntimeBossRegistry
    {
        private IBossRuntimePort runtime;
        private Transform transform;

        public int Phase => runtime == null ? 0 : (int)runtime.Phase;
        public bool IsDead => runtime != null &&
            (runtime.Health <= 0 || runtime.Snapshot.Mode == EncounterMode.Defeated);
        public bool IsTransitioning => runtime != null && runtime.IsInvulnerable;

        public bool TryRegister(IBossRuntimePort value, Transform valueTransform)
        {
            if (value == null) return false;
            if (runtime != null && !ReferenceEquals(runtime, value)) return false;
            runtime = value;
            transform = valueTransform;
            return true;
        }

        public void Unregister(IBossRuntimePort value)
        {
            if (!ReferenceEquals(runtime, value)) return;
            runtime = null;
            transform = null;
        }

        public bool TryGetPosition(out WorldPosition position)
        {
            if (runtime == null || transform == null || IsDead)
            {
                position = WorldPosition.Origin;
                return false;
            }

            Vector3 value = transform.position;
            position = new WorldPosition(value.x, value.y, value.z);
            return true;
        }
    }

    internal sealed class RuntimeSpawnLocationQuery : ISpawnLocationQuery
    {
        private readonly EnemySpawnSpec spec;
        private readonly INavigationField navigation;
        private readonly IRandomSource random;
        private readonly RuntimePlayerRegistry players;
        private readonly RuntimeEnemySpawner spawner;
        private readonly RuntimeBossRegistry boss;
        private readonly List<WorldPosition> positions = new();
        private readonly List<WorldPosition> enemies = new();

        public RuntimeSpawnLocationQuery(
            EnemySpawnSpec spec,
            INavigationField navigation,
            IRandomSource random,
            RuntimePlayerRegistry players,
            RuntimeEnemySpawner spawner,
            RuntimeBossRegistry boss)
        {
            this.spec = spec ?? throw new ArgumentNullException(nameof(spec));
            this.navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            this.random = random ?? throw new ArgumentNullException(nameof(random));
            this.players = players ?? throw new ArgumentNullException(nameof(players));
            this.spawner = spawner ?? throw new ArgumentNullException(nameof(spawner));
            this.boss = boss ?? throw new ArgumentNullException(nameof(boss));
        }

        public WorldPosition SampleAround(WorldPosition target)
        {
            double angle = random.NextFloat() * Math.PI * 2d;
            if (spec.BossDirectionProbability > 0f &&
                random.NextFloat() < spec.BossDirectionProbability &&
                boss.TryGetPosition(out WorldPosition bossPosition))
            {
                float deltaX = bossPosition.X - target.X;
                float deltaZ = bossPosition.Z - target.Z;
                if (deltaX * deltaX + deltaZ * deltaZ > 0.0001f)
                {
                    double center = Math.Atan2(deltaZ, deltaX);
                    double offsetDegrees =
                        (random.NextFloat() * 2f - 1f) * spec.BossDirectionHalfAngle;
                    angle = center + offsetDegrees * Math.PI / 180d;
                }
            }

            float minimum = spec.MinSpawnRadius;
            float radius = (float)Math.Sqrt(
                minimum * minimum +
                (spec.SpawnRadius * spec.SpawnRadius - minimum * minimum) * random.NextFloat());
            return new WorldPosition(
                target.X + (float)Math.Cos(angle) * radius,
                target.Y + spec.SpawnHeightOffset,
                target.Z + (float)Math.Sin(angle) * radius);
        }

        public bool IsValid(WorldPosition position)
        {
            if (spec.RequireWalkable && !navigation.IsWalkable(position)) return false;
            positions.Clear();
            players.CopyAlivePositions(positions);
            float minimumDistance = spec.MinSpawnRadius;
            float minimumSquared = minimumDistance * minimumDistance;
            for (int i = 0; i < positions.Count; i++)
                if (position.DistanceSquaredTo(positions[i]) < minimumSquared) return false;

            if (spec.MinWaveSpawnSeparation > 0f)
            {
                enemies.Clear();
                spawner.CopyActivePositions(enemies);
                float separationSquared = spec.MinWaveSpawnSeparation * spec.MinWaveSpawnSeparation;
                for (int i = 0; i < enemies.Count; i++)
                    if (position.DistanceSquaredTo(enemies[i]) < separationSquared) return false;
            }

            if (spec.SpawnClearanceRadius > 0f && spec.SpawnBlockingMask != 0)
            {
                Vector3 query = new(
                    position.X,
                    position.Y + spec.SpawnClearanceRadius,
                    position.Z);
                if (Physics.CheckSphere(
                    query,
                    spec.SpawnClearanceRadius,
                    spec.SpawnBlockingMask,
                    QueryTriggerInteraction.Ignore))
                    return false;
            }
            return true;
        }
    }

    internal sealed class RuntimeSpawnGate : ISpawnGate
    {
        private readonly GameCompositionContext context;
        private readonly RuntimePlayerRegistry players;
        private readonly RuntimeBossRegistry boss;
        private double resumeAt;

        public RuntimeSpawnGate(
            GameCompositionContext context,
            RuntimePlayerRegistry players,
            RuntimeBossRegistry boss)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            this.players = players ?? throw new ArgumentNullException(nameof(players));
            this.boss = boss ?? throw new ArgumentNullException(nameof(boss));
        }

        public bool CanSpawn(SpawnContext spawnContext)
        {
            if (context.IsDisposed || players.Count <= 0 || spawnContext.TargetPositions.Count <= 0)
                return false;

            EnemySpawnSpec spec = context.Specs.EnemySpawn;
            if (spec.StopWhenBossDies && boss.IsDead) return false;
            if (spec.PauseDuringBossTransition && boss.IsTransitioning)
            {
                resumeAt = Math.Max(
                    resumeAt,
                    spawnContext.ElapsedSeconds + spec.ResumeDelayAfterPhase);
                return false;
            }

            return spawnContext.ElapsedSeconds >= resumeAt;
        }
    }

    /// <summary>Server simulation callback that advances the single director.</summary>
    internal sealed class RuntimeEnemySpawnSimulation
    {
        private readonly GameCompositionContext context;
        private readonly IEnemySpawnDirector director;
        private readonly IEnemySpawner spawner;
        private readonly RuntimePlayerRegistry players;
        private readonly NetworkManager networkManager;
        private readonly RuntimeBossRegistry boss;
        private readonly List<WorldPosition> targets = new();
        private double elapsed;

        public RuntimeEnemySpawnSimulation(
            GameCompositionContext context,
            IEnemySpawnDirector director,
            IEnemySpawner spawner,
            RuntimePlayerRegistry players,
            NetworkManager networkManager,
            RuntimeBossRegistry boss)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            this.director = director ?? throw new ArgumentNullException(nameof(director));
            this.spawner = spawner ?? throw new ArgumentNullException(nameof(spawner));
            this.players = players ?? throw new ArgumentNullException(nameof(players));
            this.networkManager = networkManager;
            this.boss = boss ?? throw new ArgumentNullException(nameof(boss));
        }

        public void Tick(float deltaTime)
        {
            if (context.IsDisposed ||
                (context.RuntimeMode == RuntimeMode.Netcode &&
                 (networkManager == null || !networkManager.IsServer)))
                return;

            float safeDelta = Math.Max(0f, deltaTime);
            elapsed += safeDelta;
            targets.Clear();
            players.CopyAlivePositions(targets);
            if (targets.Count == 0) return;
            director.Tick(new SpawnContext(
                elapsed,
                safeDelta,
                boss.Phase,
                targets,
                spawner.ActiveCount));
        }
    }
}
