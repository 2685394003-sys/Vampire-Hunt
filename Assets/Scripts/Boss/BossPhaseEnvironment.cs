using System;
using System.Reflection;
using UnityEngine;

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
        ApplyStatsRewardWithoutCompileDependency();
        if (line != null)
        {
            line.startColor = new Color(1f, 0.2f, 0.25f, 0.15f);
            line.endColor = line.startColor;
        }
        Destroy(gameObject, 0.25f);
    }

    private void ApplyStatsRewardWithoutCompileDependency()
    {
        Type statsType = Type.GetType("StatsManager, Assembly-CSharp");
        FieldInfo instanceField = statsType?.GetField(
            "Instance",
            BindingFlags.Public | BindingFlags.Static);
        object statsInstance = instanceField?.GetValue(null);
        if (statsInstance == null)
        {
            Debug.LogWarning("[Boss 血池] 未找到 StatsManager.Instance，保留表现但未修改玩家属性。", this);
            return;
        }

        AddIntField(statsType, statsInstance, "damage", config.phaseRewardDamage);
        AddIntField(statsType, statsInstance, "speed", config.phaseRewardSpeed);
        AddIntField(statsType, statsInstance, "maxHealth", config.phaseRewardMaxHealth);
        AddIntField(statsType, statsInstance, "currentHealth", config.phaseRewardMaxHealth);

        FieldInfo maxHealthField = statsType.GetField("maxHealth", BindingFlags.Public | BindingFlags.Instance);
        FieldInfo currentHealthField = statsType.GetField("currentHealth", BindingFlags.Public | BindingFlags.Instance);
        if (maxHealthField != null && currentHealthField != null)
        {
            int maxHealth = (int)maxHealthField.GetValue(statsInstance);
            int currentHealth = (int)currentHealthField.GetValue(statsInstance);
            currentHealthField.SetValue(statsInstance, Mathf.Min(currentHealth, maxHealth));
        }

        Debug.Log("[Boss 血池] 玩家获得阶段强化：伤害、移速与最大生命提升。", this);
    }

    private static void AddIntField(Type type, object instance, string fieldName, int amount)
    {
        if (amount <= 0)
        {
            return;
        }

        FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
        if (field?.FieldType != typeof(int))
        {
            return;
        }

        int current = (int)field.GetValue(instance);
        field.SetValue(instance, current + amount);
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
