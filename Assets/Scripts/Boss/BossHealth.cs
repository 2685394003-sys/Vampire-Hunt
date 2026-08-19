using System;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Boss.Contracts;
using RuntimeBossSnapshot = VampireHunt.Boss.Contracts.BossSnapshot;

/// <summary>
/// Legacy health/NGO shell. The authoritative BossRuntime owns vitals; this
/// component only exposes a stable serialized/NetworkVariable projection and
/// forwards old damage/animation entry points to BossController.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(NetworkObject))]
public sealed class BossHealth : NetworkBehaviour, IDamageable
{
    [SerializeField] private BossConfig stats;

    private readonly NetworkVariable<int> networkMaxHealth = new(
        1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> networkCurrentHealth = new(
        1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<int> networkPhase = new(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> networkInvulnerable = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> networkDead = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> networkInitialized = new(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private int offlineCurrentHealth;
    private int offlineMaxHealth = 1;
    private int offlinePhase;
    private bool offlineInvulnerable;
    private bool offlineDead;
    private BossController controller;
    private bool deathNotified;
    private int lastPhase;

    private bool UsesNetworkState => NetworkAuthority.IsNetworkActive && IsSpawned;
    public int CurrentHealth => UsesNetworkState ? networkCurrentHealth.Value : offlineCurrentHealth;
    public int MaxHealth => UsesNetworkState
        ? Mathf.Max(1, networkMaxHealth.Value)
        : Mathf.Max(1, offlineMaxHealth);
    public int CurrentPhase => UsesNetworkState ? networkPhase.Value : offlinePhase;
    public bool IsInvulnerable => UsesNetworkState ? networkInvulnerable.Value : offlineInvulnerable;
    public bool IsDead => UsesNetworkState ? networkDead.Value : offlineDead;
    public float HealthNormalized => MaxHealth > 0 ? (float)CurrentHealth / MaxHealth : 0f;

    public event Action<int, int> HealthChanged;
    public event Action<int> PhaseChangeStarted;
    public event Action Died;

    private void Awake()
    {
        stats ??= GetComponent<BossConfig>();
        controller ??= GetComponent<BossController>();
        offlineMaxHealth = Mathf.Max(1, stats != null ? stats.maxHealth : 1);
        offlineCurrentHealth = offlineMaxHealth;
    }

    private void OnEnable()
    {
        if (!NetworkAuthority.IsNetworkActive) HealthChanged?.Invoke(CurrentHealth, MaxHealth);
    }

    public override void OnNetworkSpawn()
    {
        networkCurrentHealth.OnValueChanged += OnNetworkHealthChanged;
        networkMaxHealth.OnValueChanged += OnNetworkMaxHealthChanged;
        networkPhase.OnValueChanged += OnNetworkPhaseChanged;
        networkInvulnerable.OnValueChanged += OnNetworkInvulnerableChanged;
        networkDead.OnValueChanged += OnNetworkDeadChanged;

        if (IsServer && !networkInitialized.Value)
        {
            networkMaxHealth.Value = Mathf.Max(1, stats != null ? stats.maxHealth : 1);
            networkCurrentHealth.Value = networkMaxHealth.Value;
            networkPhase.Value = 1;
            networkInvulnerable.Value = false;
            networkDead.Value = false;
            networkInitialized.Value = true;
        }

        HealthChanged?.Invoke(CurrentHealth, MaxHealth);
        lastPhase = CurrentPhase;
        if (CurrentPhase > 0) PhaseChangeStarted?.Invoke(CurrentPhase);
        if (IsDead) RaiseDiedOnce();
    }

    public override void OnNetworkDespawn()
    {
        networkCurrentHealth.OnValueChanged -= OnNetworkHealthChanged;
        networkMaxHealth.OnValueChanged -= OnNetworkMaxHealthChanged;
        networkPhase.OnValueChanged -= OnNetworkPhaseChanged;
        networkInvulnerable.OnValueChanged -= OnNetworkInvulnerableChanged;
        networkDead.OnValueChanged -= OnNetworkDeadChanged;
    }

    public bool TakeDamage(int amount) => TakeDamage(amount, Vector3.zero);

    void IDamageable.TakeDamage(int amount) => TakeDamage(amount, Vector3.zero);

    public bool TakeDamage(int amount, Vector3 damageSource)
    {
        if (!CanMutate() || amount <= 0 || IsDead || IsInvulnerable) return false;
        if (controller == null) controller = GetComponent<BossController>();
        if (controller == null) return false;
        return controller.ApplyDamageFromHealthShell(amount, damageSource);
    }

    public void CompletePhaseChange() => controller?.CompletePhaseChangeFromHealthShell();

    public void SetInvulnerable(bool value) => controller?.SetInvulnerableFromHealthShell(value);

    [ContextMenu("测试：Boss 受到 10 点伤害")]
    private void DebugTakeDamage() => DebugApplyDamage(stats != null ? stats.debugDamageAmount : 10);

    [ContextMenu("测试：直接击杀 Boss")]
    private void DebugKill() => DebugApplyDamage(Mathf.Max(1, CurrentHealth));

    public void DebugApplyDamage(int amount)
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[Boss 调试] 请先进入 Play 模式再测试受伤。", this);
            return;
        }
        TakeDamage(Mathf.Max(1, amount), transform.position);
    }

    /// <summary>Called by BossController after an authoritative runtime tick.</summary>
    internal void ApplySnapshot(RuntimeBossSnapshot snapshot)
    {
        int previousHealth = CurrentHealth;
        int previousMaxHealth = MaxHealth;
        int previousPhase = CurrentPhase;
        bool previousDead = IsDead;

        offlineMaxHealth = Mathf.Max(1, snapshot.MaxHealth);
        offlineCurrentHealth = Mathf.Clamp(snapshot.Health, 0, offlineMaxHealth);
        offlinePhase = (int)snapshot.Phase;
        offlineInvulnerable = snapshot.IsInvulnerable;
        offlineDead = !snapshot.IsAlive ||
            snapshot.Mode == VampireHunt.Boss.Contracts.EncounterMode.Defeated;

        bool networkProjection = UsesNetworkState && IsServer;
        if (networkProjection)
        {
            networkMaxHealth.Value = offlineMaxHealth;
            networkCurrentHealth.Value = offlineCurrentHealth;
            networkPhase.Value = offlinePhase;
            networkInvulnerable.Value = offlineInvulnerable;
            networkDead.Value = offlineDead;
        }

        if (!networkProjection && (previousHealth != offlineCurrentHealth || previousMaxHealth != offlineMaxHealth))
            HealthChanged?.Invoke(offlineCurrentHealth, offlineMaxHealth);
        if (!networkProjection && offlinePhase > previousPhase)
        {
            lastPhase = offlinePhase;
            PhaseChangeStarted?.Invoke(offlinePhase);
        }
        else lastPhase = offlinePhase;
        if (!previousDead && offlineDead) RaiseDiedOnce();
        if (!offlineDead) deathNotified = false;
    }

    private bool CanMutate() => !NetworkAuthority.IsNetworkActive || (IsSpawned && IsServer);

    private void RaiseDiedOnce()
    {
        if (deathNotified) return;
        deathNotified = true;
        Died?.Invoke();
    }

    private void OnNetworkHealthChanged(int previous, int current) => HealthChanged?.Invoke(current, MaxHealth);
    private void OnNetworkMaxHealthChanged(int previous, int current) => HealthChanged?.Invoke(CurrentHealth, current);

    private void OnNetworkPhaseChanged(int previous, int current)
    {
        lastPhase = current;
        if (current > previous) PhaseChangeStarted?.Invoke(current);
    }

    private void OnNetworkInvulnerableChanged(bool previous, bool current) { }

    private void OnNetworkDeadChanged(bool previous, bool current)
    {
        if (!previous && current) RaiseDiedOnce();
        if (!current) deathNotified = false;
    }
}
