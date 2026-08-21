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
            // NGO observes Transform.SetParent even while offline.  A
            // NetworkObject with AutoObjectParentSync enabled rejects any
            // parent change before it is spawned or while its manager is not
            // listening, so keep pooled network instances at the scene root.
            if (offlineParent != null &&
                instance.GetComponentInChildren<NetworkObject>(true) == null)
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
        private readonly Stack<NetworkObject> available = new();
        private readonly HashSet<EntityId> activeEntityIds = new();
        private NetworkManager registeredManager;
        private int maxRetained;

        public int ActiveCount => activeEntityIds.Count;

        public PrefabPool(GameObject prefab, int prewarmCount, int maxRetained)
        {
            this.prefab = prefab;
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

        public bool TryRegister(NetworkManager manager)
        {
            if (manager == null || registeredManager == manager) return true;
            if (!manager.PrefabHandler.AddHandler(prefab, this)) return false;
            registeredManager = manager;
            return true;
        }

        public NetworkObject Take(Vector3 position, Quaternion rotation)
        {
            NetworkObject instance = null;
            while (available.Count > 0 && instance == null)
            {
                NetworkObject candidate = available.Pop();
                if (candidate == null) continue;
                // A pooled object must be an unspawned scene-root instance.
                // Calling Transform.SetParent on a spawned object or on an
                // unspawned object under a non-NetworkObject parent invokes
                // NGO's automatic parent-sync validation.
                if (candidate.IsSpawned || candidate.transform.parent != null)
                {
                    Object.Destroy(candidate.gameObject);
                    continue;
                }
                instance = candidate;
            }
            instance ??= CreateInstance();
            if (instance == null) return null;

            Transform instanceTransform = instance.transform;
            if (instance.IsSpawned || instanceTransform.parent != null)
            {
                Object.Destroy(instance.gameObject);
                return null;
            }
            instanceTransform.SetPositionAndRotation(position, rotation);
            NotifyTaken(instance.gameObject);
            instance.gameObject.SetActive(true);
            activeEntityIds.Add(instance.gameObject.GetEntityId());
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
            if (instance == null || !activeEntityIds.Remove(instance.gameObject.GetEntityId()))
                return false;

            poolsByEntityId.Remove(instance.gameObject.GetEntityId());
            NotifyReturned(instance.gameObject);
            instance.gameObject.SetActive(false);
            // NetworkObject instances intentionally remain at the scene root
            // while pooled.  Reparenting them under root would trigger NGO's
            // "manager is not listening" or "can only be re-parented after
            // being spawned" errors during Offline and shutdown paths.
            if (instance.IsSpawned || instance.transform.parent != null)
            {
                Object.Destroy(instance.gameObject);
                return true;
            }
            if (available.Count < maxRetained)
                available.Push(instance);
            else
                Object.Destroy(instance.gameObject);
            return true;
        }

        private NetworkObject CreateInstance()
        {
            // Keep NetworkObject instances at the scene root.  Parenting an
            // unspawned instance under the pool marker causes NGO's automatic
            // parent-sync callback to reject it when Offline or before the
            // NetworkManager starts listening.
            GameObject instance = Object.Instantiate(prefab);
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
    private static readonly Dictionary<EntityId, PrefabPool> poolsByEntityId = new();
    private static Transform poolRoot;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        poolsByPrefab.Clear();
        poolsByEntityId.Clear();
        poolRoot = null;
    }

    public static void Configure(GameObject prefab, int prewarmCount, int maxRetained)
    {
        if (prefab == null || prefab.GetComponent<NetworkObject>() == null) return;
        if (poolsByPrefab.TryGetValue(prefab, out PrefabPool existing))
        {
            existing.IncreaseCapacity(Mathf.Max(1, maxRetained));
            if (!existing.TryRegister(NetworkManager.Singleton))
                Debug.LogWarning($"[Network Pool] A handler is already registered for '{prefab.name}'.", prefab);
            return;
        }

        NetworkManager manager = NetworkManager.Singleton;
        EnsureRoot();
        PrefabPool pool = new(prefab, prewarmCount, maxRetained);
        if (!pool.TryRegister(manager))
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
        poolsByEntityId[instance.gameObject.GetEntityId()] = pool;
        return instance.gameObject;
    }

    public static bool TryReturn(GameObject instance)
    {
        if (instance == null ||
            !poolsByEntityId.TryGetValue(instance.GetEntityId(), out PrefabPool pool))
            return false;
        poolsByEntityId.Remove(instance.GetEntityId());
        return pool.Return(instance.GetComponent<NetworkObject>());
    }

    public static int GetActiveCount(GameObject prefab)
    {
        return prefab != null && poolsByPrefab.TryGetValue(prefab, out PrefabPool pool)
            ? pool.ActiveCount
            : 0;
    }

    private static void EnsureRoot()
    {
        if (poolRoot != null) return;
        GameObject root = new("[NetworkObjectPool]");
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
