using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Central authority policy for gameplay code.
/// A scene without an active NetworkManager behaves like an offline server so the
/// existing single-player workflow stays usable while networking is introduced.
/// </summary>
public static class NetworkAuthority
{
    public static bool IsNetworkActive =>
        NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

    public static bool IsServerOrOffline(NetworkBehaviour behaviour = null)
    {
        if (!IsNetworkActive)
        {
            return true;
        }

        return behaviour != null
            ? behaviour.IsServer
            : NetworkManager.Singleton.IsServer;
    }

    public static bool IsOwnerOrOffline(NetworkBehaviour behaviour)
    {
        return !IsNetworkActive || (behaviour != null && behaviour.IsOwner);
    }
}

/// <summary>
/// Server-only spawn/despawn helpers. Network sessions never silently create a
/// local-only gameplay object because that would immediately diverge clients.
/// </summary>
public static class NetworkSpawnUtility
{
    public static void ConfigurePool(GameObject prefab, int prewarmCount, int maxRetained)
    {
        NetworkObjectPoolRegistry.Configure(prefab, prewarmCount, maxRetained);
    }

    public static int GetActivePooledCount(GameObject prefab)
    {
        return NetworkObjectPoolRegistry.GetActiveCount(prefab);
    }

    public static GameObject Spawn(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        Transform offlineParent = null)
    {
        if (prefab == null || !NetworkAuthority.IsServerOrOffline())
        {
            return null;
        }

        GameObject instance = NetworkObjectPoolRegistry.TryTake(prefab, position, rotation)
            ?? Object.Instantiate(prefab, position, rotation);

        if (!NetworkAuthority.IsNetworkActive)
        {
            if (offlineParent != null)
            {
                instance.transform.SetParent(offlineParent, true);
            }
            return instance;
        }

        NetworkObject networkObject = instance.GetComponent<NetworkObject>();
        if (networkObject == null)
        {
            Debug.LogError(
                $"[Network] Server tried to spawn '{prefab.name}' without a NetworkObject. " +
                "The instance was discarded to prevent client divergence.",
                prefab);
            if (!NetworkObjectPoolRegistry.TryReturn(instance))
                Object.Destroy(instance);
            return null;
        }

        networkObject.Spawn(true);
        return instance;
    }

    public static void Despawn(GameObject instance)
    {
        if (instance == null || !NetworkAuthority.IsServerOrOffline())
        {
            return;
        }

        NetworkObject networkObject = instance.GetComponent<NetworkObject>();
        if (NetworkAuthority.IsNetworkActive &&
            networkObject != null &&
            networkObject.IsSpawned)
        {
            networkObject.Despawn(true);
            return;
        }

        if (!NetworkObjectPoolRegistry.TryReturn(instance))
            Object.Destroy(instance);
    }
}

public interface INetworkPoolLifecycle
{
    void OnTakenFromNetworkPool();
    void OnReturnedToNetworkPool();
}

internal static class NetworkObjectPoolRegistry
{
    private sealed class PrefabPool : INetworkPrefabInstanceHandler
    {
        private readonly GameObject prefab;
        private readonly Transform root;
        private readonly Stack<NetworkObject> available = new();
        private readonly HashSet<int> activeInstanceIds = new();
        private int maxRetained;

        public int ActiveCount => activeInstanceIds.Count;

        public PrefabPool(GameObject prefab, Transform root, int prewarmCount, int maxRetained)
        {
            this.prefab = prefab;
            this.root = root;
            this.maxRetained = Mathf.Max(1, maxRetained);
            int count = Mathf.Clamp(prewarmCount, 0, this.maxRetained);
            for (int i = 0; i < count; i++)
            {
                NetworkObject instance = CreateInstance();
                if (instance != null) available.Push(instance);
            }
        }

        public void IncreaseCapacity(int requestedMaxRetained)
        {
            maxRetained = Mathf.Max(maxRetained, requestedMaxRetained);
        }

        public NetworkObject Take(Vector3 position, Quaternion rotation)
        {
            NetworkObject instance = null;
            while (available.Count > 0 && instance == null)
                instance = available.Pop();
            instance ??= CreateInstance();
            if (instance == null) return null;

            Transform instanceTransform = instance.transform;
            instanceTransform.SetParent(null, false);
            instanceTransform.SetPositionAndRotation(position, rotation);
            NotifyTaken(instance.gameObject);
            instance.gameObject.SetActive(true);
            activeInstanceIds.Add(instance.gameObject.GetInstanceID());
            return instance;
        }

        public NetworkObject Instantiate(ulong ownerClientId, Vector3 position, Quaternion rotation)
        {
            return Take(position, rotation);
        }

        public void Destroy(NetworkObject networkObject)
        {
            Return(networkObject);
        }

        public bool Return(NetworkObject instance)
        {
            if (instance == null || !activeInstanceIds.Remove(instance.gameObject.GetInstanceID()))
                return false;

            poolsByInstanceId.Remove(instance.gameObject.GetInstanceID());
            NotifyReturned(instance.gameObject);
            instance.gameObject.SetActive(false);
            instance.transform.SetParent(root, false);
            if (available.Count < maxRetained)
                available.Push(instance);
            else
                Object.Destroy(instance.gameObject);
            return true;
        }

        private NetworkObject CreateInstance()
        {
            GameObject instance = Object.Instantiate(prefab, root);
            NetworkObject networkObject = instance.GetComponent<NetworkObject>();
            if (networkObject == null)
            {
                Debug.LogError($"[Network Pool] '{prefab.name}' is missing NetworkObject.", prefab);
                Object.Destroy(instance);
                return null;
            }
            instance.SetActive(false);
            return networkObject;
        }
    }

    private static readonly Dictionary<GameObject, PrefabPool> poolsByPrefab = new();
    private static readonly Dictionary<int, PrefabPool> poolsByInstanceId = new();
    private static Transform poolRoot;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        poolsByPrefab.Clear();
        poolsByInstanceId.Clear();
        poolRoot = null;
    }

    public static void Configure(GameObject prefab, int prewarmCount, int maxRetained)
    {
        if (prefab == null || prefab.GetComponent<NetworkObject>() == null) return;
        if (poolsByPrefab.TryGetValue(prefab, out PrefabPool existing))
        {
            existing.IncreaseCapacity(Mathf.Max(1, maxRetained));
            return;
        }

        NetworkManager manager = NetworkManager.Singleton;
        if (manager == null)
        {
            Debug.LogWarning("[Network Pool] NetworkManager is not ready; pool setup was skipped.", prefab);
            return;
        }

        EnsureRoot(manager.transform);
        PrefabPool pool = new(prefab, poolRoot, prewarmCount, maxRetained);
        if (!manager.PrefabHandler.AddHandler(prefab, pool))
        {
            Debug.LogWarning($"[Network Pool] A handler is already registered for '{prefab.name}'.", prefab);
            return;
        }
        poolsByPrefab.Add(prefab, pool);
    }

    public static GameObject TryTake(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (!poolsByPrefab.TryGetValue(prefab, out PrefabPool pool)) return null;
        NetworkObject instance = pool.Take(position, rotation);
        if (instance == null) return null;
        poolsByInstanceId[instance.gameObject.GetInstanceID()] = pool;
        return instance.gameObject;
    }

    public static bool TryReturn(GameObject instance)
    {
        if (instance == null ||
            !poolsByInstanceId.TryGetValue(instance.GetInstanceID(), out PrefabPool pool))
            return false;
        poolsByInstanceId.Remove(instance.GetInstanceID());
        return pool.Return(instance.GetComponent<NetworkObject>());
    }

    public static int GetActiveCount(GameObject prefab)
    {
        return prefab != null && poolsByPrefab.TryGetValue(prefab, out PrefabPool pool)
            ? pool.ActiveCount
            : 0;
    }

    private static void EnsureRoot(Transform managerTransform)
    {
        if (poolRoot != null) return;
        GameObject root = new("[NetworkObjectPool]");
        root.transform.SetParent(managerTransform, false);
        poolRoot = root.transform;
    }

    private static void NotifyTaken(GameObject instance)
    {
        foreach (MonoBehaviour behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true))
            if (behaviour is INetworkPoolLifecycle lifecycle) lifecycle.OnTakenFromNetworkPool();
    }

    private static void NotifyReturned(GameObject instance)
    {
        foreach (MonoBehaviour behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true))
            if (behaviour is INetworkPoolLifecycle lifecycle) lifecycle.OnReturnedToNetworkPool();
    }
}
