using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Registers Boss-owned network prefabs before a session starts.
/// Every peer loads this scene component, so the registration remains
/// deterministic without changing the project's shared NetworkPrefabsList.
/// </summary>
[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
[RequireComponent(typeof(BossConfig))]
public sealed class BossNetworkPrefabRegistrar : MonoBehaviour
{
    private BossConfig config;
    private NetworkManager registeredManager;
    private GameObject registeredProjectile;

    private void Awake()
    {
        config = GetComponent<BossConfig>();
        TryRegister();
    }

    private void Start()
    {
        // Fallback for scenes where NetworkManager is created later in Awake.
        TryRegister();
    }

    private void TryRegister()
    {
        GameObject projectile = config != null ? config.projectilePrefab : null;
        NetworkManager manager = NetworkManager.Singleton;
        if (projectile == null || manager == null ||
            (registeredManager == manager && registeredProjectile == projectile))
        {
            return;
        }

        if (projectile.GetComponent<NetworkObject>() == null)
        {
            Debug.LogError(
                $"[Boss] 联机投射物 '{projectile.name}' 缺少 NetworkObject。",
                projectile);
            return;
        }

        if (manager.IsListening)
        {
            Debug.LogError(
                "[Boss] NetworkManager 已启动，无法再注册投射物；请确保场景中的 " +
                "BossNetworkPrefabRegistrar 在启动 Host/Client 前保持启用。",
                this);
            return;
        }

        try
        {
            if (!manager.NetworkConfig.Prefabs.Contains(projectile))
            {
                manager.AddNetworkPrefab(projectile);
            }
            registeredManager = manager;
            registeredProjectile = projectile;
        }
        catch (Exception exception)
        {
            Debug.LogError(
                $"[Boss] 注册联机投射物 '{projectile.name}' 失败：{exception.Message}",
                this);
        }
    }
}
