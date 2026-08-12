using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class EnemyHealth : NetworkBehaviour
{
    [SerializeField] private EnemyStatsConfig stats;
    public GameObject healthPackPrefab;
    public GameObject redResourcePrefab;
    public GameObject coinPrefab;

    private readonly NetworkVariable<int> networkHealth = new(
        1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> networkDead = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private EnemyHurtFlash hurtFlash;
    private int offlineHealth;
    private bool offlineDead;
    private int cachedMaxHealth;

    public int CurrentHealth => UseNetworkValues ? networkHealth.Value : offlineHealth;
    public bool IsDead => UseNetworkValues ? networkDead.Value : offlineDead;
    public EnemyStatsConfig Config => stats != null ? stats : stats = EnemyStatsConfig.LoadDefault();
    private bool UseNetworkValues => NetworkAuthority.IsNetworkActive && IsSpawned;

    private void Awake()
    {
        if (GetComponent<NetworkObject>() == null && !NetworkAuthority.IsNetworkActive)
        {
            gameObject.AddComponent<NetworkObject>();
        }

        hurtFlash = GetComponent<EnemyHurtFlash>();
        cachedMaxHealth = GetConfiguredMaxHealth();
        offlineHealth = cachedMaxHealth;
    }

    private void OnEnable() => EnemyRunStats.StatChanged += HandleRunStatChanged;
    private void OnDisable() => EnemyRunStats.StatChanged -= HandleRunStatChanged;

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            cachedMaxHealth = GetConfiguredMaxHealth();
            networkHealth.Value = cachedMaxHealth;
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
        EnemyStatsConfig config = Config;
        ServerTryDrop(healthPackPrefab, config != null ? config.healthPackDropChance : 0f);
        ServerDropCount(redResourcePrefab, config != null ? config.redResourceDropAmount : 0);
        ServerDropCount(coinPrefab, config != null ? config.coinDropAmount : 0);
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

    private void ServerDropCount(GameObject prefab, int count)
    {
        if (prefab == null) return;
        for (int index = 0; index < Mathf.Max(0, count); index++)
        {
            Vector2 offset = Random.insideUnitCircle * 0.5f;
            Vector3 position = transform.position + new Vector3(offset.x, 0f, offset.y);
            NetworkSpawnUtility.Spawn(prefab, position, Quaternion.identity);
        }
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

    private int GetConfiguredMaxHealth()
    {
        EnemyStatsConfig config = Config;
        return Mathf.Max(1, EnemyRunStats.GetRoundedValue(config, EnemyStatType.MaxHealth));
    }

    private void HandleRunStatChanged(EnemyStatType statType)
    {
        if (statType != EnemyStatType.MaxHealth ||
            !NetworkAuthority.IsServerOrOffline(this) ||
            IsDead)
        {
            return;
        }

        int nextMaxHealth = GetConfiguredMaxHealth();
        int delta = nextMaxHealth - cachedMaxHealth;
        cachedMaxHealth = nextMaxHealth;
        SetHealth(Mathf.Clamp(CurrentHealth + Mathf.Max(0, delta), 0, nextMaxHealth));
    }
}
