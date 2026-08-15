using System;
using UnityEditor;
using UnityEngine;

/// <summary>Fast integration checks for the GAS core and implemented pacts.</summary>
public static class GameplayAbilityFrameworkValidator
{
    [MenuItem("Vampire Hunt/Validation/Validate Gameplay Ability Framework")]
    public static void ValidateFromMenu()
    {
        string report = RunOrThrow();
        Debug.Log(report);
    }

    public static string RunOrThrow()
    {
        ValidateHierarchicalTags();
        ValidateDurationLifecycle();
        ValidateStackedPeriodicEffect();
        ValidateScarletKiss();
        ValidateBerserkerRage();
        ValidateEnemyBurningAndFrozen();
        return "[GAS Validation] Passed tags, duration, stacking/periods, Scarlet Kiss, Berserker Rage, enemy instance modifiers, burning, and frozen state.";
    }

    private static void ValidateHierarchicalTags()
    {
        GameplayTagContainer tags = new();
        Require(tags.Add("State.BloodPact.Berserker"), "tag add failed");
        Require(tags.Has("State"), "parent State tag was not granted");
        Require(tags.Has("State.BloodPact"), "parent BloodPact tag was not granted");
        Require(tags.Has("State.BloodPact.Berserker"), "leaf tag was not granted");
        Require(tags.Remove("State.BloodPact.Berserker"), "tag remove failed");
        Require(tags.Count == 0, "hierarchical tag counts leaked after removal");
    }

    private static void ValidateDurationLifecycle()
    {
        PlayerNetworkState player = CreatePlayer("GAS Duration Validation");
        try
        {
            float baseline = player.Damage;
            GameplayEffectDefinition timed = new(
                "validation.timed_attack",
                GameplayEffectDurationPolicy.Duration,
                modifiers: new[]
                {
                    new GameplayModifierDefinition(
                        GameplayAttributeType.BaseAttack,
                        PlayerModifierOperation.Flat,
                        new GameplayMagnitudeDefinition(GameplayMagnitudeSource.Fixed, 5f))
                },
                durationSeconds: 0.1f,
                executeOnApplication: false,
                grantedTags: new[] { "State.Validation.Timed" });
            GameplayAbilityDefinition ability = new(
                "validation.duration",
                GameplayEventType.Granted,
                GameplayAbilityTarget.Self,
                new[] { timed });

            Require(player.AbilitySystem.GrantAbilities(new[] { ability }, "validation"),
                "duration ability grant failed");
            RequireClose(player.Damage, baseline + 5f, "duration modifier did not apply");
            Require(player.AbilitySystem.Tags.Has("State.Validation.Timed"),
                "duration tag did not apply");

            player.AbilitySystem.Tick(0.11f);
            RequireClose(player.Damage, baseline, "expired duration modifier was not removed");
            Require(!player.AbilitySystem.Tags.Has("State.Validation.Timed"),
                "expired duration tag was not removed");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(player.gameObject);
        }
    }

    private static void ValidateScarletKiss()
    {
        PlayerNetworkState player = CreatePlayer("Scarlet Kiss Validation");
        try
        {
            BloodPactDefinition pact = FindPact("update_017");
            Require(pact.IsRuntimeImplemented, "Scarlet Kiss is not marked implemented");
            Require(player.ApplyDamage(50), "could not prepare missing health");
            int before = player.CurrentHealth;
            player.AddScarlet(PlayerNetworkState.BloodPactScarletCost);
            Require(player.RequestBloodPactSelection(pact.PactId),
                "Scarlet Kiss server selection failed");
            Require(player.HasBloodPact(pact.PactId) && player.GetBloodPactStacks(pact.PactId) == 1,
                "Scarlet Kiss replicated/offline pact snapshot is missing");

            player.ReportAttackHit(null, 20f, false, Vector3.zero);
            Require(player.CurrentHealth - before == 3,
                $"Scarlet Kiss expected 3 healing, got {player.CurrentHealth - before}");

            before = player.CurrentHealth;
            player.ReportAttackHit(null, 1f, false, Vector3.zero);
            Require(player.CurrentHealth - before == 1,
                "Scarlet Kiss minimum one-point healing failed");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(player.gameObject);
        }
    }

    private static void ValidateStackedPeriodicEffect()
    {
        PlayerNetworkState player = CreatePlayer("GAS Periodic Validation");
        try
        {
            Require(player.ApplyDamage(20), "could not prepare periodic healing test");
            GameplayEffectDefinition periodic = new(
                "validation.periodic_heal",
                GameplayEffectDurationPolicy.Duration,
                executions: new[]
                {
                    new GameplayExecutionDefinition(
                        GameplayExecutionType.Heal,
                        new GameplayMagnitudeDefinition(GameplayMagnitudeSource.Fixed, 1f))
                },
                durationSeconds: 0.3f,
                periodSeconds: 0.1f,
                executeOnApplication: false,
                stackingPolicy: GameplayEffectStackingPolicy.AddStacks,
                maxStacks: 2);
            GameplayAbilityDefinition ability = new(
                "validation.periodic",
                GameplayEventType.AttackHit,
                GameplayAbilityTarget.Self,
                new[] { periodic });
            Require(player.AbilitySystem.GrantAbilities(new[] { ability }, "periodic_validation"),
                "periodic ability grant failed");

            GameplayEventData hit = new(GameplayEventType.AttackHit, player, player, 1f);
            player.AbilitySystem.SendEvent(in hit);
            player.AbilitySystem.SendEvent(in hit);
            Require(player.AbilitySystem.ActiveEffectCount == 1,
                "stacking created duplicate active effect instances");

            int before = player.CurrentHealth;
            player.AbilitySystem.Tick(0.1f);
            Require(player.CurrentHealth - before == 2,
                "two-stack periodic effect did not execute with two stacks");
            player.AbilitySystem.Tick(0.21f);
            Require(player.AbilitySystem.ActiveEffectCount == 0,
                "periodic duration effect did not expire");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(player.gameObject);
        }
    }

    private static void ValidateBerserkerRage()
    {
        PlayerNetworkState player = CreatePlayer("Berserker Rage Validation");
        try
        {
            BloodPactDefinition pact = FindPact("update_021");
            Require(pact.IsRuntimeImplemented, "Berserker Rage is not marked implemented");
            float baseDamage = player.Damage;
            float baseCrit = player.CritRate;

            Require(player.ApplyDamage(player.MaxHealth / 2), "could not prepare half health");
            player.AddScarlet(PlayerNetworkState.BloodPactScarletCost);
            Require(player.RequestBloodPactSelection(pact.PactId),
                "Berserker Rage server selection failed");
            RequireClose(player.Damage, baseDamage * 1.375f,
                "half-health Berserker attack scaling is wrong");
            RequireClose(player.CritRate, baseCrit + 0.125f,
                "half-health Berserker crit scaling is wrong");
            Require(player.AbilitySystem.Tags.Has("State.BloodPact.Berserker"),
                "Berserker granted tag is missing");

            player.ApplyDamage(player.CurrentHealth - 1);
            float missingRatio = 1f - 1f / player.MaxHealth;
            RequireClose(player.Damage, baseDamage * (1f + missingRatio * 0.75f),
                "Berserker did not refresh after health changed");
            RequireClose(player.CritRate, baseCrit + missingRatio * 0.25f,
                "Berserker crit did not refresh after health changed");

            player.HealToFull();
            RequireClose(player.Damage, baseDamage, "Berserker attack did not return to baseline");
            RequireClose(player.CritRate, baseCrit, "Berserker crit did not return to baseline");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(player.gameObject);
        }
    }

    private static void ValidateEnemyBurningAndFrozen()
    {
        PlayerNetworkState player = CreatePlayer("Enemy Debuff Source Validation");
        EnemyHealth enemy = CreateEnemy("Enemy Debuff Target Validation");
        try
        {
            EnemyStatusVfxConfig globalVfxConfig = EnemyStatusVfxConfig.LoadDefault();
            Require(globalVfxConfig != null, "global enemy status VFX config is missing");
            Require(enemy.StatusVfxPresenter.Config == globalVfxConfig,
                "enemy presenter did not resolve the shared global VFX config");

            BloodPactDefinition burningPact = FindPact("update_015");
            BloodPactDefinition frozenPact = FindPact("update_016");
            Require(burningPact.IsRuntimeImplemented, "Ember Fire is not marked implemented");
            Require(frozenPact.IsRuntimeImplemented, "Frost Breath is not marked implemented");

            GameplayAbilityDefinition burning = MakeDeterministic(
                burningPact.GameplayAbilities[0],
                "validation.enemy_burning");
            GameplayAbilityDefinition frozen = MakeDeterministic(
                frozenPact.GameplayAbilities[0],
                "validation.enemy_frozen");
            Require(player.AbilitySystem.GrantAbilities(
                    new[] { burning, frozen },
                    "enemy_debuff_validation"),
                "enemy debuff abilities failed to grant");

            int healthBeforeBurning = enemy.CurrentHealth;
            GameplayEventData hit = new(
                GameplayEventType.AttackHit,
                player,
                enemy,
                magnitude: 10f,
                position: enemy.transform.position);
            player.AbilitySystem.SendEvent(in hit);

            Require(enemy.AbilitySystem.Tags.Has("State.Debuff.Burning"),
                "burning gameplay tag was not applied to enemy");
            ValidateOverlayTargets(enemy);
            if (!Application.isBatchMode)
            {
                Require(enemy.StatusVfxPresenter.IsStatusVisible(
                        EnemyStatusVfxPresenter.BurningCueTag),
                    "burning persistent VFX cue was not presented");
            }

            enemy.AbilitySystem.Tick(1f);
            Require(healthBeforeBurning - enemy.CurrentHealth == 2,
                "burning did not deal two server-side damage on its first period");
            enemy.AbilitySystem.Tick(3.01f);
            Require(healthBeforeBurning - enemy.CurrentHealth == 8,
                "burning did not deal four two-damage periods");
            Require(!enemy.AbilitySystem.Tags.Has("State.Debuff.Burning"),
                "burning gameplay tag did not expire");

            player.AbilitySystem.SendEvent(in hit);
            Require(enemy.IsFrozen, "frozen control tag was not applied to enemy");
            if (!Application.isBatchMode)
            {
                Require(enemy.StatusVfxPresenter.IsStatusVisible(
                        EnemyStatusVfxPresenter.FrozenCueTag),
                    "frozen persistent VFX cue was not presented");
            }

            enemy.AbilitySystem.Tick(2.01f);
            Require(!enemy.IsFrozen, "frozen control tag did not expire");
            if (!Application.isBatchMode)
            {
                Require(!enemy.StatusVfxPresenter.IsStatusVisible(
                        EnemyStatusVfxPresenter.FrozenCueTag),
                    "frozen VFX remained after the effect expired");
            }

            float baselineMoveSpeed = enemy.GetStatValue(EnemyStatType.MoveSpeed);
            GameplayEffectDefinition slowEffect = new(
                "validation.enemy_slow",
                GameplayEffectDurationPolicy.Duration,
                modifiers: new[]
                {
                    new GameplayModifierDefinition(
                        GameplayAttributeType.MoveSpeed,
                        PlayerModifierOperation.Multiplicative,
                        new GameplayMagnitudeDefinition(GameplayMagnitudeSource.Fixed, 0.5f))
                },
                durationSeconds: 0.5f,
                executeOnApplication: false,
                grantedTags: new[] { "State.Debuff.Slow" });
            GameplayAbilityDefinition slowAbility = new(
                "validation.enemy_slow",
                GameplayEventType.CriticalHit,
                GameplayAbilityTarget.EventTarget,
                new[] { slowEffect });
            Require(player.AbilitySystem.GrantAbilities(
                    new[] { slowAbility },
                    "enemy_slow_validation"),
                "enemy instance slow failed to grant");
            GameplayEventData criticalHit = new(
                GameplayEventType.CriticalHit,
                player,
                enemy,
                magnitude: 1f);
            player.AbilitySystem.SendEvent(in criticalHit);
            RequireClose(
                enemy.GetStatValue(EnemyStatType.MoveSpeed),
                baselineMoveSpeed * 0.5f,
                "enemy instance modifier did not affect only the target value");
            enemy.AbilitySystem.Tick(0.51f);
            RequireClose(
                enemy.GetStatValue(EnemyStatType.MoveSpeed),
                baselineMoveSpeed,
                "enemy instance modifier did not expire cleanly");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(player.gameObject);
            UnityEngine.Object.DestroyImmediate(enemy.gameObject);
        }
    }

    private static GameplayAbilityDefinition MakeDeterministic(
        GameplayAbilityDefinition source,
        string abilityId)
    {
        Require(source != null, $"source ability for '{abilityId}' is missing");
        return new GameplayAbilityDefinition(
            abilityId,
            source.TriggerEvent,
            source.Target,
            source.Effects,
            chance: 1f,
            internalCooldownSeconds: 0f,
            requiredOwnerTags: source.RequiredOwnerTags as string[],
            blockedOwnerTags: source.BlockedOwnerTags as string[]);
    }

    private static void ValidateOverlayTargets(EnemyHealth enemy)
    {
        OverlayFX[] overlays = enemy.GetComponentsInChildren<OverlayFX>(true);
        if (overlays.Length == 0) return;

        MeshRenderer expected = enemy.GetComponent<MeshRenderer>();
        Require(expected != null, "enemy overlay validation mesh is missing");
        for (int index = 0; index < overlays.Length; index++)
        {
            Require(overlays[index].targetRenderer == expected,
                "status OverlayFX was not bound to the enemy mesh renderer");
        }
    }

    private static PlayerNetworkState CreatePlayer(string name)
    {
        GameObject gameObject = new(name);
        PlayerNetworkState player = gameObject.AddComponent<PlayerNetworkState>();
        if (player.AbilitySystem == null)
            typeof(PlayerNetworkState)
                .GetMethod(
                    "Awake",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(player, null);
        return player;
    }

    private static EnemyHealth CreateEnemy(string name)
    {
        GameObject gameObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        gameObject.name = name;
        EnemyHealth enemy = gameObject.AddComponent<EnemyHealth>();
        if (enemy.AbilitySystem == null)
        {
            typeof(EnemyHealth)
                .GetMethod(
                    "Awake",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.Invoke(enemy, null);
        }
        return enemy;
    }

    private static BloodPactDefinition FindPact(string pactId)
    {
        BloodPactConfig database = BloodPactConfig.LoadDefault();
        Require(database != null, "BloodPacts database is missing");
        Require(database.TryGet(pactId, out BloodPactDefinition pact),
            $"pact '{pactId}' is missing");
        return pact;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"[GAS Validation] {message}");
    }

    private static void RequireClose(float actual, float expected, string message)
    {
        if (Mathf.Abs(actual - expected) > 0.001f)
            throw new InvalidOperationException(
                $"[GAS Validation] {message}. Expected {expected}, got {actual}.");
    }
}
