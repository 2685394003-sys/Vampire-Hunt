using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class EnemyHealth : NetworkBehaviour
{
    public GameObject healthPackPrefab;
    [Range(0f, 1f)] public float healthPackDropChance = 0.2f;
    public GameObject coinPrefab;
    [Range(0f, 1f)] public float coinDropChance = 0.5f;

    private readonly NetworkVariable<int> networkHealth = new(
        1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> networkDead = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private EnemyHurtFlash hurtFlash;
    private int offlineHealth;
    private bool offlineDead;

    public int CurrentHealth => UseNetworkValues ? networkHealth.Value : offlineHealth;
    public bool IsDead => UseNetworkValues ? networkDead.Value : offlineDead;
    private bool UseNetworkValues => NetworkAuthority.IsNetworkActive && IsSpawned;

    private void Awake()
    {
        if (GetComponent<NetworkObject>() == null && !NetworkAuthority.IsNetworkActive)
        {
            gameObject.AddComponent<NetworkObject>();
        }

        hurtFlash = GetComponent<EnemyHurtFlash>();
        offlineHealth = GetConfiguredMaxHealth();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            networkHealth.Value = GetConfiguredMaxHealth();
            networkDead.Value = false;
        }
    }

    public void ChangeEnemyHealth(int amount)
    {
        if (!NetworkAuthority.IsServerOrOffline(this) || amount <= 0 || IsDead)
        {
            return;
        }

        SetHealth(Mathf.Max(0, CurrentHealth - amount));
        if (NetworkAuthority.IsNetworkActive)
        {
            PlayHurtFeedbackRpc();
        }
        else
        {
            hurtFlash?.StartHurtFlash();
        }

        if (CurrentHealth <= 0)
        {
            ServerDie();
        }
    }

    private void ServerDie()
    {
        if (IsDead || !NetworkAuthority.IsServerOrOffline(this))
        {
            return;
        }

        SetDead(true);
        ServerTryDrop(healthPackPrefab, healthPackDropChance);
        ServerTryDrop(coinPrefab, coinDropChance);
        NetworkSpawnUtility.Despawn(gameObject);
    }

    private void ServerTryDrop(GameObject prefab, float chance)
    {
        if (prefab == null || Random.value > chance)
        {
            return;
        }

        Vector2 offset = Random.insideUnitCircle * 0.5f;
        Vector3 position = transform.position + new Vector3(offset.x, 0f, offset.y);
        NetworkSpawnUtility.Spawn(prefab, position, Quaternion.identity);
    }

    [Rpc(SendTo.ClientsAndHost)]
    private void PlayHurtFeedbackRpc()
    {
        hurtFlash?.StartHurtFlash();
    }

    private void SetHealth(int value)
    {
        if (UseNetworkValues) networkHealth.Value = value;
        else offlineHealth = value;
    }

    private void SetDead(bool value)
    {
        if (UseNetworkValues) networkDead.Value = value;
        else offlineDead = value;
    }

    private static int GetConfiguredMaxHealth()
    {
        return Mathf.Max(1, StatsManager.Instance != null ? StatsManager.Instance.enemymaxHealth : 1);
    }
}
