using System;

namespace VampireHunt.Bootstrap
{
    public abstract class ModuleInstallerBase : IModuleInstaller
    {
        private IDisposable installation;

        public bool IsInstalled => installation != null;

        public abstract void Install(GameCompositionContext context);

        protected void InstallCore(GameCompositionContext context, IModuleFactory factory)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (IsInstalled) throw new InvalidOperationException("The module installer is already installed.");

            installation = factory == null
                ? EmptyModuleInstallation.Instance
                : factory.Create(context) ?? EmptyModuleInstallation.Instance;
        }

        public void Dispose()
        {
            IDisposable current = installation;
            installation = null;
            current?.Dispose();
        }
    }

    /// <summary>Installs the shared Application/Domain graph for either runtime mode.</summary>
    public sealed class GameplayModuleInstaller : ModuleInstallerBase
    {
        private readonly IModuleFactory factory;

        public GameplayModuleInstaller(IModuleFactory factory = null)
        {
            this.factory = factory;
        }

        public override void Install(GameCompositionContext context) => InstallCore(context, factory);
    }

    /// <summary>
    /// Selects the Offline or Netcode adapter factory without changing the
    /// Application/Domain graph.
    /// </summary>
    public sealed class NetcodeModuleInstaller : ModuleInstallerBase
    {
        private readonly IModuleFactory offlineFactory;
        private readonly IModuleFactory netcodeFactory;

        public NetcodeModuleInstaller(
            IModuleFactory offlineFactory = null,
            IModuleFactory netcodeFactory = null)
        {
            this.offlineFactory = offlineFactory;
            this.netcodeFactory = netcodeFactory;
        }

        public override void Install(GameCompositionContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (context.RuntimeMode == RuntimeMode.Netcode && netcodeFactory == null)
            {
                throw new InvalidOperationException(
                    "RuntimeMode.Netcode requires an injected Netcode module factory.");
            }

            InstallCore(
                context,
                context.RuntimeMode == RuntimeMode.Netcode ? netcodeFactory : offlineFactory);
        }
    }

    /// <summary>Installs client-facing event/view adapters when presentation is enabled.</summary>
    public sealed class PresentationModuleInstaller : ModuleInstallerBase
    {
        private readonly IModuleFactory factory;

        public PresentationModuleInstaller(IModuleFactory factory = null)
        {
            this.factory = factory;
        }

        public override void Install(GameCompositionContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (!context.PresentationEnabled) return;
            InstallCore(context, factory);
        }
    }

    /// <summary>Installs UI command/read-model presenters, skipped on dedicated servers.</summary>
    public sealed class UiModuleInstaller : ModuleInstallerBase
    {
        private readonly IModuleFactory factory;

        public UiModuleInstaller(IModuleFactory factory = null)
        {
            this.factory = factory;
        }

        public override void Install(GameCompositionContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (!context.UiEnabled) return;
            InstallCore(context, factory);
        }
    }
}
