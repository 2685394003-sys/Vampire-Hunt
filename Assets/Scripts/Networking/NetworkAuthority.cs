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

        GameObject instance = Object.Instantiate(prefab, position, rotation);

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

        Object.Destroy(instance);
    }
}
