using System;
using System.Collections;
using UnityEngine;

public enum BossGuardSide
{
    Left,
    Right
}

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

    public BossGuardSide Side => side;
    public int CurrentHealth { get; private set; }
    public int MaxHealth => maxHealth;
    public bool IsBroken => CurrentHealth <= 0;
    public bool IsDamaged => CurrentHealth < maxHealth;
    public bool CanSweep => isActiveAndEnabled && !IsDamaged;

    public event Action<BossGuard> Broken;
    public event Action<BossGuard, int, int> HealthChanged;

    private Coroutine flashCoroutine;
    private Color baseColor = Color.white;
    private Camera viewCamera;

    private void Awake()
    {
        spriteRenderer ??= GetComponentInChildren<SpriteRenderer>(true);
        hitCollider ??= GetComponent<Collider>();
        bossRoot ??= transform.parent;
        viewCamera = Camera.main;
        if (spriteRenderer != null)
        {
            baseColor = spriteRenderer.color;
        }

        CurrentHealth = maxHealth;
        ApplyVisualState();
    }

    private void Start()
    {
        if (independentMovement && bossRoot != null && transform.parent != null)
        {
            transform.SetParent(null, true);
        }
    }

    private void FixedUpdate()
    {
        if (!independentMovement || bossRoot == null)
        {
            return;
        }

        viewCamera ??= Camera.main;
        Vector3 cameraRight = viewCamera != null
            ? Vector3.ProjectOnPlane(viewCamera.transform.right, Vector3.up).normalized
            : Vector3.right;
        float sideSign = side == BossGuardSide.Left ? -1f : 1f;
        Vector3 targetPosition = bossRoot.position +
                                 cameraRight * (formationDistance * sideSign) +
                                 Vector3.up * formationHeight;

        transform.position = Vector3.MoveTowards(
            transform.position,
            targetPosition,
            followSpeed * Time.fixedDeltaTime);
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

        CurrentHealth = maxHealth;
        ApplyVisualState();
    }

    public void TakeDamage(int amount)
    {
        if (amount <= 0 || IsBroken)
        {
            return;
        }

        CurrentHealth = Mathf.Max(0, CurrentHealth - amount);
        HealthChanged?.Invoke(this, CurrentHealth, maxHealth);

        if (CurrentHealth <= 0)
        {
            Break();
            return;
        }

        if (flashCoroutine != null)
        {
            StopCoroutine(flashCoroutine);
        }
        flashCoroutine = StartCoroutine(HitFlashRoutine());
    }

    [ContextMenu("Boss/修复护卫")]
    public void Restore()
    {
        CurrentHealth = maxHealth;
        if (hitCollider != null)
        {
            hitCollider.enabled = true;
        }

        ApplyVisualState();
        HealthChanged?.Invoke(this, CurrentHealth, maxHealth);
    }

    [ContextMenu("Boss/调试：护卫 -5 HP")]
    private void DebugDamage()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[Boss 护卫] 请进入 Play 模式后再测试伤害。", this);
            return;
        }

        TakeDamage(5);
    }

    [ContextMenu("Boss/调试：直接击破护卫")]
    private void DebugBreak()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[Boss 护卫] 请进入 Play 模式后再测试击破。", this);
            return;
        }

        TakeDamage(Mathf.Max(1, CurrentHealth));
    }

    public void DisableForBossDeath()
    {
        CurrentHealth = 0;
        if (hitCollider != null)
        {
            hitCollider.enabled = false;
        }
        ApplyVisualState();
        gameObject.SetActive(false);
    }

    private void Break()
    {
        if (hitCollider != null)
        {
            hitCollider.enabled = false;
        }

        ApplyVisualState();
        Broken?.Invoke(this);
    }

    private IEnumerator HitFlashRoutine()
    {
        if (spriteRenderer == null)
        {
            yield break;
        }

        spriteRenderer.color = Color.white;
        if (hitFlashDuration > 0f)
        {
            yield return new WaitForSeconds(hitFlashDuration);
        }

        flashCoroutine = null;
        ApplyVisualState();
    }

    private void ApplyVisualState()
    {
        if (spriteRenderer == null)
        {
            return;
        }

        Color color = baseColor;
        if (IsBroken)
        {
            color.a = brokenAlpha;
        }
        else if (IsDamaged)
        {
            color = Color.Lerp(baseColor, Color.gray, 0.45f);
            color.a = Mathf.Min(baseColor.a, 0.65f);
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
    }
}
