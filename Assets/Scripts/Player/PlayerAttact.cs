using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Server-authoritative melee resolution with client-only presentation.
/// Damage timing no longer depends exclusively on an Animator event, which is
/// essential for headless dedicated servers.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerNetworkState))]
public sealed class PlayerAttact : MonoBehaviour
{
    private static readonly int AttackParameter = Animator.StringToHash("Attack");
    private static readonly int VfxColorProperty = Shader.PropertyToID("_Color");
    private static readonly int VfxOpacityProperty = Shader.PropertyToID("_Opacity");
    private static readonly int VfxProgressProperty = Shader.PropertyToID("_Progress");
    private const string SwordSlashShaderPath = "VFX/SwordSlashWhite";

    public Animator anim;
    public Transform AttackPoint;

    [Header("服务器判定 / Server Hit Timing")]
    [SerializeField, Min(0f)] private float hitDelay = 0.12f;
    [SerializeField, Min(0.01f)] private float attackAnimationDuration = 1.25f;

    [Header("白色剑气 / White Sword Slash VFX")]
    [SerializeField, Min(0.01f)] private float swordSlashDuration = 0.32f;
    [SerializeField, Min(0f)] private float swordSlashHeight = 0.8f;
    [SerializeField, Range(0.05f, 0.5f)] private float swordSlashThicknessRatio = 0.22f;
    [SerializeField, Min(0.01f)] private float swordSlashMinThickness = 0.35f;
    [SerializeField, ColorUsage(true, true)] private Color swordSlashColor = Color.white;

    private PlayerNetworkState playerState;
    private PlayerController playerController;
    private Coroutine serverHitCoroutine;
    private Coroutine attackResetCoroutine;
    private GameObject swordSlashObject;
    private Mesh swordSlashMesh;
    private MeshRenderer swordSlashRenderer;
    private Material swordSlashMaterial;
    private MaterialPropertyBlock swordSlashProperties;
    private float swordSlashElapsed;
    private float renderedSwordSlashRange = -1f;
    private float renderedSwordSlashAngle = -1f;
    private bool swordSlashPlaying;
    private bool missingSwordSlashShaderLogged;
    private int activeAttackSequence;
    private int lastResolvedAttackSequence;

    public bool IsAttackAnimationPlaying { get; private set; }

    private void Awake()
    {
        playerState = PlayerNetworkState.EnsureForMigration(gameObject);
        playerController = GetComponent<PlayerController>();
    }

    private void Update()
    {
        UpdateSwordSlashVfx(Time.deltaTime);
    }

    private void OnDestroy()
    {
        if (swordSlashMesh != null)
        {
            Destroy(swordSlashMesh);
        }
        if (swordSlashMaterial != null)
        {
            Destroy(swordSlashMaterial);
        }
    }

    /// <summary>Compatibility entry point used by older animation/controller code.</summary>
    public void Attack()
    {
        playerController?.RequestAttack();
    }

    public void ServerBeginAttack(int sequence)
    {
        if (!NetworkAuthority.IsServerOrOffline(playerState) ||
            playerState == null ||
            !playerState.IsAlive ||
            sequence <= activeAttackSequence)
        {
            return;
        }

        activeAttackSequence = sequence;
        playerController?.StopMoveAnimationForAttack();
        SetAttackState(true);
        RestartAttackResetTimer();
        if (serverHitCoroutine != null)
        {
            StopCoroutine(serverHitCoroutine);
        }
        serverHitCoroutine = StartCoroutine(ServerResolveAfterDelay(sequence));
    }

    public void PlayAttackPresentation()
    {
        playerController?.StopMoveAnimationForAttack();
        SetAttackState(true);
        RestartAttackResetTimer();
        PlaySwordSlashVfx();
        SFXManager.Instance?.PlayAttackSFX();
    }

    public void Attackfalse()
    {
        SetAttackState(false);
    }

    private void SetAttackState(bool attacking)
    {
        IsAttackAnimationPlaying = attacking;
        if (anim != null)
        {
            anim.SetBool(AttackParameter, attacking);
        }
    }

    private void PlaySwordSlashVfx()
    {
        if (Application.isBatchMode || playerState == null || !EnsureSwordSlashVfx())
        {
            return;
        }

        BuildSwordSlashMesh(playerState.WeaponRange, playerState.AttackConeAngle);
        swordSlashElapsed = 0f;
        swordSlashPlaying = true;
        swordSlashRenderer.enabled = true;
        ApplySwordSlashProperties(0f, 1f);
    }

    private void UpdateSwordSlashVfx(float deltaTime)
    {
        if (!swordSlashPlaying || swordSlashRenderer == null || playerState == null)
        {
            return;
        }

        BuildSwordSlashMesh(playerState.WeaponRange, playerState.AttackConeAngle);
        UpdateSwordSlashTransform();
        swordSlashElapsed += Mathf.Max(0f, deltaTime);
        float normalizedTime = Mathf.Clamp01(
            swordSlashElapsed / Mathf.Max(0.01f, swordSlashDuration));
        float reveal = Mathf.Clamp01(normalizedTime / 0.55f);
        float fade = normalizedTime < 0.18f
            ? Mathf.SmoothStep(0f, 1f, normalizedTime / 0.18f)
            : 1f - Mathf.SmoothStep(0f, 1f, (normalizedTime - 0.58f) / 0.42f);
        ApplySwordSlashProperties(reveal, fade);

        if (normalizedTime >= 1f)
        {
            swordSlashPlaying = false;
            swordSlashRenderer.enabled = false;
        }
    }

    private void RestartAttackResetTimer()
    {
        if (attackResetCoroutine != null)
        {
            StopCoroutine(attackResetCoroutine);
        }
        attackResetCoroutine = StartCoroutine(ResetAttackAfterDelay());
    }

    private IEnumerator ResetAttackAfterDelay()
    {
        yield return new WaitForSeconds(attackAnimationDuration);
        SetAttackState(false);
        attackResetCoroutine = null;
    }

    /// <summary>
    /// AnimationEvent fallback. Sequence de-duplication guarantees that the server
    /// coroutine and animation event cannot apply the same swing twice.
    /// </summary>
    public void DealDamage()
    {
        ServerDealDamageOnce(activeAttackSequence);
    }

    private IEnumerator ServerResolveAfterDelay(int sequence)
    {
        if (hitDelay > 0f)
        {
            yield return new WaitForSeconds(hitDelay);
        }

        ServerDealDamageOnce(sequence);
        serverHitCoroutine = null;
    }

    private void ServerDealDamageOnce(int sequence)
    {
        if (!NetworkAuthority.IsServerOrOffline(playerState) ||
            playerState == null ||
            !playerState.IsAlive ||
            AttackPoint == null ||
            sequence <= 0 ||
            sequence <= lastResolvedAttackSequence)
        {
            return;
        }

        lastResolvedAttackSequence = sequence;
        Collider[] hits = Physics.OverlapSphere(
            AttackPoint.position,
            playerState.WeaponRange,
            playerState.EnemyLayer,
            QueryTriggerInteraction.Collide);

        HashSet<Component> damaged = new();
        foreach (Collider hit in hits)
        {
            if (hit == null)
            {
                continue;
            }
            if (!IsInsideAttackCone(hit))
            {
                continue;
            }

            if (BossCombatTarget.TryGetInParent<IDamageable>(hit, out IDamageable damageable))
            {
                Component damageComponent = damageable as Component;
                if (damageComponent != null && !damaged.Add(damageComponent))
                {
                    continue;
                }

                Vector3 damagePosition = damageComponent != null
                    ? damageComponent.transform.position
                    : hit.transform.position;
                int bossCombatTextTargetKey = damageComponent != null
                    ? damageComponent.GetInstanceID()
                    : hit.GetInstanceID();
                int bossAttackDamage = playerState.RollAttackDamage(out bool bossWasCritical);
                bool tracksHealth = TryReadCurrentHealth(damageComponent, out int previousHealth);
                damageable.TakeDamage(bossAttackDamage);
                if (tracksHealth &&
                    damageComponent != null &&
                    TryReadCurrentHealth(damageComponent, out int currentHealth))
                {
                    playerState.ReportAttackHit(
                        null,
                        Mathf.Max(0, previousHealth - currentHealth),
                        bossWasCritical,
                        damagePosition,
                        bossCombatTextTargetKey);
                }
                if (playerState.KnockbackForce > 0f &&
                    BossCombatTarget.TryGetInParent<IKnockbackReceiver>(hit, out IKnockbackReceiver receiver))
                {
                    receiver.ApplyKnockback(
                        transform,
                        playerState.KnockbackForce,
                        playerState.KnockbackTime);
                }
                continue;
            }

            EnemyHealth enemyHealth = hit.GetComponentInParent<EnemyHealth>();
            if (enemyHealth == null || !damaged.Add(enemyHealth))
            {
                continue;
            }

            Vector3 enemyPosition = enemyHealth.transform.position;
            int combatTextTargetKey = enemyHealth.GetInstanceID();
            int enemyAttackDamage = playerState.RollAttackDamage(out bool enemyWasCritical);
            int damageDealt = enemyHealth.ApplyDamage(
                enemyAttackDamage,
                playerState,
                out bool killed);
            playerState.ReportAttackHit(
                killed ? null : enemyHealth,
                damageDealt,
                enemyWasCritical,
                enemyPosition,
                combatTextTargetKey);
            if (killed)
            {
                playerState.ReportEnemyKilled(enemyPosition);
            }
            else
            {
                EnemyKnockBack enemyKnockBack = hit.GetComponentInParent<EnemyKnockBack>();
                enemyKnockBack?.EnemyKnockback(
                    transform,
                    playerState.KnockbackForce,
                    playerState.StunTime,
                    playerState.KnockbackTime);
            }
        }
    }

    private static bool TryReadCurrentHealth(Component damageComponent, out int currentHealth)
    {
        switch (damageComponent)
        {
            case BossHealth bossHealth:
                currentHealth = bossHealth.CurrentHealth;
                return true;
            case BossGuard bossGuard:
                currentHealth = bossGuard.CurrentHealth;
                return true;
            default:
                currentHealth = 0;
                return false;
        }
    }

    private bool IsInsideAttackCone(Collider hit)
    {
        Vector3 origin = AttackPoint.position;
        Vector3 closestPoint = hit.ClosestPoint(origin);
        Vector3 toTarget = Vector3.ProjectOnPlane(closestPoint - origin, Vector3.up);
        if (toTarget.sqrMagnitude <= 0.0001f)
        {
            return true;
        }

        Vector3 forward = GetAttackForward();
        float minimumDot = Mathf.Cos(playerState.AttackConeAngle * 0.5f * Mathf.Deg2Rad);
        return Vector3.Dot(forward, toTarget.normalized) >= minimumDot;
    }

    private Vector3 GetAttackForward()
    {
        Vector3 forward = playerController != null
            ? playerController.facingDirection
            : transform.forward;
        forward = Vector3.ProjectOnPlane(forward, Vector3.up);
        return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
    }

    private bool EnsureSwordSlashVfx()
    {
        if (swordSlashRenderer != null)
        {
            return true;
        }

        Shader shader = Resources.Load<Shader>(SwordSlashShaderPath);
        if (shader == null)
        {
            if (!missingSwordSlashShaderLogged)
            {
                Debug.LogError($"[Player Attack] Missing Resources/{SwordSlashShaderPath}.shader", this);
                missingSwordSlashShaderLogged = true;
            }
            return false;
        }

        swordSlashObject = new GameObject("WhiteSwordSlashVFX")
        {
            layer = gameObject.layer,
            hideFlags = HideFlags.DontSave
        };
        swordSlashObject.transform.SetParent(transform, false);

        MeshFilter meshFilter = swordSlashObject.AddComponent<MeshFilter>();
        swordSlashRenderer = swordSlashObject.AddComponent<MeshRenderer>();
        swordSlashRenderer.shadowCastingMode = ShadowCastingMode.Off;
        swordSlashRenderer.receiveShadows = false;
        swordSlashRenderer.lightProbeUsage = LightProbeUsage.Off;
        swordSlashRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        swordSlashRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        swordSlashRenderer.allowOcclusionWhenDynamic = false;
        swordSlashRenderer.sortingOrder = 50;

        swordSlashMesh = new Mesh
        {
            name = "Runtime White Sword Slash",
            hideFlags = HideFlags.DontSave
        };
        swordSlashMesh.MarkDynamic();
        meshFilter.sharedMesh = swordSlashMesh;

        swordSlashMaterial = new Material(shader)
        {
            name = "Runtime White Sword Slash",
            hideFlags = HideFlags.DontSave
        };
        swordSlashRenderer.sharedMaterial = swordSlashMaterial;
        swordSlashProperties = new MaterialPropertyBlock();
        swordSlashRenderer.enabled = false;
        UpdateSwordSlashTransform();
        return true;
    }

    private void BuildSwordSlashMesh(float range, float angle)
    {
        range = Mathf.Max(0.01f, range);
        angle = Mathf.Clamp(angle, 1f, 179f);
        if (Mathf.Approximately(range, renderedSwordSlashRange) &&
            Mathf.Approximately(angle, renderedSwordSlashAngle))
        {
            return;
        }

        renderedSwordSlashRange = range;
        renderedSwordSlashAngle = angle;
        float thickness = Mathf.Min(
            range * 0.8f,
            Mathf.Max(swordSlashMinThickness, range * swordSlashThicknessRatio));
        float innerRadius = Mathf.Max(0f, range - thickness);
        int segmentCount = Mathf.Clamp(Mathf.CeilToInt(angle / 4f), 12, 64);
        Vector3[] vertices = new Vector3[(segmentCount + 1) * 2];
        Vector2[] uvs = new Vector2[vertices.Length];
        int[] triangles = new int[segmentCount * 6];

        for (int i = 0; i <= segmentCount; i++)
        {
            float t = i / (float)segmentCount;
            float degrees = Mathf.Lerp(-angle * 0.5f, angle * 0.5f, t);
            Vector3 direction = Quaternion.Euler(0f, degrees, 0f) * Vector3.forward;
            int vertex = i * 2;
            vertices[vertex] = direction * innerRadius;
            vertices[vertex + 1] = direction * range;
            uvs[vertex] = new Vector2(t, 0f);
            uvs[vertex + 1] = new Vector2(t, 1f);

            if (i == segmentCount)
            {
                continue;
            }
            int triangle = i * 6;
            triangles[triangle] = vertex;
            triangles[triangle + 1] = vertex + 1;
            triangles[triangle + 2] = vertex + 2;
            triangles[triangle + 3] = vertex + 1;
            triangles[triangle + 4] = vertex + 3;
            triangles[triangle + 5] = vertex + 2;
        }

        swordSlashMesh.Clear();
        swordSlashMesh.vertices = vertices;
        swordSlashMesh.uv = uvs;
        swordSlashMesh.triangles = triangles;
        swordSlashMesh.RecalculateBounds();
    }

    private void UpdateSwordSlashTransform()
    {
        if (swordSlashObject == null)
        {
            return;
        }

        Vector3 scale = transform.lossyScale;
        swordSlashObject.transform.localPosition = new Vector3(
            0f,
            swordSlashHeight / SafeScale(scale.y),
            0f);
        swordSlashObject.transform.localRotation = Quaternion.identity;
        swordSlashObject.transform.localScale = new Vector3(
            1f / SafeScale(scale.x),
            1f / SafeScale(scale.y),
            1f / SafeScale(scale.z));
    }

    private void ApplySwordSlashProperties(float progress, float opacity)
    {
        swordSlashRenderer.GetPropertyBlock(swordSlashProperties);
        swordSlashProperties.SetColor(VfxColorProperty, swordSlashColor);
        swordSlashProperties.SetFloat(VfxProgressProperty, Mathf.Clamp01(progress));
        swordSlashProperties.SetFloat(VfxOpacityProperty, Mathf.Clamp01(opacity));
        swordSlashRenderer.SetPropertyBlock(swordSlashProperties);
    }

    private static float SafeScale(float value)
    {
        return Mathf.Max(0.0001f, Mathf.Abs(value));
    }

    [ContextMenu("测试/触发一次服务器挥砍伤害")]
    private void DebugDealDamage()
    {
        if (!Application.isPlaying || !NetworkAuthority.IsServerOrOffline(playerState))
        {
            return;
        }

        activeAttackSequence++;
        ServerDealDamageOnce(activeAttackSequence);
    }

    private void OnDrawGizmos()
    {
        PlayerNetworkState state = playerState != null
            ? playerState
            : GetComponent<PlayerNetworkState>();
        if (AttackPoint == null || state == null)
        {
            return;
        }

        Vector3 origin = AttackPoint.position;
        Vector3 forward = GetAttackForward();
        float halfAngle = state.AttackConeAngle * 0.5f;
        Vector3 left = Quaternion.AngleAxis(-halfAngle, Vector3.up) * forward;
        Vector3 right = Quaternion.AngleAxis(halfAngle, Vector3.up) * forward;

        Gizmos.color = Color.white;
        Gizmos.DrawLine(origin, origin + left * state.WeaponRange);
        Gizmos.DrawLine(origin, origin + right * state.WeaponRange);
        Vector3 previous = origin + left * state.WeaponRange;
        const int arcSegments = 24;
        for (int i = 1; i <= arcSegments; i++)
        {
            float degrees = Mathf.Lerp(-halfAngle, halfAngle, i / (float)arcSegments);
            Vector3 direction = Quaternion.AngleAxis(degrees, Vector3.up) * forward;
            Vector3 next = origin + direction * state.WeaponRange;
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
    }
}
