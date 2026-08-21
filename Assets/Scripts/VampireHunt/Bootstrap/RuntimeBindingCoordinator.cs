using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Boss.Application;
using VampireHunt.Boss.Contracts;
using VampireHunt.Combat.Application;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;
using VampireHunt.Enemies.Contracts;
using VampireHunt.Enemies.Application;
using VampireHunt.Infrastructure.Integration;
using VampireHunt.Infrastructure.Input;
using VampireHunt.Infrastructure.Input.Contracts;
using VampireHunt.Infrastructure.Netcode;
using VampireHunt.Infrastructure.Netcode.Contracts;
using VampireHunt.Infrastructure.UnityPhysics;
using VampireHunt.Player.Application;
using VampireHunt.Player.Contracts;
using VampireHunt.Navigation.Contracts;
using VampireHunt.Presentation.Boss;
using VampireHunt.Presentation.Player;
using VampireHunt.Spawning.Contracts;
using EntityId = VampireHunt.Core.EntityId;

namespace VampireHunt.Bootstrap
{
    /// <summary>
    /// Binds scene/prefab shells to application runtimes.  Discovery is limited
    /// to explicit SceneBindings and NetworkManager's own spawned-object list;
    /// no feature service performs a global scene search.
    /// </summary>
    internal sealed class RuntimeBindingCoordinator : IDisposable
    {
        private readonly GameCompositionContext context;
        private readonly EntityIdAllocator entityIds;
        private readonly IGameClock clock;
        private readonly IRandomSource random;
        private readonly RuntimeGameplayEventHub events;
        private readonly RuntimeUpdateLoop updateLoop;
        private readonly CombatApplicationService combat;
        private readonly CombatEntityDirectoryAdapter combatEntities;
        private readonly CombatTargetQueryAdapter combatTargets;
        private readonly PhysicsTargetRegistry physicsTargets;
        private readonly IMeleeHitQuery melee;
        private readonly IPlayerDamageResolver playerDamage;
        private readonly RuntimePlayerRegistry players;
        private readonly IBloodPactCatalog bloodPacts;
        private readonly IPlayerRandom playerRandom;
        private readonly INavigationField navigation;
        private readonly IRewardService rewards;
        private readonly RuntimeBossRegistry bosses;
        private readonly Dictionary<MonoBehaviour, PlayerRecord> playerRecords = new();
        private readonly Dictionary<MonoBehaviour, EnemyRecord> enemyRecords = new();
        private readonly Dictionary<MonoBehaviour, BossRecord> bossRecords = new();
        private readonly List<WorldPosition> playerPositionBuffer = new();
        private readonly PlayerPoseState playerPoses = new();
        private NetworkManager networkManager;
        private NetworkEntityRegistry networkEntities;
        private bool disposed;

        public RuntimeBindingCoordinator(
            GameCompositionContext context,
            EntityIdAllocator entityIds,
            IGameClock clock,
            IRandomSource random,
            RuntimeGameplayEventHub events,
            RuntimeUpdateLoop updateLoop,
            CombatApplicationService combat,
            CombatEntityDirectoryAdapter combatEntities,
            CombatTargetQueryAdapter combatTargets,
            PhysicsTargetRegistry physicsTargets,
            IMeleeHitQuery melee,
            IPlayerDamageResolver playerDamage,
            RuntimePlayerRegistry players,
            IBloodPactCatalog bloodPacts,
            IPlayerRandom playerRandom,
            INavigationField navigation,
            IRewardService rewards,
            RuntimeBossRegistry bosses)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            this.entityIds = entityIds ?? throw new ArgumentNullException(nameof(entityIds));
            this.clock = clock ?? throw new ArgumentNullException(nameof(clock));
            this.random = random ?? throw new ArgumentNullException(nameof(random));
            this.events = events ?? throw new ArgumentNullException(nameof(events));
            this.updateLoop = updateLoop ?? throw new ArgumentNullException(nameof(updateLoop));
            this.combat = combat ?? throw new ArgumentNullException(nameof(combat));
            this.combatEntities = combatEntities ?? throw new ArgumentNullException(nameof(combatEntities));
            this.combatTargets = combatTargets ?? throw new ArgumentNullException(nameof(combatTargets));
            this.physicsTargets = physicsTargets ?? throw new ArgumentNullException(nameof(physicsTargets));
            this.melee = melee ?? throw new ArgumentNullException(nameof(melee));
            this.playerDamage = playerDamage ?? throw new ArgumentNullException(nameof(playerDamage));
            this.players = players ?? throw new ArgumentNullException(nameof(players));
            this.bloodPacts = bloodPacts ?? throw new ArgumentNullException(nameof(bloodPacts));
            this.playerRandom = playerRandom ?? throw new ArgumentNullException(nameof(playerRandom));
            this.navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
            this.rewards = rewards ?? throw new ArgumentNullException(nameof(rewards));
            this.bosses = bosses ?? throw new ArgumentNullException(nameof(bosses));
        }

        public void BindExplicitSceneAdapters()
        {
            ThrowIfDisposed();
            if (context.SceneBindings == null) return;
            IReadOnlyList<MonoBehaviour> bindings = context.SceneBindings.AdapterBindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                MonoBehaviour behaviour = bindings[i];
                if (behaviour == null) continue;
                if (behaviour is IEnemySpawnDirectorBinding spawnBinding &&
                    context.TryResolve(out IEnemySpawnDirector spawnDirector))
                    spawnBinding.TryBind(spawnDirector);
                if (context.RuntimeMode == RuntimeMode.Offline && behaviour is IPlayerRuntimeBinding)
                    TryBindPlayer(behaviour, 0UL, null);
                if (context.RuntimeMode == RuntimeMode.Offline && behaviour is IEnemyRuntimeBinding)
                    TryBindEnemy(behaviour, null);
                if (context.RuntimeMode == RuntimeMode.Offline &&
                    behaviour is IBossRuntimeDependencyProvider &&
                    behaviour is IBossRuntimeBinding)
                {
                    TryBindBoss(behaviour);
                }
            }
        }

        public void ConfigureNetwork(NetworkManager manager, NetworkEntityRegistry entities)
        {
            if (networkManager != null)
            {
                networkManager.OnServerStarted -= HandleServerStarted;
                networkManager.OnClientConnectedCallback -= HandleClientConnected;
            }

            networkManager = manager;
            networkEntities = entities;
            if (networkManager == null) return;
            networkManager.OnServerStarted += HandleServerStarted;
            networkManager.OnClientConnectedCallback += HandleClientConnected;
            if (networkManager.IsServer) HandleServerStarted();
        }

        public void RefreshNetworkBindings()
        {
            if (disposed) return;
            if (context.RuntimeMode == RuntimeMode.Offline)
                RefreshExplicitGameplayRoot();

            if (networkManager == null || !networkManager.IsServer || networkManager.SpawnManager == null)
            {
                RefreshEnemyIdentities();
                return;
            }

            foreach (NetworkObject networkObject in networkManager.SpawnManager.SpawnedObjectsList)
            {
                if (networkObject == null || !networkObject.IsSpawned) continue;
                MonoBehaviour[] behaviours = networkObject.GetComponentsInChildren<MonoBehaviour>(true);
                for (int componentIndex = 0; componentIndex < behaviours.Length; componentIndex++)
                {
                    MonoBehaviour behaviour = behaviours[componentIndex];
                    if (behaviour is IPlayerRuntimeBinding)
                        TryBindPlayer(behaviour, networkObject.OwnerClientId, networkObject);
                    if (behaviour is IEnemyRuntimeBinding)
                        TryBindEnemy(behaviour, networkObject);
                }
            }

            CleanupDespawnedPlayers();
            CleanupDespawnedEnemies();
            RefreshEnemyIdentities();
        }

        /// <summary>
        /// Binds a freshly instantiated enemy view before NGO exposes it to
        /// clients. The returned EntityId identifies this logical life only.
        /// </summary>
        public EntityId BindSpawnedEnemy(GameObject instance, NetworkObject networkObject)
        {
            ThrowIfDisposed();
            if (instance == null) return EntityId.Invalid;
            MonoBehaviour[] behaviours = instance.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour is not IEnemyRuntimeBinding binding) continue;
                if (!TryBindEnemy(behaviour, networkObject)) return EntityId.Invalid;
                return binding.Runtime != null ? binding.Runtime.Id : EntityId.Invalid;
            }
            return EntityId.Invalid;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            ConfigureNetwork(null, null);
            foreach (PlayerRecord record in playerRecords.Values) record.Dispose();
            playerRecords.Clear();
            foreach (EnemyRecord record in enemyRecords.Values) record.Dispose();
            enemyRecords.Clear();
            foreach (BossRecord record in bossRecords.Values) record.Dispose();
            bossRecords.Clear();
        }

        private void HandleServerStarted()
        {
            BindExplicitBossAdapters();
            RefreshNetworkBindings();
        }

        private void HandleClientConnected(ulong clientId) => RefreshNetworkBindings();

        private void BindExplicitBossAdapters()
        {
            if (context.SceneBindings == null) return;
            IReadOnlyList<MonoBehaviour> bindings = context.SceneBindings.AdapterBindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                MonoBehaviour behaviour = bindings[i];
                if (behaviour is IBossRuntimeDependencyProvider && behaviour is IBossRuntimeBinding)
                    TryBindBoss(behaviour);
            }
        }

        private bool TryBindPlayer(
            MonoBehaviour behaviour,
            ulong ownerClientId,
            NetworkObject networkObject)
        {
            if (behaviour == null || playerRecords.ContainsKey(behaviour) ||
                behaviour is not IPlayerRuntimeBinding binding)
                return false;

            EntityId id = entityIds.Allocate();
            playerPoses.Seed(id, behaviour.transform, clock.Now);
            IPlayerRuntimePort runtime = PlayerRuntimeEndpointFactory.Create(
                id,
                context.Specs.Player,
                playerDamage,
                clock: clock,
                hitQuery: melee,
                positions: playerPoses,
                bloodPactCatalog: bloodPacts.Count == 0 ? null : bloodPacts,
                random: bloodPacts.Count == 0 ? null : playerRandom,
                poseSink: playerPoses);
            if (!binding.TryBind(runtime))
            {
                (runtime as IDisposable)?.Dispose();
                playerPoses.Unbind(id);
                return false;
            }

            players.Register(runtime, behaviour.transform, ownerClientId);
            if (behaviour is IPlayerPoseOwnershipBinding ownershipBinding)
                ownershipBinding.ConfigurePoseOwnership(players);
            combatEntities.Register(runtime.PlayerId, runtime);
            RuntimePlayerCombatTarget target = new(runtime, playerPoses);
            combatTargets.Register(target);
            Collider[] colliders = behaviour.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                physicsTargets.Register(colliders[i], (ICombatTarget)target);
                physicsTargets.Register(colliders[i], (IMeleeHitTarget)target);
            }

            Action<PlayerSnapshot> snapshotChanged = snapshot =>
                ForwardPlayerSnapshot(runtime, ownerClientId, snapshot);
            runtime.SnapshotChanged += snapshotChanged;
            IDisposable tick = updateLoop.Add(RuntimeSimulationPhase.Abilities, runtime.Tick);
            PlayerRecord record = new(
                behaviour,
                networkObject,
                runtime,
                colliders,
                snapshotChanged,
                tick,
                players,
                combatEntities,
                combatTargets,
                physicsTargets,
                networkEntities,
                playerPoses);
            playerRecords.Add(behaviour, record);
            RefreshBossTargetBindings();

            if (networkObject != null && networkObject.NetworkObjectId != 0UL)
                networkEntities?.Register(runtime.PlayerId, networkObject.NetworkObjectId);
            if (bloodPacts.Count > 0) runtime.TryCreateBloodPactOffer(out _);
            ForwardPlayerSnapshot(runtime, ownerClientId, runtime.Snapshot);
            return true;
        }

        private bool TryBindBoss(MonoBehaviour behaviour)
        {
            if (behaviour == null || bossRecords.ContainsKey(behaviour) ||
                behaviour is not IBossRuntimeDependencyProvider provider ||
                behaviour is not IBossRuntimeBinding binding ||
                !provider.TryCreateRuntimeDependencies(out BossRuntimeDependencies dependencies))
                return false;

            BossRuntime runtime = BossRuntimeFactory.Create(
                context.Specs.Boss,
                random,
                combat,
                in dependencies,
                events,
                clock);
            dependencies.Register(runtime);
            if (!bosses.TryRegister(runtime, behaviour.transform)) return false;
            if (!binding.TryBind(runtime))
            {
                bosses.Unregister(runtime);
                return false;
            }
            combatEntities.Register(runtime.BossId, runtime.DamageReceiver);
            IDisposable tick = updateLoop.Add(RuntimeSimulationPhase.BossSimulation, deltaTime =>
            {
                runtime.Tick(deltaTime);
                ForwardBossSnapshot(runtime.Snapshot);
            });
            bossRecords.Add(
                behaviour,
                new BossRecord(runtime, tick, combatEntities, bosses));
            RefreshBossTargetBindings();
            ForwardBossSnapshot(runtime.Snapshot);
            return true;
        }

        private bool TryBindEnemy(MonoBehaviour behaviour, NetworkObject networkObject)
        {
            if (behaviour == null || enemyRecords.ContainsKey(behaviour) ||
                behaviour is not IEnemyRuntimeBinding binding)
                return false;

            EnemyRuntimeController runtime = new(
                navigation,
                entityIds,
                combatTargets,
                rewards,
                events,
                clock,
                Resolve<GameplayEventIdAllocator>());
            if (!binding.TryBind(runtime, context.Specs.Enemy)) return false;
            if (behaviour is IEnemyLifetimeBinding lifetimeBinding &&
                context.TryResolve(out IEnemyLifetimePort lifetime))
                lifetimeBinding.TryBind(lifetime);

            object damageCapability = behaviour is IDamageReceiver || behaviour is IHealingReceiver
                ? behaviour
                : runtime;
            combatEntities.Register(runtime.Id, damageCapability);
            RuntimeEnemyCombatTarget target = new(runtime, behaviour.transform);
            combatTargets.Register(target);
            Collider[] colliders = behaviour.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                physicsTargets.Register(colliders[i], (ICombatTarget)target);
                physicsTargets.Register(colliders[i], (IMeleeHitTarget)target);
            }
            if (networkObject != null && networkObject.NetworkObjectId != 0UL)
                networkEntities?.Register(runtime.Id, networkObject.NetworkObjectId);

            IDisposable tick = updateLoop.Add(RuntimeSimulationPhase.StateSnapshots, _ =>
            {
                if (behaviour == null || !runtime.IsSpawned) return;
                Vector3 position = behaviour.transform.position;
                runtime.SetPosition(new WorldPosition(position.x, position.y, position.z));
                ForwardEnemySnapshot(runtime.Snapshot);
            });
            enemyRecords.Add(
                behaviour,
                new EnemyRecord(
                    behaviour,
                    networkObject,
                    runtime,
                    damageCapability,
                    target,
                    colliders,
                    tick,
                    combatEntities,
                    combatTargets,
                    physicsTargets,
                    networkEntities));
            ForwardEnemySnapshot(runtime.Snapshot);
            return true;
        }

        public void ReleaseEnemy(GameObject instance)
        {
            if (instance == null) return;
            MonoBehaviour[] behaviours = instance.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (!enemyRecords.TryGetValue(behaviour, out EnemyRecord record)) continue;
                enemyRecords.Remove(behaviour);
                record.Dispose();
                return;
            }
        }

        private void ForwardPlayerSnapshot(
            IPlayerRuntimePort runtime,
            ulong ownerClientId,
            PlayerSnapshot snapshot)
        {
            if (bloodPacts.Count > 0 && !runtime.TryGetBloodPactOffer(out _))
                runtime.TryCreateBloodPactOffer(out _);

            bool local = context.RuntimeMode == RuntimeMode.Offline ||
                (networkManager != null && networkManager.IsClient &&
                 ownerClientId == networkManager.LocalClientId);
            if (local && context.TryResolve(out PlayerReadModelProjector projector))
                projector.Apply(snapshot);

            if (networkManager != null && networkManager.IsServer &&
                context.TryResolve(out NetworkStateRpcAdapter stateRpc) &&
                context.TryResolve(out PlayerStateReplicator replicator))
            {
                stateRpc.BroadcastPlayer(replicator.Capture(snapshot));
            }
        }

        private void ForwardBossSnapshot(BossSnapshot snapshot)
        {
            if (context.TryResolve(out BossReadModelProjector projector))
                projector.Apply(snapshot);
            if (networkManager != null && networkManager.IsServer &&
                context.TryResolve(out NetworkStateRpcAdapter stateRpc) &&
                context.TryResolve(out BossStateReplicator replicator))
            {
                stateRpc.BroadcastBoss(replicator.Capture(snapshot));
            }
        }

        private void ForwardEnemySnapshot(EnemySnapshot snapshot)
        {
            if (context.TryResolve(out VampireHunt.Presentation.Enemies.EnemyReadModelProjector projector))
                projector.Apply(snapshot);
            if (networkManager != null && networkManager.IsServer &&
                context.TryResolve(out NetworkStateRpcAdapter stateRpc) &&
                context.TryResolve(out EnemyStateReplicator replicator) &&
                replicator.Capture(
                    snapshot,
                    DistanceToNearestAlivePlayer(snapshot.Position),
                    clock.Now,
                    out VampireHunt.Infrastructure.Netcode.Contracts.EnemyStateDto dto))
            {
                stateRpc.BroadcastEnemy(dto);
            }
        }

        private float DistanceToNearestAlivePlayer(WorldPosition enemyPosition)
        {
            playerPositionBuffer.Clear();
            players.CopyAlivePositions(playerPositionBuffer);
            if (playerPositionBuffer.Count == 0) return float.PositiveInfinity;

            double nearestSquared = double.PositiveInfinity;
            for (int i = 0; i < playerPositionBuffer.Count; i++)
            {
                WorldPosition playerPosition = playerPositionBuffer[i];
                double x = enemyPosition.X - playerPosition.X;
                double y = enemyPosition.Y - playerPosition.Y;
                double z = enemyPosition.Z - playerPosition.Z;
                nearestSquared = Math.Min(nearestSquared, x * x + y * y + z * z);
            }

            return (float)Math.Sqrt(nearestSquared);
        }

        private void RefreshExplicitGameplayRoot()
        {
            Transform root = context.SceneBindings?.GameplayRoot;
            if (root == null) return;
            MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour is IPlayerRuntimeBinding) TryBindPlayer(behaviour, 0UL, null);
                if (behaviour is IEnemyRuntimeBinding) TryBindEnemy(behaviour, null);
            }
        }

        private void RefreshEnemyIdentities()
        {
            foreach (EnemyRecord record in enemyRecords.Values) record.RefreshIdentity();
        }

        private void CleanupDespawnedPlayers()
        {
            List<MonoBehaviour> removals = null;
            foreach (KeyValuePair<MonoBehaviour, PlayerRecord> pair in playerRecords)
            {
                PlayerRecord record = pair.Value;
                if (pair.Key != null && (record.NetworkObject == null || record.NetworkObject.IsSpawned))
                    continue;
                removals ??= new List<MonoBehaviour>();
                removals.Add(pair.Key);
            }
            if (removals == null) return;
            for (int i = 0; i < removals.Count; i++)
            {
                MonoBehaviour key = removals[i];
                if (!playerRecords.TryGetValue(key, out PlayerRecord record)) continue;
                playerRecords.Remove(key);
                record.Dispose();
            }
            RefreshBossTargetBindings();
        }

        private void RefreshBossTargetBindings()
        {
            Transform target = null;
            foreach (MonoBehaviour player in playerRecords.Keys)
            {
                if (player == null || !player.gameObject.activeInHierarchy) continue;
                target = player.transform;
                break;
            }

            foreach (MonoBehaviour boss in bossRecords.Keys)
            {
                if (boss is not IBossTargetBinding targetBinding) continue;
                if (target != null)
                    targetBinding.TryBindTarget(target);
                else
                    targetBinding.ClearBoundTarget();
            }
        }

        private void CleanupDespawnedEnemies()
        {
            List<MonoBehaviour> removals = null;
            foreach (KeyValuePair<MonoBehaviour, EnemyRecord> pair in enemyRecords)
            {
                EnemyRecord record = pair.Value;
                if (pair.Key != null && (record.NetworkObject == null || record.NetworkObject.IsSpawned))
                    continue;
                removals ??= new List<MonoBehaviour>();
                removals.Add(pair.Key);
            }
            if (removals == null) return;
            for (int i = 0; i < removals.Count; i++)
            {
                MonoBehaviour key = removals[i];
                if (!enemyRecords.TryGetValue(key, out EnemyRecord record)) continue;
                enemyRecords.Remove(key);
                record.Dispose();
            }
        }

        private T Resolve<T>() where T : class
        {
            if (context.TryResolve(out T value)) return value;
            throw new InvalidOperationException($"Composition service {typeof(T).FullName} is missing.");
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(RuntimeBindingCoordinator));
        }

        /// <summary>
        /// Latest owner-pose store. Combat and spawn consumers read this store
        /// instead of reaching into the owner-written Transform directly.
        /// </summary>
        private sealed class PlayerPoseState :
            IPlayerPoseSink,
            IPlayerPositionQuery
        {
            private readonly Dictionary<EntityId, MovementPose> poses = new();

            public void Seed(EntityId playerId, Transform transform, double serverReceivedAt)
            {
                if (!playerId.IsValid || transform == null) return;
                Vector3 position = transform.position;
                Vector3 facing = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
                if (facing.sqrMagnitude <= 0.000001f) facing = Vector3.forward;
                poses[playerId] = new MovementPose(
                    new WorldPosition(position.x, position.y, position.z),
                    new MoveVector(facing.x, facing.y, facing.z),
                    serverReceivedAt,
                    sequence: 0u);
            }

            public void SetPose(EntityId playerId, MovementPose pose)
            {
                if (playerId.IsValid && pose.IsFinite) poses[playerId] = pose;
            }

            public bool TryGetPosition(EntityId playerId, out WorldPosition position)
            {
                if (poses.TryGetValue(playerId, out MovementPose pose))
                {
                    position = pose.Position;
                    return true;
                }
                position = WorldPosition.Origin;
                return false;
            }

            public void Unbind(EntityId playerId) => poses.Remove(playerId);
        }

        private sealed class RuntimePlayerCombatTarget : ICombatTarget, IMeleeHitTarget
        {
            private readonly IPlayerRuntimePort runtime;
            private readonly PlayerPoseState playerPoses;

            public RuntimePlayerCombatTarget(
                IPlayerRuntimePort runtime,
                PlayerPoseState playerPoses)
            {
                this.runtime = runtime;
                this.playerPoses = playerPoses;
            }

            public EntityId Id => runtime.PlayerId;
            public bool IsAlive => runtime.IsAlive;
            public WorldPosition Position => playerPoses.TryGetPosition(runtime.PlayerId, out WorldPosition pose)
                ? pose
                : WorldPosition.Origin;
            public WorldPosition HitPosition => Position;
        }

        private sealed class RuntimeEnemyCombatTarget : ICombatTarget, IMeleeHitTarget
        {
            private readonly IEnemyRuntime runtime;
            private readonly Transform transform;

            public RuntimeEnemyCombatTarget(IEnemyRuntime runtime, Transform transform)
            {
                this.runtime = runtime;
                this.transform = transform;
            }

            public EntityId Id => runtime.Id;
            public bool IsAlive => runtime.IsSpawned && runtime.Snapshot.IsAlive;
            public WorldPosition Position
            {
                get
                {
                    if (transform == null) return runtime.Snapshot.Position;
                    Vector3 value = transform.position;
                    return new WorldPosition(value.x, value.y, value.z);
                }
            }
            public WorldPosition HitPosition => Position;
        }

        private sealed class PlayerRecord : IDisposable
        {
            private readonly MonoBehaviour behaviour;
            private readonly IPlayerRuntimePort runtime;
            private readonly Collider[] colliders;
            private readonly Action<PlayerSnapshot> snapshotChanged;
            private readonly IDisposable tick;
            private readonly RuntimePlayerRegistry players;
            private readonly CombatEntityDirectoryAdapter combatEntities;
            private readonly CombatTargetQueryAdapter combatTargets;
            private readonly PhysicsTargetRegistry physicsTargets;
            private readonly NetworkEntityRegistry networkEntities;
            private readonly PlayerPoseState playerPoses;

            public PlayerRecord(
                MonoBehaviour behaviour,
                NetworkObject networkObject,
                IPlayerRuntimePort runtime,
                Collider[] colliders,
                Action<PlayerSnapshot> snapshotChanged,
                IDisposable tick,
                RuntimePlayerRegistry players,
                CombatEntityDirectoryAdapter combatEntities,
                CombatTargetQueryAdapter combatTargets,
                PhysicsTargetRegistry physicsTargets,
                NetworkEntityRegistry networkEntities,
                PlayerPoseState playerPoses)
            {
                this.behaviour = behaviour;
                NetworkObject = networkObject;
                this.runtime = runtime;
                this.colliders = colliders;
                this.snapshotChanged = snapshotChanged;
                this.tick = tick;
                this.players = players;
                this.combatEntities = combatEntities;
                this.combatTargets = combatTargets;
                this.physicsTargets = physicsTargets;
                this.networkEntities = networkEntities;
                this.playerPoses = playerPoses;
            }

            public NetworkObject NetworkObject { get; }

            public void Dispose()
            {
                runtime.SnapshotChanged -= snapshotChanged;
                tick?.Dispose();
                players.Unregister(runtime.PlayerId, out _);
                combatEntities.Unregister(runtime.PlayerId);
                combatTargets.Unregister(runtime.PlayerId);
                networkEntities?.Unregister(runtime.PlayerId);
                for (int i = 0; i < colliders.Length; i++)
                    if (colliders[i] != null) physicsTargets.Unregister(colliders[i]);
                playerPoses.Unbind(runtime.PlayerId);
                (runtime as IDisposable)?.Dispose();
            }
        }

        private sealed class BossRecord : IDisposable
        {
            private readonly IBossRuntimePort runtime;
            private readonly IDisposable tick;
            private readonly CombatEntityDirectoryAdapter combatEntities;
            private readonly RuntimeBossRegistry bosses;

            public BossRecord(
                IBossRuntimePort runtime,
                IDisposable tick,
                CombatEntityDirectoryAdapter combatEntities,
                RuntimeBossRegistry bosses)
            {
                this.runtime = runtime;
                this.tick = tick;
                this.combatEntities = combatEntities;
                this.bosses = bosses;
            }

            public void Dispose()
            {
                tick?.Dispose();
                combatEntities.Unregister(runtime.BossId);
                bosses.Unregister(runtime);
            }
        }

        private sealed class EnemyRecord : IDisposable
        {
            private readonly MonoBehaviour behaviour;
            private readonly IEnemyRuntime runtime;
            private readonly object damageCapability;
            private readonly RuntimeEnemyCombatTarget target;
            private readonly Collider[] colliders;
            private readonly IDisposable tick;
            private readonly CombatEntityDirectoryAdapter combatEntities;
            private readonly CombatTargetQueryAdapter combatTargets;
            private readonly PhysicsTargetRegistry physicsTargets;
            private readonly NetworkEntityRegistry networkEntities;
            private EntityId registeredId;

            public EnemyRecord(
                MonoBehaviour behaviour,
                NetworkObject networkObject,
                IEnemyRuntime runtime,
                object damageCapability,
                RuntimeEnemyCombatTarget target,
                Collider[] colliders,
                IDisposable tick,
                CombatEntityDirectoryAdapter combatEntities,
                CombatTargetQueryAdapter combatTargets,
                PhysicsTargetRegistry physicsTargets,
                NetworkEntityRegistry networkEntities)
            {
                this.behaviour = behaviour;
                NetworkObject = networkObject;
                this.runtime = runtime;
                this.damageCapability = damageCapability;
                this.target = target;
                this.colliders = colliders;
                this.tick = tick;
                this.combatEntities = combatEntities;
                this.combatTargets = combatTargets;
                this.physicsTargets = physicsTargets;
                this.networkEntities = networkEntities;
                registeredId = runtime.Id;
            }

            public NetworkObject NetworkObject { get; }

            public void RefreshIdentity()
            {
                EntityId current = runtime.Id;
                if (!current.IsValid) return;
                if (current == registeredId)
                {
                    if (NetworkObject != null && NetworkObject.IsSpawned)
                        networkEntities?.Register(registeredId, NetworkObject.NetworkObjectId);
                    return;
                }
                combatEntities.Unregister(registeredId);
                combatTargets.Unregister(registeredId);
                networkEntities?.Unregister(registeredId);
                registeredId = current;
                combatEntities.Register(registeredId, damageCapability);
                combatTargets.Register(target);
                if (NetworkObject != null && NetworkObject.IsSpawned)
                    networkEntities?.Register(registeredId, NetworkObject.NetworkObjectId);
            }

            public void Dispose()
            {
                tick?.Dispose();
                combatEntities.Unregister(registeredId);
                combatTargets.Unregister(registeredId);
                networkEntities?.Unregister(registeredId);
                for (int i = 0; i < colliders.Length; i++)
                    if (colliders[i] != null) physicsTargets.Unregister(colliders[i]);
                runtime.ResetForDespawn();
            }
        }
    }
}
