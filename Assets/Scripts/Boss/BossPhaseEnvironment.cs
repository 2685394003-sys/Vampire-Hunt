using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class BossPhaseBloodPool : MonoBehaviour
{
    private BossConfig config;
    private Transform player;
    private float radius;
    private bool consumed;
    private bool presentationOnly;
    private LineRenderer line;

    public event Action<BossPhaseBloodPool> Consumed;

    public void Initialize(BossConfig bossConfig, Transform targetPlayer, Vector3 worldPosition)
    {
        InitializeInternal(bossConfig, targetPlayer, worldPosition, false);
    }

    public void InitializeVisual(BossConfig bossConfig, Vector3 worldPosition)
    {
        InitializeInternal(bossConfig, null, worldPosition, true);
    }

    private void InitializeInternal(
        BossConfig bossConfig,
        Transform targetPlayer,
        Vector3 worldPosition,
        bool visualOnly)
    {
        config = bossConfig;
        player = targetPlayer;
        presentationOnly = visualOnly;
        radius = Mathf.Max(0.1f, config != null ? config.bloodPoolRadius : 1.4f);

        worldPosition.y = config != null ? config.GetEffectHeight() : -0.99f;
        transform.position = worldPosition;
        line = gameObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.loop = true;
        line.positionCount = 64;
        line.startWidth = config != null ? config.telegraphLineWidth * 3f : 0.3f;
        line.endWidth = line.startWidth;
        line.startColor = new Color(0.45f, 0f, 0.03f, 0.95f);
        line.endColor = line.startColor;
        line.sortingOrder = 99;

        if (config != null && config.telegraphMaterial != null)
        {
            line.sharedMaterial = config.telegraphMaterial;
        }
        else
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                line.material = new Material(shader);
            }
        }

        for (int i = 0; i < line.positionCount; i++)
        {
            float angle = i / (float)line.positionCount * Mathf.PI * 2f;
            line.SetPosition(
                i,
                worldPosition + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
        }
    }

    private void Update()
    {
        if (presentationOnly) return;
        if (!NetworkAuthority.IsServerOrOffline()) return;
        if (consumed || player == null)
        {
            return;
        }

        Vector3 offset = Vector3.ProjectOnPlane(player.position - transform.position, Vector3.up);
        if (offset.sqrMagnitude > radius * radius)
        {
            return;
        }

        consumed = true;
        ApplyPlayerReward();
        Consumed?.Invoke(this);
        if (line != null)
        {
            line.startColor = new Color(1f, 0.2f, 0.25f, 0.15f);
            line.endColor = line.startColor;
        }
        Destroy(gameObject, 0.25f);
    }

    private void ApplyPlayerReward()
    {
        PlayerNetworkState state = player != null
            ? player.GetComponentInParent<PlayerNetworkState>()
            : null;
        if (state == null)
        {
            Debug.LogWarning("[Boss 血池] 未找到目标玩家的 PlayerNetworkState。", this);
            return;
        }

        state.ApplyUpgrade(
            config != null ? config.phaseRewardDamage : 0,
            config != null ? config.phaseRewardSpeed : 0,
            config != null ? config.phaseRewardMaxHealth : 0);

        Debug.Log("[Boss 血池] 玩家获得阶段强化：伤害、移速与最大生命提升。", this);
    }
}

[DisallowMultipleComponent]
public sealed class BossPhaseRain : MonoBehaviour
{
    public void Initialize(BossConfig config)
    {
        ParticleSystem particles = gameObject.AddComponent<ParticleSystem>();
        ParticleSystem.MainModule main = particles.main;
        main.loop = true;
        main.startLifetime = 1.1f;
        main.startSpeed = 0f;
        main.startSize = 0.045f;
        main.startColor = new Color(0.45f, 0f, 0.05f, 0.7f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 700;

        ParticleSystem.EmissionModule emission = particles.emission;
        emission.rateOverTime = 120f;

        ParticleSystem.ShapeModule shape = particles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(
            config.arenaHalfSize.x * 2f,
            0.1f,
            config.arenaHalfSize.y * 2f);

        ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.y = new ParticleSystem.MinMaxCurve(-8f);

        float spawnY = Mathf.Max(config.arenaCenter.y + 8f, config.GetEffectHeight() + 8f);
        transform.position = new Vector3(config.arenaCenter.x, spawnY, config.arenaCenter.z);

        ParticleSystemRenderer particleRenderer = particles.GetComponent<ParticleSystemRenderer>();
        particleRenderer.renderMode = ParticleSystemRenderMode.Stretch;
        particleRenderer.velocityScale = 0.12f;
        particleRenderer.lengthScale = 5f;
        particleRenderer.sortingOrder = 98;

        Shader shader = Shader.Find("Sprites/Default");
        if (shader != null)
        {
            particleRenderer.material = new Material(shader);
        }

        particles.Play();
    }
}
