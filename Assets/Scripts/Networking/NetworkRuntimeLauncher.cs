using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using VampireHunt.Bootstrap;

public enum NetworkStartMode
{
    None,
    Host,
    Client,
    Server
}

/// <summary>
/// Starts the same server-authoritative game over direct IP today. A Relay
/// adapter can configure UnityTransport first and then call StartHost/Client;
/// a dedicated build uses StartServer without changing gameplay code.
/// </summary>
[DefaultExecutionOrder(-1000)]
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkManager))]
[RequireComponent(typeof(UnityTransport))]
public sealed class NetworkRuntimeLauncher : MonoBehaviour
{
    [SerializeField] private NetworkStartMode editorStartMode = NetworkStartMode.None;
    [SerializeField] private NetworkStartMode playerBuildStartMode = NetworkStartMode.None;
    [SerializeField] private string address = "127.0.0.1";
    [SerializeField, Min(1)] private ushort port = 7777;
    [SerializeField] private bool batchModeStartsServer = true;
    [SerializeField] private bool showDevelopmentMenu = true;
    [Header("Composition (required at runtime)")]
    [SerializeField] private SceneBindings sceneBindings;
    [SerializeField] private ConfigCatalog configCatalog;
    [SerializeField] private MonoBehaviour compositionFactoryProvider;

    private NetworkManager manager;
    private UnityTransport transport;
    private GameCompositionRoot compositionRoot;
    private CompositionFactorySet configuredFactories;
    private ICompositionFactoryProvider configuredFactoryProvider;
    private RuntimeMode composedRuntimeMode;
    private bool composedAsDedicatedServer;

    public string Address => address;
    public ushort Port => port;
    public bool IsListening => manager != null && manager.IsListening;
    public bool IsComposed => compositionRoot != null && compositionRoot.IsComposed;
    public GameCompositionRoot CompositionRoot => compositionRoot;

    public event Action<NetworkStartMode, bool> StartCompleted;

    /// <summary>
    /// Test/integration seam for the concrete adapter graph. Production
    /// scenes should assign a MonoBehaviour implementing
    /// ICompositionFactoryProvider instead.
    /// </summary>
    public void ConfigureComposition(
        SceneBindings bindings,
        ConfigCatalog catalog,
        CompositionFactorySet factories)
    {
        EnsureCompositionConfigurationIsMutable();
        sceneBindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        configCatalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        configuredFactories = factories ?? throw new ArgumentNullException(nameof(factories));
        configuredFactoryProvider = null;
    }

    /// <summary>Explicit provider seam for runtime adapter construction.</summary>
    public void ConfigureCompositionProvider(
        SceneBindings bindings,
        ConfigCatalog catalog,
        ICompositionFactoryProvider provider)
    {
        EnsureCompositionConfigurationIsMutable();
        sceneBindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
        configCatalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        configuredFactoryProvider = provider ?? throw new ArgumentNullException(nameof(provider));
        configuredFactories = null;
    }

    /// <summary>
    /// Composes exactly once for the requested launcher mode. Network start
    /// methods call this same entry point, so no mode can bypass validation.
    /// </summary>
    public void ComposeRuntime(NetworkStartMode requestedMode = NetworkStartMode.None)
    {
        EnsureComposition(requestedMode);
    }

    private void Awake()
    {
        manager = GetComponent<NetworkManager>();
        transport = GetComponent<UnityTransport>();
    }

    private void Start()
    {
        if (manager == null || manager.IsListening) return;

        string[] args = Environment.GetCommandLineArgs();
        address = GetArgument(args, "-address", address);
        if (ushort.TryParse(GetArgument(args, "-port", port.ToString()), out ushort parsedPort))
            port = parsedPort;

        NetworkStartMode mode = ResolveStartMode(args);
        switch (mode)
        {
            case NetworkStartMode.None:
                EnsureComposition(NetworkStartMode.None);
                break;
            case NetworkStartMode.Host:
                StartHost();
                break;
            case NetworkStartMode.Client:
                StartClient(address);
                break;
            case NetworkStartMode.Server:
                StartServer();
                break;
        }
    }

    public bool StartHost()
    {
        EnsureComposition(NetworkStartMode.Host);
        if (!CanStart(NetworkStartMode.Host)) return false;
        transport.SetConnectionData(address, port, "0.0.0.0");
        bool started = manager.StartHost();
        StartCompleted?.Invoke(NetworkStartMode.Host, started);
        return started;
    }

    public bool StartClient(string serverAddress = null)
    {
        if (!string.IsNullOrWhiteSpace(serverAddress)) address = serverAddress.Trim();
        EnsureComposition(NetworkStartMode.Client);
        if (!CanStart(NetworkStartMode.Client)) return false;
        transport.SetConnectionData(address, port);
        bool started = manager.StartClient();
        StartCompleted?.Invoke(NetworkStartMode.Client, started);
        return started;
    }

    public bool StartServer()
    {
        EnsureComposition(NetworkStartMode.Server);
        if (!CanStart(NetworkStartMode.Server)) return false;
        transport.SetConnectionData(address, port, "0.0.0.0");
        bool started = manager.StartServer();
        StartCompleted?.Invoke(NetworkStartMode.Server, started);
        return started;
    }

    public void Shutdown()
    {
        if (manager != null && manager.IsListening) manager.Shutdown();
        DisposeComposition();
    }

    public void SetEndpoint(string serverAddress, ushort serverPort)
    {
        if (manager != null && manager.IsListening)
            throw new InvalidOperationException("Cannot change endpoint while networking is active.");

        if (!string.IsNullOrWhiteSpace(serverAddress)) address = serverAddress.Trim();
        port = serverPort == 0 ? (ushort)7777 : serverPort;
    }

    private bool CanStart(NetworkStartMode requestedMode)
    {
        if (manager == null || transport == null || manager.IsListening)
        {
            StartCompleted?.Invoke(requestedMode, false);
            return false;
        }

        if (manager.NetworkConfig.PlayerPrefab == null && requestedMode != NetworkStartMode.Server)
        {
            Debug.LogError("[Network] NetworkManager 尚未配置 PlayerPrefab。", this);
            StartCompleted?.Invoke(requestedMode, false);
            return false;
        }
        return true;
    }

    private void EnsureComposition(NetworkStartMode requestedMode)
    {
        RuntimeMode runtimeMode = requestedMode == NetworkStartMode.None
            ? RuntimeMode.Offline
            : RuntimeMode.Netcode;
        bool dedicatedServer = requestedMode == NetworkStartMode.Server;

        if (compositionRoot != null)
        {
            if (!compositionRoot.IsComposed)
                throw new InvalidOperationException("The runtime composition root exists but is not composed.");
            if (composedRuntimeMode != runtimeMode || composedAsDedicatedServer != dedicatedServer)
            {
                throw new InvalidOperationException(
                    "The runtime composition is already bound to a different launcher mode; reload the scene before changing mode.");
            }
            return;
        }

        if (sceneBindings == null)
            throw new InvalidOperationException("NetworkRuntimeLauncher requires SceneBindings before startup.");
        if (configCatalog == null)
            throw new InvalidOperationException("NetworkRuntimeLauncher requires ConfigCatalog before startup.");

        CompositionFactorySet factories = configuredFactories;
        if (factories == null)
        {
            ICompositionFactoryProvider provider = configuredFactoryProvider;
            if (provider == null && compositionFactoryProvider != null)
            {
                provider = compositionFactoryProvider as ICompositionFactoryProvider;
                if (provider == null)
                {
                    throw new InvalidOperationException(
                        "The configured composition provider must implement ICompositionFactoryProvider.");
                }
            }

            if (provider == null)
            {
                throw new InvalidOperationException(
                    "NetworkRuntimeLauncher requires explicit gameplay/adapter factories; " +
                    "empty module installation is not supported.");
            }

            factories = provider.CreateFactories();
            if (factories == null)
                throw new InvalidOperationException("The composition factory provider returned null.");
        }

        GameCompositionRoot next = GameCompositionRoot.Create(factories);
        try
        {
            next.Compose(sceneBindings, configCatalog, runtimeMode, dedicatedServer);
            compositionRoot = next;
            composedRuntimeMode = runtimeMode;
            composedAsDedicatedServer = dedicatedServer;
        }
        catch
        {
            next.Dispose();
            throw;
        }
    }

    private void EnsureCompositionConfigurationIsMutable()
    {
        if (compositionRoot != null)
            throw new InvalidOperationException("Composition dependencies cannot change after composition.");
    }

    private void DisposeComposition()
    {
        GameCompositionRoot current = compositionRoot;
        compositionRoot = null;
        current?.Dispose();
    }

    private NetworkStartMode ResolveStartMode(string[] args)
    {
        if (HasFlag(args, "-server") || HasFlag(args, "-dedicatedServer"))
            return NetworkStartMode.Server;
        if (HasFlag(args, "-host")) return NetworkStartMode.Host;
        if (HasFlag(args, "-client")) return NetworkStartMode.Client;

#if UNITY_SERVER
        return NetworkStartMode.Server;
#else
        if (Application.isBatchMode && batchModeStartsServer)
            return NetworkStartMode.Server;
        return Application.isEditor ? editorStartMode : playerBuildStartMode;
#endif
    }

    private static bool HasFlag(string[] args, string flag)
    {
        foreach (string arg in args)
            if (string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static string GetArgument(string[] args, string key, string fallback)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
        return fallback;
    }

    private void OnGUI()
    {
        if (!showDevelopmentMenu || Application.isBatchMode || manager == null) return;
        GUILayout.BeginArea(new Rect(12f, 12f, 260f, 190f), GUI.skin.box);
        GUILayout.Label(manager.IsListening
            ? $"Network: {(manager.IsHost ? "Host" : manager.IsServer ? "Server" : "Client")}"
            : "Network: Offline");
        address = GUILayout.TextField(address);
        string portText = GUILayout.TextField(port.ToString());
        if (ushort.TryParse(portText, out ushort parsedPort) && parsedPort > 0) port = parsedPort;

        GUI.enabled = !manager.IsListening;
        if (GUILayout.Button("Start Host")) StartHost();
        if (GUILayout.Button("Start Client")) StartClient(address);
        if (GUILayout.Button("Start Server")) StartServer();
        GUI.enabled = manager.IsListening;
        if (GUILayout.Button("Shutdown")) Shutdown();
        GUI.enabled = true;
        GUILayout.EndArea();
    }

    private void OnDestroy()
    {
        DisposeComposition();
    }
}
