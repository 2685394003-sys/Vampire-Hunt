using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime registry used by server-side AI and client-side UI. It replaces all
/// "find the one player" assumptions and is safe with late joins/disconnects.
/// </summary>
public static class NetworkPlayerRegistry
{
    private static readonly HashSet<PlayerNetworkState> Players = new();

    public static int Count => Players.Count;

    public static void Register(PlayerNetworkState player)
    {
        if (player != null)
        {
            Players.Add(player);
        }
    }

    public static void Unregister(PlayerNetworkState player)
    {
        if (player != null)
        {
            Players.Remove(player);
        }
    }

    public static PlayerNetworkState GetLocalPlayer()
    {
        PruneDestroyed();
        foreach (PlayerNetworkState player in Players)
        {
            if (player != null && NetworkAuthority.IsOwnerOrOffline(player))
            {
                return player;
            }
        }

        return null;
    }

    public static PlayerNetworkState GetClosestAlive(Vector3 position)
    {
        PruneDestroyed();

        PlayerNetworkState closest = null;
        float bestSqrDistance = float.PositiveInfinity;
        foreach (PlayerNetworkState player in Players)
        {
            if (player == null || !player.IsAlive || !player.gameObject.activeInHierarchy)
            {
                continue;
            }

            float sqrDistance = (player.transform.position - position).sqrMagnitude;
            if (sqrDistance < bestSqrDistance)
            {
                bestSqrDistance = sqrDistance;
                closest = player;
            }
        }

        return closest;
    }

    public static void GetAlivePlayers(List<PlayerNetworkState> buffer)
    {
        buffer.Clear();
        PruneDestroyed();
        foreach (PlayerNetworkState player in Players)
        {
            if (player != null && player.IsAlive && player.gameObject.activeInHierarchy)
            {
                buffer.Add(player);
            }
        }
    }

    /// <summary>
    /// Copies every currently registered player into the caller-owned buffer.
    /// Unlike GetAlivePlayers, this includes dead players so shared run rewards
    /// remain fair while a teammate is waiting to revive.
    /// </summary>
    public static void GetPlayers(List<PlayerNetworkState> buffer)
    {
        buffer.Clear();
        PruneDestroyed();
        foreach (PlayerNetworkState player in Players)
        {
            if (player != null)
            {
                buffer.Add(player);
            }
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeState()
    {
        Players.Clear();
    }

    private static void PruneDestroyed()
    {
        Players.RemoveWhere(player => player == null);
    }
}
