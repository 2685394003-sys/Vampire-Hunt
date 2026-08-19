using UnityEngine;

/// <summary>
/// Phase environment Presenter. Encounter rewards/phase transitions are
/// authoritative GameplayEvents; this visual marker never mutates Player or
/// Boss state. The old Initialize signature remains for scene/AnimationEvent
/// compatibility.
/// </summary>
[DisallowMultipleComponent]
public sealed class BossPhaseBloodPool : MonoBehaviour
{
    private BossConfig config;
    private Transform player;
    private float radius;
    private bool consumed;
    private LineRenderer line;

    public void Initialize(BossConfig bossConfig, Transform targetPlayer, Vector3 worldPosition)
    {
        config = bossConfig;
        player = targetPlayer;
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
        if (config != null && config.telegraphMaterial != null) line.sharedMaterial = config.telegraphMaterial;

        for (int i = 0; i < line.positionCount; i++)
        {
            float angle = i / (float)line.positionCount * Mathf.PI * 2f;
            line.SetPosition(i, worldPosition + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
        }
    }

    private void Update()
    {
        if (consumed || player == null) return;
        Vector3 offset = Vector3.ProjectOnPlane(player.position - transform.position, Vector3.up);
        if (offset.sqrMagnitude > radius * radius) return;
        // The domain/application layer consumes the authoritative phase event;
        // this component only records that the local visual was visited.
        consumed = true;
        if (line != null)
        {
            line.startColor = new Color(1f, 0.2f, 0.25f, 0.15f);
            line.endColor = line.startColor;
        }
        Destroy(gameObject, 0.25f);
    }
}

/// <summary>Optional phase-three rain Presenter; safe to omit on Dedicated Server.</summary>
[DisallowMultipleComponent]
public sealed class BossPhaseRain : MonoBehaviour
{
    public void Initialize(BossConfig config)
    {
        if (config == null) return;
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
        shape.scale = new Vector3(config.arenaHalfSize.x * 2f, 0.1f, config.arenaHalfSize.y * 2f);
        ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
        velocity.enabled = true;
        velocity.space = ParticleSystemSimulationSpace.World;
        velocity.y = new ParticleSystem.MinMaxCurve(-8f);

        transform.position = new Vector3(
            config.arenaCenter.x,
            Mathf.Max(config.arenaCenter.y + 8f, config.GetEffectHeight() + 8f),
            config.arenaCenter.z);
        ParticleSystemRenderer renderer = particles.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Stretch;
        renderer.velocityScale = 0.12f;
        renderer.lengthScale = 5f;
        renderer.sortingOrder = 98;
        particles.Play();
    }
}
