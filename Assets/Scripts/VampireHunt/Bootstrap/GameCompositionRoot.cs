using System;

namespace VampireHunt.Bootstrap
{
    /// <summary>
    /// The sole composition root. It creates the immutable config snapshot,
    /// installs modules in dependency order, and disposes them in reverse order.
    /// </summary>
    public sealed class GameCompositionRoot : IDisposable
    {
        private readonly GameplayModuleInstaller gameplayInstaller;
        private readonly NetcodeModuleInstaller netcodeInstaller;
        private readonly PresentationModuleInstaller presentationInstaller;
        private readonly UiModuleInstaller uiInstaller;
        private GameCompositionContext context;
        private bool disposed;

        public GameCompositionRoot(
            GameplayModuleInstaller gameplayInstaller = null,
            NetcodeModuleInstaller netcodeInstaller = null,
            PresentationModuleInstaller presentationInstaller = null,
            UiModuleInstaller uiInstaller = null)
        {
            this.gameplayInstaller = gameplayInstaller ?? new GameplayModuleInstaller();
            this.netcodeInstaller = netcodeInstaller ?? new NetcodeModuleInstaller();
            this.presentationInstaller = presentationInstaller ?? new PresentationModuleInstaller();
            this.uiInstaller = uiInstaller ?? new UiModuleInstaller();
        }

        public GameplayModuleInstaller GameplayInstaller => gameplayInstaller;
        public NetcodeModuleInstaller NetcodeInstaller => netcodeInstaller;
        public PresentationModuleInstaller PresentationInstaller => presentationInstaller;
        public UiModuleInstaller UiInstaller => uiInstaller;
        public GameCompositionContext Context => context;
        public bool IsComposed => context != null;
        public bool IsDisposed => disposed;

        public void Compose(SceneBindings sceneBindings, ConfigCatalog configCatalog)
        {
            Compose(sceneBindings, configCatalog, RuntimeMode.Offline, false);
        }

        public void Compose(
            SceneBindings sceneBindings,
            ConfigCatalog configCatalog,
            RuntimeMode runtimeMode,
            bool isDedicatedServer = false)
        {
            if (sceneBindings == null) throw new ArgumentNullException(nameof(sceneBindings));
            sceneBindings.Validate(!isDedicatedServer);
            ComposeCore(sceneBindings, configCatalog, runtimeMode, isDedicatedServer);
        }

        /// <summary>
        /// Headless/test overload. The normal scene-bound entry point above is
        /// preferred for a client, while dedicated server startup can omit a
        /// camera and all presentation scene references.
        /// </summary>
        public void Compose(
            ConfigCatalog configCatalog,
            RuntimeMode runtimeMode = RuntimeMode.Offline,
            bool isDedicatedServer = false)
        {
            ComposeCore(null, configCatalog, runtimeMode, isDedicatedServer);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;

            // Each installer is idempotent; disposing all four also cleans up
            // a partially-composed graph after a factory failure.
            uiInstaller.Dispose();
            presentationInstaller.Dispose();
            netcodeInstaller.Dispose();
            gameplayInstaller.Dispose();

            context?.Dispose();
            context = null;
        }

        private void ComposeCore(
            SceneBindings sceneBindings,
            ConfigCatalog configCatalog,
            RuntimeMode runtimeMode,
            bool isDedicatedServer)
        {
            ThrowIfDisposed();
            if (context != null) throw new InvalidOperationException("The composition root is already composed.");
            if (runtimeMode != RuntimeMode.Offline && runtimeMode != RuntimeMode.Netcode)
                throw new ArgumentOutOfRangeException(nameof(runtimeMode));
            if (configCatalog == null) throw new ArgumentNullException(nameof(configCatalog));

            GameSpecs specs = configCatalog.BuildSpecs();
            GameCompositionContext next = new(
                sceneBindings,
                configCatalog,
                specs,
                runtimeMode,
                isDedicatedServer);

            try
            {
                gameplayInstaller.Install(next);
                netcodeInstaller.Install(next);
                if (!isDedicatedServer)
                {
                    presentationInstaller.Install(next);
                    uiInstaller.Install(next);
                }

                context = next;
            }
            catch
            {
                uiInstaller.Dispose();
                presentationInstaller.Dispose();
                netcodeInstaller.Dispose();
                gameplayInstaller.Dispose();
                next.Dispose();
                throw;
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(GameCompositionRoot));
        }
    }
}
