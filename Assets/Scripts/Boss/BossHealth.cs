using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

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
    private int offlinePhase;
    private bool offlineInvulnerable;
    private bool offlineDead;
    private Coroutine hurtFlashCoroutine;

    private bool UsesNetworkState => NetworkAuthority.IsNetworkActive && IsSpawned;
    public int CurrentHealth => UsesNetworkState ? networkCurrentHealth.Value : offlineCurrentHealth;
    public int MaxHealth => UsesNetworkState
        ? Mathf.Max(1, networkMaxHealth.Value)
        : Mathf.Max(1, stats != null ? stats.maxHealth : 1);
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
        offlineCurrentHealth = Mathf.Max(1, stats != null ? stats.maxHealth : 1);
    }

    private void OnEnable()
    {
        if (!NetworkAuthority.IsNetworkActive)
            HealthChanged?.Invoke(CurrentHealth, MaxHealth);
    }

    public override void OnNetworkSpawn()
    {
        networkCurrentHealth.OnValueChanged += OnNetworkHealthChanged;
        networkMaxHealth.OnValueChanged += OnNetworkMaxHealthChanged;
        networkPhase.OnValueChanged += OnNetworkPhaseChanged;
        networkDead.OnValueChanged += OnNetworkDeadChanged;

        if (IsServer && !networkInitialized.Value)
        {
            networkMaxHealth.Value = Mathf.Max(1, stats != null ? stats.maxHealth : 1);
            networkCurrentHealth.Value = networkMaxHealth.Value;
            networkPhase.Value = 0;
            networkInvulnerable.Value = false;
            networkDead.Value = false;
            networkInitialized.Value = true;
        }

        HealthChanged?.Invoke(CurrentHealth, MaxHealth);
        if (CurrentPhase > 0) PhaseChangeStarted?.Invoke(CurrentPhase);
        if (IsDead) Died?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        networkCurrentHealth.OnValueChanged -= OnNetworkHealthChanged;
        networkMaxHealth.OnValueChanged -= OnNetworkMaxHealthChanged;
        networkPhase.OnValueChanged -= OnNetworkPhaseChanged;
        networkDead.OnValueChanged -= OnNetworkDeadChanged;
    }

    public bool TakeDamage(int amount) => TakeDamage(amount, Vector3.zero);

    void IDamageable.TakeDamage(int amount)
    {
        TakeDamage(amount, Vector3.zero);
    }

    public bool TakeDamage(int amount, Vector3 damageSource)
    {
        if (!CanMutate() || amount <= 0 || IsDead || IsInvulnerable)
            return false;

        int newHealth = Mathf.Max(0, CurrentHealth - amount);
        SetCurrentHealth(newHealth);
        PlayHurtFlash();

        if (newHealth <= 0)
        {
            Die();
            return true;
        }

        TryBeginNextPhase();
        return true;
    }

    public void CompletePhaseChange()
    {
        if (!CanMutate() || IsDead) return;
        SetInvulnerableValue(false);
        TryBeginNextPhase();
    }

    public void SetInvulnerable(bool value)
    {
        if (!CanMutate() || IsDead) return;
        SetInvulnerableValue(value);
    }

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
        if (CanMutate()) TakeDamage(Mathf.Max(1, amount), transform.position);
    }

    private bool CanMutate()
    {
        if (!NetworkAuthority.IsNetworkActive) return true;
        return IsSpawned && IsServer;
    }

    private void TryBeginNextPhase()
    {
        int targetPhase = GetTargetPhase();
        if (targetPhase <= CurrentPhase) return;

        int newPhase = CurrentPhase + 1;
        if (UsesNetworkState)
        {
            networkPhase.Value = newPhase;
            networkInvulnerable.Value = stats != null && stats.invulnerableDuringPhaseChange;
        }
        else
        {
            offlinePhase = newPhase;
            offlineInvulnerable = stats != null && stats.invulnerableDuringPhaseChange;
            PhaseChangeStarted?.Invoke(newPhase);
        }
    }

    private int GetTargetPhase()
    {
        if (stats == null) return 0;
        float rate = HealthNormalized;
        if (rate <= stats.phase3HealthRate) return 3;
        if (rate <= stats.phase2HealthRate) return 2;
        if (rate <= stats.phase1HealthRate) return 1;
        return 0;
    }

    private void Die()
    {
        if (UsesNetworkState)
        {
            networkDead.Value = true;
            networkInvulnerable.Value = true;
        }
        else
        {
            offlineDead = true;
            offlineInvulnerable = true;
            Died?.Invoke();
        }
    }

    private void SetCurrentHealth(int value)
    {
        if (UsesNetworkState)
            networkCurrentHealth.Value = value;
        else
        {
            offlineCurrentHealth = value;
            HealthChanged?.Invoke(CurrentHealth, MaxHealth);
        }
    }

    private void SetInvulnerableValue(bool value)
    {
        if (UsesNetworkState) networkInvulnerable.Value = value;
        else offlineInvulnerable = value;
    }

    private void OnNetworkHealthChanged(int previous, int current)
    {
        HealthChanged?.Invoke(current, MaxHealth);
        if (current < previous) PlayHurtFlash();
    }

    private void OnNetworkMaxHealthChanged(int previous, int current) =>
        HealthChanged?.Invoke(CurrentHealth, current);

    private void OnNetworkPhaseChanged(int previous, int current)
    {
        if (current > previous) PhaseChangeStarted?.Invoke(current);
    }

    private void OnNetworkDeadChanged(bool previous, bool current)
    {
        if (!previous && current) Died?.Invoke();
    }

    private void PlayHurtFlash()
    {
        if (!isActiveAndEnabled) return;
        if (hurtFlashCoroutine != null) StopCoroutine(hurtFlashCoroutine);
        hurtFlashCoroutine = StartCoroutine(HurtFlashRoutine());
    }

    private IEnumerator HurtFlashRoutine()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        Color[] originalColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            Material material = renderers[i].material;
            originalColors[i] = material.HasProperty("_BaseColor")
                ? material.GetColor("_BaseColor")
                : material.color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
            else material.color = Color.white;
        }

        yield return new WaitForSeconds(0.08f);

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            Material material = renderers[i].material;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", originalColors[i]);
            else material.color = originalColors[i];
        }
        hurtFlashCoroutine = null;
    }
}
