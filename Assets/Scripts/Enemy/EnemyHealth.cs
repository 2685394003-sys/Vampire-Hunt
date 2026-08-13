using Unity.Netcode;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class EnemyHealth : NetworkBehaviour
{
    [SerializeField] private EnemyStatsConfig stats;

    private readonly NetworkVariable<int> networkHealth = new(
        1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> networkDead = new(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private EnemyHurtFlash hurtFlash;
    private PlayerNetworkState lastDamageDealer;
    private int offlineHealth;
    private bool offlineDead;
    private int cachedMaxHealth;

    public int CurrentHealth => UseNetworkValues ? networkHealth.Value : offlineHealth;
    public bool IsDead => UseNetworkValues ? networkDead.Value : offlineDead;
    public EnemyStatsConfig Config =>
        stats != null ? stats : stats = EnemyStatsConfig.LoadDefault();
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
        if (!IsServer) return;

        cachedMaxHealth = GetConfiguredMaxHealth();
        networkHealth.Value = cachedMaxHealth;
        networkDead.Value = false;
        lastDamageDealer = null;
    }

    /// <summary>Compatibility entry point for non-player or legacy damage.</summary>
    public void ChangeEnemyHealth(int amount)
    {
        ChangeEnemyHealth(amount, null);
    }

    /// <summary>
    /// Applies damage and remembers its player source. The last damaging player
    /// receives personal rewards when this hit (or a later unattributed hit)
    /// kills it. Scarlet is shared by PlayerNetworkState in multiplayer.
    /// </summary>
    public void ChangeEnemyHealth(int amount, PlayerNetworkState damageDealer)
    {
        ApplyDamage(amount, damageDealer);
    }

    /// <summary>Returns the actual health removed, excluding overkill.</summary>
    public int ApplyDamage(int amount, PlayerNetworkState damageDealer) =>
        ApplyDamage(amount, damageDealer, out _);

    /// <summary>Also captures lethality before network despawn destroys the target.</summary>
    public int ApplyDamage(
        int amount,
        PlayerNetworkState damageDealer,
        out bool killed)
    {
        killed = false;
        if (!NetworkAuthority.IsServerOrOffline(this) || amount <= 0 || IsDead)
        {
            return 0;
        }

        if (damageDealer != null) lastDamageDealer = damageDealer;
        int previousHealth = CurrentHealth;
        SetHealth(Mathf.Max(0, CurrentHealth - amount));
        if (NetworkAuthority.IsNetworkActive)
        {
            PlayHurtFeedbackRpc();
        }
        else
        {
            hurtFlash?.StartHurtFlash();
        }

        int damageDealt = previousHealth - CurrentHealth;
        if (CurrentHealth <= 0)
        {
            killed = true;
            ServerDie();
        }
        return damageDealt;
    }

    private void ServerDie()
    {
        if (IsDead || !NetworkAuthority.IsServerOrOffline(this)) return;

        SetDead(true);
        RewardPlayerDirectly(Config);
        NetworkSpawnUtility.Despawn(gameObject);
    }

    private void RewardPlayerDirectly(EnemyStatsConfig config)
    {
        PlayerNetworkState recipient = lastDamageDealer;
        if (recipient == null ||
            !recipient.IsAlive ||
            !recipient.gameObject.activeInHierarchy)
        {
            recipient = NetworkPlayerRegistry.GetClosestAlive(transform.position);
        }

        if (recipient == null)
        {
            Debug.LogWarning(
                $"[Enemy Reward] '{name}' died without an available player recipient.",
                this);
            return;
        }

        if (config == null) return;
        recipient.AddScarlet(config.redResourceDropAmount);
        recipient.AddCoins(config.coinDropAmount);

        // Existing health packs restored the player directly. With world drops
        // removed, a successful drop roll now performs that recovery immediately.
        if (config.healthPackDropChance > 0f &&
            Random.value <= config.healthPackDropChance)
        {
            recipient.HealToFull();
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
        return Mathf.Max(
            1,
            EnemyRunStats.GetRoundedValue(config, EnemyStatType.MaxHealth));
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
        SetHealth(Mathf.Clamp(
            CurrentHealth + Mathf.Max(0, delta),
            0,
            nextMaxHealth));
    }
}
