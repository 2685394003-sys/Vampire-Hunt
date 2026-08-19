using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using VampireHunt.Boss.Application;
using VampireHunt.Boss.Contracts;
using VampireHunt.Combat.Application;
using VampireHunt.Combat.Contracts;
using VampireHunt.Combat.Domain;
using VampireHunt.Core;
using VampireHunt.Core.Contracts;
using VampireHunt.Enemies.Contracts;
using VampireHunt.Infrastructure.Integration;
using VampireHunt.Infrastructure.Netcode;
using VampireHunt.Infrastructure.Netcode.Contracts;
using VampireHunt.Infrastructure.UnityPhysics;
using VampireHunt.Navigation.Contracts;
using VampireHunt.Player.Application;
using VampireHunt.Player.Contracts;
using VampireHunt.Presentation.Boss;
using VampireHunt.Presentation.Enemies;
using VampireHunt.Presentation.Player;
using VampireHunt.Presentation.Runtime;
using VampireHunt.UI;
using VampireHunt.UI.Contracts;

namespace VampireHunt.Bootstrap
{
    /// <summary>
    /// Production composition provider.  This is the only runtime type that
    /// knows concrete feature, Unity, NGO, Presentation and UI adapters.
    /// </summary>
    [DefaultExecutionOrder(-1100)]
    [DisallowMultipleComponent]
    public sealed class DefaultCompositionFactoryProvider :
        MonoBehaviour,
        ICompositionFactoryProvider
    {
        [SerializeField] private int randomSeed = 19790427;
        [SerializeField] private LayerMask combatLayerMask = Physics.DefaultRaycastLayers;
        [SerializeField, Min(8)] private int maximumMeleeColliders = 128;

        private GameCompositionContext activeContext;

        public CompositionFactorySet CreateFactories() => new(
            new DelegateModuleFactory(InstallGameplay),
            new DelegateModuleFactory(InstallOffline),
            new DelegateModuleFactory(InstallNetcode),
            new DelegateModuleFactory(InstallPresentation),
            new DelegateModuleFactory(InstallUi));

        private void Update()
        {
            if (activeContext == null || activeContext.IsDisposed) return;
            if (activeContext.TryResolve(out RuntimeBindingCoordinator bindings))
                bindings.RefreshNetworkBindings();
            if (activeContext.TryResolve(out RuntimeUpdateLoop loop))
                loop.Tick(Time.deltaTime);
            if (activeContext.TryResolve(out RuntimeUiCoordinator ui))
                ui.Render();
        }

        private IDisposable InstallGameplay(GameCompositionContext context)
        {
            if (activeContext != null && !activeContext.IsDisposed)
                throw new InvalidOperationException("This provider already owns a composed runtime.");

            CompositionInstallation installation = new();
            UnityGameClock clock = new();
            SeededRandomSource random = new(randomSeed);
            EntityIdAllocator entityIds = new();
            GameplayEventIdAllocator eventIds = new();
            RuntimeGameplayEventHub events = new();
            RuntimeEntityLifecycleHub lifecycle = new();
            RuntimeUpdateLoop updateLoop = new();
            CombatEntityDirectoryAdapter combatEntities = new(lifecycle);
            CombatTargetQueryAdapter combatTargets = new();
            CombatResolver resolver = new(random);
            KnockbackResolver knockback = new();
            CombatApplicationService combat = new(
                resolver,
                combatEntities,
                events,
                clock,
                eventIds);
            RuntimePlayerRegistry players = new();
            RuntimeBossRegistry bosses = new();
            PlayerRewardAdapter rewards = new(players);
            RuntimeBloodPactCatalog bloodPacts = new(context.Specs.BloodPacts);
            PlayerRandomAdapter playerRandom = new(random);
            PhysicsTargetRegistry physicsTargets = new();
            MeleePhysicsQueryAdapter melee = new(
                physicsTargets,
                combatLayerMask,
                maximumMeleeColliders,
                QueryTriggerInteraction.Ignore,
                physicsTargets);
            CombatDamageResolverAdapter playerDamage = new(combat);
            INavigationField navigation = ResolveNavigation(context);

            Register(context, clock);
            Register<IGameClock>(context, clock);
            Register(context, random);
            Register<IRandomSource>(context, random);
            Register(context, entityIds);
            Register(context, eventIds);
            Register(context, events);
            Register<IGameplayEventSink>(context, events);
            Register(context, lifecycle);
            Register(context, updateLoop);
            Register(context, combatEntities);
            Register<ICombatEntityDirectory>(context, combatEntities);
            Register(context, combatTargets);
            Register<ICombatTargetQuery>(context, combatTargets);
            Register(context, resolver);
            Register(context, knockback);
            Register(context, combat);
            Register(context, players);
            Register(context, bosses);
            Register<IPlayerProgressionCommands>(context, players);
            Register<VampireHunt.Enemies.Contracts.IRewardService>(context, rewards);
            Register<IPlayerPositionQuery>(context, players);
            Register<IBloodPactCatalog>(context, bloodPacts);
            Register<IPlayerRandom>(context, playerRandom);
            Register(context, physicsTargets);
            Register<IMeleeHitQuery>(context, melee);
            Register<IPlayerDamageResolver>(context, playerDamage);
            Register<INavigationField>(context, navigation);

            RuntimeBindingCoordinator bindings = new(
                context,
                entityIds,
                clock,
                random,
                events,
                updateLoop,
                combat,
                combatEntities,
                combatTargets,
                physicsTargets,
                melee,
                playerDamage,
                players,
                bloodPacts,
                playerRandom,
                navigation,
                rewards,
                bosses);
            Register(context, bindings);

            RuntimeEnemySpawner enemySpawner = new(context, bindings, GetComponent<NetworkManager>());
            RuntimeSpawnLocationQuery spawnLocations = new(
                context.Specs.EnemySpawn,
                navigation,
                random,
                players,
                enemySpawner,
                bosses);
            RuntimeSpawnGate spawnGate = new(context, players, bosses);
            VampireHunt.Spawning.Domain.EnemySpawnDirector spawnDirector = new(
                context.Specs.EnemySpawn,
                new VampireHunt.Spawning.Domain.SpawnPressurePolicy(context.Specs.EnemySpawn),
                new VampireHunt.Spawning.Domain.SpawnCandidateSampler(
                    spawnLocations,
                    random,
                    context.Specs.EnemySpawn.MaxSampleAttempts),
                enemySpawner,
                spawnGate);
            RuntimeEnemySpawnSimulation spawnSimulation = new(
                context,
                spawnDirector,
                enemySpawner,
                players,
                GetComponent<NetworkManager>(),
                bosses);
            Register<VampireHunt.Spawning.Contracts.IEnemySpawnDirector>(context, spawnDirector);
            Register(context, enemySpawner);
            Register<IEnemyLifetimePort>(context, enemySpawner);
            Register(context, spawnSimulation);
            installation.Add(updateLoop.Add(
                RuntimeSimulationPhase.EnemySpawning,
                spawnSimulation.Tick));
            bindings.BindExplicitSceneAdapters();

            installation.Add(enemySpawner);
            installation.Add(bindings);
            installation.Add(players);
            installation.Add(updateLoop);
            installation.Add(lifecycle);
            installation.Add(events);
            installation.OnDispose(() =>
            {
                if (ReferenceEquals(activeContext, context)) activeContext = null;
            });
            activeContext = context;
            return installation;
        }

        private IDisposable InstallOffline(GameCompositionContext context)
        {
            RuntimePlayerRegistry players = Resolve<RuntimePlayerRegistry>(context);
            LocalRuntimeAdapter local = new(players);
            Register(context, local);
            Register<IPlayerCommandGateway>(context, local);
            return new ActionModuleInstallation(null);
        }

        private IDisposable InstallNetcode(GameCompositionContext context)
        {
            NetworkManager manager = GetComponent<NetworkManager>();
            if (manager == null)
                throw new InvalidOperationException("Netcode composition requires NetworkManager beside the provider.");

            NetworkCommandRpcAdapter commandRpc = RequireSceneBinding<NetworkCommandRpcAdapter>(context);
            NetworkStateRpcAdapter stateRpc = RequireSceneBinding<NetworkStateRpcAdapter>(context);
            GameplayEventRpcAdapter eventRpc = RequireSceneBinding<GameplayEventRpcAdapter>(context);
            RuntimePlayerRegistry players = Resolve<RuntimePlayerRegistry>(context);
            RuntimeGameplayEventHub eventHub = Resolve<RuntimeGameplayEventHub>(context);
            RuntimeEntityLifecycleHub lifecycle = Resolve<RuntimeEntityLifecycleHub>(context);
            RuntimeBindingCoordinator bindings = Resolve<RuntimeBindingCoordinator>(context);

            NetworkCommandRouter router = new((IOwnedPlayerCommandEndpoint)players);
            commandRpc.Configure(router, players);
            RuntimeNetworkCommandGateway gateway = new(commandRpc);

            PlayerReadModelProjector playerProjector = GetOrCreatePlayerProjector(context);
            EnemyReadModelProjector enemyProjector = GetOrCreateEnemyProjector(context);
            BossReadModelProjector bossProjector = GetOrCreateBossProjector(context);
            PlayerStateReplicator playerStates = new();
            EnemyStateReplicator enemyStates = new();
            BossStateReplicator bossStates = new();
            stateRpc.Configure(
                playerStates,
                playerProjector,
                enemyStates,
                enemyProjector,
                bossStates,
                bossProjector);

            GameplayEventReplicator eventReplicator = new(eventHub);
            eventRpc.Configure(eventReplicator, eventHub);
            IDisposable outbound = eventHub.AddOutbound(eventReplicator);
            NetworkEntityRegistry networkEntities = new();
            IDisposable lifecycleRegistration = lifecycle.Add(networkEntities);
            bindings.ConfigureNetwork(manager, networkEntities);

            Register(context, manager);
            Register(context, commandRpc);
            Register(context, stateRpc);
            Register(context, eventRpc);
            Register(context, router);
            Register(context, playerStates);
            Register(context, enemyStates);
            Register(context, bossStates);
            Register(context, eventReplicator);
            Register(context, networkEntities);
            Register<IPlayerCommandGateway>(context, gateway);

            CompositionInstallation installation = new();
            installation.Add(lifecycleRegistration);
            installation.Add(outbound);
            installation.OnDispose(() => bindings.ConfigureNetwork(null, null));
            return installation;
        }

        private IDisposable InstallPresentation(GameCompositionContext context)
        {
            RuntimeGameplayEventHub eventHub = Resolve<RuntimeGameplayEventHub>(context);
            RuntimeEntityLifecycleHub lifecycle = Resolve<RuntimeEntityLifecycleHub>(context);
            ClientGameplayEventDispatcher dispatcher = new();
            EntityViewRegistry views = new();
            IDisposable localEvents = eventHub.AddLocalIngress(dispatcher);
            IDisposable lifecycleRegistration = lifecycle.Add(views);

            PlayerReadModelProjector player = GetOrCreatePlayerProjector(context);
            EnemyReadModelProjector enemy = GetOrCreateEnemyProjector(context);
            BossReadModelProjector boss = GetOrCreateBossProjector(context);
            Register<IGameplayEventIngress>(context, dispatcher);
            Register<IPlayerReadModel>(context, player);
            Register<IEnemyReadModel>(context, enemy);
            Register<IBossReadModel>(context, boss);
            Register(context, dispatcher);
            Register(context, views);

            if (Resolve<RuntimePlayerRegistry>(context).TryGetFirst(out IPlayerRuntimePort runtime))
                player.Apply(runtime.Snapshot);

            CompositionInstallation installation = new();
            installation.Add(lifecycleRegistration);
            installation.Add(localEvents);
            installation.Add(views);
            installation.Add(dispatcher);
            return installation;
        }

        private IDisposable InstallUi(GameCompositionContext context)
        {
            RuntimeUiCoordinator ui = new(
                context,
                Resolve<IPlayerReadModel>(context),
                Resolve<IPlayerCommandGateway>(context),
                Resolve<RuntimePlayerRegistry>(context),
                context.TryResolve(out NetworkManager manager) ? manager : null);
            Register(context, ui);
            return ui;
        }

        private static PlayerReadModelProjector GetOrCreatePlayerProjector(GameCompositionContext context)
        {
            if (context.TryResolve(out PlayerReadModelProjector value)) return value;
            value = new PlayerReadModelProjector();
            Register(context, value);
            return value;
        }

        private static EnemyReadModelProjector GetOrCreateEnemyProjector(GameCompositionContext context)
        {
            if (context.TryResolve(out EnemyReadModelProjector value)) return value;
            value = new EnemyReadModelProjector();
            Register(context, value);
            return value;
        }

        private static BossReadModelProjector GetOrCreateBossProjector(GameCompositionContext context)
        {
            if (context.TryResolve(out BossReadModelProjector value)) return value;
            value = new BossReadModelProjector();
            Register(context, value);
            return value;
        }

        private static T RequireSceneBinding<T>(GameCompositionContext context) where T : class
        {
            if (context.SceneBindings != null)
            {
                foreach (T binding in context.SceneBindings.EnumerateBindings<T>())
                    return binding;
            }
            throw new InvalidOperationException($"SceneBindings requires an explicit {typeof(T).Name} adapter.");
        }

        private static INavigationField ResolveNavigation(GameCompositionContext context)
        {
            if (context.SceneBindings != null)
            {
                foreach (INavigationField binding in context.SceneBindings.EnumerateBindings<INavigationField>())
                    return binding;
            }
            return new AlwaysWalkableNavigationField();
        }

        private static T Resolve<T>(GameCompositionContext context) where T : class
        {
            if (context.TryResolve(out T value)) return value;
            throw new InvalidOperationException($"Composition service {typeof(T).FullName} is missing.");
        }

        private static void Register<T>(GameCompositionContext context, T value) where T : class =>
            context.Register<T>(value);
    }

    internal sealed class AlwaysWalkableNavigationField : INavigationField
    {
        public VampireHunt.Navigation.Domain.Direction SampleDirection(WorldPosition position, WorldPosition target) =>
            VampireHunt.Navigation.Domain.Direction.None;
        public bool IsWalkable(WorldPosition position) => true;
        public WorldPosition TryFindRecovery(WorldPosition position) => position;
    }

    internal sealed class RuntimeNetworkCommandGateway : IPlayerCommandGateway
    {
        private readonly NetworkCommandRpcAdapter adapter;

        public RuntimeNetworkCommandGateway(NetworkCommandRpcAdapter adapter) =>
            this.adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));

        public CommandResult SubmitDash(DashCommand command) => adapter.SubmitDash(command);
        public CommandResult SubmitAttack(AttackCommand command) => adapter.SubmitAttack(command);
        public CommandResult SelectBloodPact(SelectBloodPactCommand command) => adapter.SubmitBloodPact(command);
    }

    internal sealed class RuntimeUiCoordinator : IDisposable
    {
        private readonly GameCompositionContext context;
        private readonly IPlayerReadModel player;
        private readonly IPlayerCommandGateway commands;
        private readonly RuntimePlayerRegistry players;
        private readonly NetworkManager networkManager;
        private readonly List<PlayerHudPresenter> hud = new();
        private readonly List<BloodPactUiBinding> bloodPacts = new();
        private bool disposed;

        public RuntimeUiCoordinator(
            GameCompositionContext context,
            IPlayerReadModel player,
            IPlayerCommandGateway commands,
            RuntimePlayerRegistry players,
            NetworkManager networkManager)
        {
            this.context = context ?? throw new ArgumentNullException(nameof(context));
            this.player = player ?? throw new ArgumentNullException(nameof(player));
            this.commands = commands ?? throw new ArgumentNullException(nameof(commands));
            this.players = players ?? throw new ArgumentNullException(nameof(players));
            this.networkManager = networkManager;
            SceneManager.sceneLoaded += HandleSceneChanged;
            SceneManager.sceneUnloaded += HandleSceneUnloaded;
            RebuildViews();
        }

        public void Render()
        {
            if (disposed) return;
            for (int i = hud.Count - 1; i >= 0; i--) hud[i].Render();
            IPlayerBloodPactReadModel bloodPactModel = ResolveBloodPactModel();
            for (int i = bloodPacts.Count - 1; i >= 0; i--)
                bloodPacts[i].Render(bloodPactModel);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            SceneManager.sceneLoaded -= HandleSceneChanged;
            SceneManager.sceneUnloaded -= HandleSceneUnloaded;
            hud.Clear();
            bloodPacts.Clear();
        }

        private void HandleSceneChanged(Scene scene, LoadSceneMode mode) => RebuildViews();
        private void HandleSceneUnloaded(Scene scene) => RebuildViews();

        private void RebuildViews()
        {
            hud.Clear();
            bloodPacts.Clear();
            HashSet<MonoBehaviour> visited = new();
            AddBindingSet(context.SceneBindings, visited, includeGameplayRoot: true);

            // Additively loaded UI scenes opt in with their own SceneBindings
            // marker. Bootstrap inspects only that explicit marker, never the
            // scene for arbitrary player/domain components.
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.IsValid() || !scene.isLoaded) continue;
                foreach (GameObject rootObject in scene.GetRootGameObjects())
                {
                    SceneBindings[] markers = rootObject.GetComponentsInChildren<SceneBindings>(true);
                    for (int i = 0; i < markers.Length; i++)
                        if (markers[i] != context.SceneBindings)
                            AddBindingSet(markers[i], visited, includeGameplayRoot: false);
                }
            }
        }

        private void AddBindingSet(
            SceneBindings source,
            ISet<MonoBehaviour> visited,
            bool includeGameplayRoot)
        {
            if (source == null) return;
            IReadOnlyList<MonoBehaviour> explicitBindings = source.AdapterBindings;
            for (int i = 0; i < explicitBindings.Count; i++)
                AddView(explicitBindings[i], visited);

            Transform root = includeGameplayRoot ? source.GameplayRoot : null;
            if (root == null) return;
            MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
                AddView(behaviours[i], visited);
        }

        private void AddView(MonoBehaviour behaviour, ISet<MonoBehaviour> visited)
        {
            if (behaviour == null || !visited.Add(behaviour)) return;
            if (behaviour is IPlayerHudView hudView)
                hud.Add(new PlayerHudPresenter(player, hudView));
            if (behaviour is IBloodPactSelectionView selectionView)
            {
                BloodPactSelectionPresenter presenter = new(selectionView, commands);
                if (behaviour is IBloodPactSelectionPresenterBinding binding)
                    binding.Bind(presenter);
                bloodPacts.Add(new BloodPactUiBinding(presenter));
            }
        }

        private IPlayerBloodPactReadModel ResolveBloodPactModel()
        {
            if (networkManager != null && networkManager.IsListening &&
                networkManager.IsClient && networkManager.SpawnManager != null)
            {
                foreach (NetworkObject networkObject in networkManager.SpawnManager.SpawnedObjectsList)
                {
                    if (networkObject == null || !networkObject.IsSpawned || !networkObject.IsOwner) continue;
                    MonoBehaviour[] behaviours = networkObject.GetComponentsInChildren<MonoBehaviour>(true);
                    for (int i = 0; i < behaviours.Length; i++)
                        if (behaviours[i] is IPlayerBloodPactReadModel model) return model;
                }
            }

            return players.TryGetFirst(out IPlayerRuntimePort runtime) ? runtime : null;
        }

        private sealed class BloodPactUiBinding
        {
            private readonly BloodPactSelectionPresenter presenter;

            public BloodPactUiBinding(BloodPactSelectionPresenter presenter) =>
                this.presenter = presenter;

            public void Render(IPlayerBloodPactReadModel model)
            {
                if (model == null || !model.IsBloodPactPlayerAlive ||
                    !model.TryGetBloodPactOffer(out BloodPactOffer offer) ||
                    !offer.IsValid || model.BloodPactScarlet < offer.Cost)
                {
                    if (presenter.HasOffer) presenter.Hide();
                    return;
                }

                if (!presenter.HasOffer ||
                    presenter.CurrentOffer.OfferVersion != offer.OfferVersion)
                    presenter.ShowOffer(offer);
            }
        }
    }
}
