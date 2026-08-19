using System;
using System.Collections.Generic;
using VampireHunt.Boss.Domain;
using VampireHunt.Player.Domain;
using VampireHunt.Spawning.Contracts;

namespace VampireHunt.Bootstrap
{
    public enum RuntimeMode
    {
        Offline = 0,
        Netcode = 1
    }

    /// <summary>Immutable output of ConfigCatalog; no authoring asset is retained.</summary>
    public sealed class GameSpecs
    {
        public GameSpecs(PlayerSpec player, BossSpec boss, EnemySpawnSpec enemySpawn)
        {
            if (player.MaxHealth <= 0) throw new ArgumentException("Player spec must have positive health.", nameof(player));
            Boss = boss ?? throw new ArgumentNullException(nameof(boss));
            EnemySpawn = enemySpawn ?? throw new ArgumentNullException(nameof(enemySpawn));
            Player = player;
        }

        public PlayerSpec Player { get; }
        public BossSpec Boss { get; }
        public EnemySpawnSpec EnemySpawn { get; }
        public EnemySpawnSpec Spawn => EnemySpawn;
    }

    /// <summary>
    /// Runtime dependency scope shared by all installers. Factories may add
    /// concrete adapters here, while feature modules only receive their ports.
    /// </summary>
    public sealed class GameCompositionContext : IDisposable
    {
        private readonly Dictionary<Type, object> services = new();
        private bool disposed;

        public GameCompositionContext(
            SceneBindings sceneBindings,
            ConfigCatalog configCatalog,
            GameSpecs specs,
            RuntimeMode runtimeMode,
            bool isDedicatedServer)
        {
            SceneBindings = sceneBindings;
            ConfigCatalog = configCatalog ?? throw new ArgumentNullException(nameof(configCatalog));
            Specs = specs ?? throw new ArgumentNullException(nameof(specs));
            RuntimeMode = runtimeMode;
            IsDedicatedServer = isDedicatedServer;
        }

        public SceneBindings SceneBindings { get; }
        public ConfigCatalog ConfigCatalog { get; }
        public GameSpecs Specs { get; }
        public RuntimeMode RuntimeMode { get; }
        public bool IsDedicatedServer { get; }
        public bool PresentationEnabled => !IsDedicatedServer;
        public bool UiEnabled => !IsDedicatedServer;
        public bool IsDisposed => disposed;

        public void Register<T>(T service) where T : class
        {
            ThrowIfDisposed();
            if (service == null) throw new ArgumentNullException(nameof(service));
            Type type = typeof(T);
            if (!services.TryAdd(type, service))
                throw new InvalidOperationException($"A service for {type.FullName} is already registered.");
        }

        public bool TryResolve<T>(out T service) where T : class
        {
            if (disposed)
            {
                service = null;
                return false;
            }

            if (services.TryGetValue(typeof(T), out object value))
            {
                service = (T)value;
                return true;
            }

            service = null;
            return false;
        }

        public bool Remove<T>() where T : class => services.Remove(typeof(T));

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            services.Clear();
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(GameCompositionContext));
        }
    }

    public interface IModuleFactory
    {
        IDisposable Create(GameCompositionContext context);
    }

    public sealed class DelegateModuleFactory : IModuleFactory
    {
        private readonly Func<GameCompositionContext, IDisposable> create;

        public DelegateModuleFactory(Func<GameCompositionContext, IDisposable> create)
        {
            this.create = create ?? throw new ArgumentNullException(nameof(create));
        }

        public IDisposable Create(GameCompositionContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return create(context);
        }
    }

    public interface IModuleInstaller : IDisposable
    {
        bool IsInstalled { get; }
        void Install(GameCompositionContext context);
    }

    internal sealed class EmptyModuleInstallation : IDisposable
    {
        public static readonly EmptyModuleInstallation Instance = new();
        private EmptyModuleInstallation() { }
        public void Dispose() { }
    }

    internal sealed class ActionModuleInstallation : IDisposable
    {
        private Action dispose;

        public ActionModuleInstallation(Action dispose)
        {
            this.dispose = dispose;
        }

        public void Dispose()
        {
            Action action = dispose;
            dispose = null;
            action?.Invoke();
        }
    }
}
