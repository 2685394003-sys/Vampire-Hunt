using System;
using UnityEngine;

public enum BossGuardSide
{
    Left = 0,
    Right = 1
}

/// <summary>
/// Legacy guard collider/visual adapter. Guard integrity, mitigation and the
/// vulnerable transition are owned by BossEncounterState; this component only
/// forwards hit/debug commands and mirrors the read-only projection.
/// </summary>
[DisallowMultipleComponent]
public sealed class BossGuard : MonoBehaviour, IDamageable
{
    [SerializeField] private BossGuardSide side;
    [SerializeField, Min(1)] private int maxHealth = 25;
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Collider hitCollider;
    [SerializeField, Min(0f)] private float hitFlashDuration = 0.08f;
    [SerializeField, Range(0f, 1f)] private float brokenAlpha = 0.18f;
    [SerializeField] private Transform bossRoot;
    [SerializeField] private bool independentMovement = true;
    [SerializeField, Min(0.1f)] private float formationDistance = 2f;
    [SerializeField, Min(0f)] private float formationHeight = 0.6f;
    [SerializeField, Min(0.1f)] private float followSpeed = 4.5f;
    [SerializeField] private Transform formationReference;

    public BossGuardSide Side => side;
    public int CurrentHealth { get; private set; }
    public int MaxHealth => maxHealth;
    public bool IsBroken => CurrentHealth <= 0;
    public bool IsDamaged => CurrentHealth < maxHealth;
    public bool CanSweep => isActiveAndEnabled && !IsDamaged;

    public event Action<BossGuard> Broken;
    public event Action<BossGuard, int, int> HealthChanged;

    private BossController controller;
    private int previousHealth;

    private void Awake()
    {
        spriteRenderer ??= GetComponentInChildren<SpriteRenderer>(true);
        hitCollider ??= GetComponent<Collider>();
        bossRoot ??= transform.parent;
        formationReference ??= bossRoot;
        controller ??= GetComponentInParent<BossController>();
        CurrentHealth = maxHealth;
        previousHealth = CurrentHealth;
        ApplyVisualState();
    }

    private void Start()
    {
        if (independentMovement && !NetworkAuthority.IsNetworkActive && bossRoot != null && transform.parent != null)
            transform.SetParent(null, true);
    }

    private void FixedUpdate()
    {
        if (!NetworkAuthority.IsServerOrOffline() || !independentMovement || bossRoot == null) return;
        Vector3 right = formationReference != null
            ? Vector3.ProjectOnPlane(formationReference.right, Vector3.up).normalized
            : Vector3.right;
        if (right.sqrMagnitude <= 0.000001f) right = Vector3.right;
        float sign = side == BossGuardSide.Left ? -1f : 1f;
        Vector3 target = bossRoot.position + right * (formationDistance * sign) + Vector3.up * formationHeight;
        transform.position = Vector3.MoveTowards(transform.position, target, followSpeed * Time.fixedDeltaTime);
    }

    public void Configure(BossGuardSide guardSide, BossConfig config)
    {
        side = guardSide;
        if (config != null)
        {
            maxHealth = Mathf.Max(1, config.guardMaxHealth);
            hitFlashDuration = Mathf.Max(0f, config.guardHitFlashDuration);
            brokenAlpha = Mathf.Clamp01(config.brokenGuardAlpha);
            independentMovement = config.guardIndependentMovement;
            formationDistance = Mathf.Max(0.1f, config.guardFormationDistance);
            formationHeight = Mathf.Max(0f, config.guardFormationHeight);
            followSpeed = Mathf.Max(0.1f, config.guardFollowSpeed);
        }
        SyncFromController(false);
    }

    public void TakeDamage(int amount)
    {
        if (!NetworkAuthority.IsServerOrOffline() || amount <= 0 || IsBroken) return;
        controller ??= GetComponentInParent<BossController>();
        if (controller == null) return;
        BossCommandResult result = controller.ApplyDamageFromGuardShell(amount, transform.position);
        if (result == BossCommandResult.Succeeded) SyncFromController(true);
    }

    [ContextMenu("Boss/修复护卫")]
    public void Restore()
    {
        if (!NetworkAuthority.IsServerOrOffline()) return;
        controller ??= GetComponentInParent<BossController>();
        if (controller != null && controller.RestoreGuardFromShell()) SyncFromController(true);
    }

    [ContextMenu("Boss/调试：护卫 -5 HP")]
    private void DebugDamage() => TakeDamage(5);

    [ContextMenu("Boss/调试：直接击破护卫")]
    private void DebugBreak() => TakeDamage(Mathf.Max(1, CurrentHealth));

    public void DisableForBossDeath()
    {
        CurrentHealth = 0;
        if (hitCollider != null) hitCollider.enabled = false;
        ApplyVisualState();
        gameObject.SetActive(false);
    }

    internal void SyncFromController(bool emitEvents)
    {
        controller ??= GetComponentInParent<BossController>();
        int next = controller != null && controller.TryGetGuardProjection(out int current, out int maximum)
            ? Mathf.Clamp(current, 0, Mathf.Max(1, maximum))
            : Mathf.Clamp(CurrentHealth, 0, maxHealth);
        if (controller != null && controller.TryGetGuardProjection(out _, out int projectedMax))
            maxHealth = Mathf.Max(1, projectedMax);

        int old = CurrentHealth;
        CurrentHealth = next;
        previousHealth = old;
        if (hitCollider != null) hitCollider.enabled = !IsBroken;
        ApplyVisualState();
        if (!emitEvents || old == CurrentHealth) return;
        HealthChanged?.Invoke(this, CurrentHealth, maxHealth);
        if (old > 0 && CurrentHealth <= 0) Broken?.Invoke(this);
    }

    private void ApplyVisualState()
    {
        if (spriteRenderer == null) return;
        Color color = Color.white;
        if (IsBroken) color.a = brokenAlpha;
        else if (IsDamaged)
        {
            color = Color.Lerp(Color.white, Color.gray, 0.45f);
            color.a = 0.65f;
        }
        spriteRenderer.color = color;
    }

    private void OnValidate()
    {
        maxHealth = Mathf.Max(1, maxHealth);
        hitFlashDuration = Mathf.Max(0f, hitFlashDuration);
        brokenAlpha = Mathf.Clamp01(brokenAlpha);
        formationDistance = Mathf.Max(0.1f, formationDistance);
        formationHeight = Mathf.Max(0f, formationHeight);
        followSpeed = Mathf.Max(0.1f, followSpeed);
        spriteRenderer ??= GetComponentInChildren<SpriteRenderer>(true);
        hitCollider ??= GetComponent<Collider>();
        bossRoot ??= transform.parent;
        formationReference ??= bossRoot;
    }
}
