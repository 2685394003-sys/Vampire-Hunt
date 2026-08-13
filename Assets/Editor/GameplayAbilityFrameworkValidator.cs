using System;
using UnityEditor;
using UnityEngine;

/// <summary>Fast integration checks for the GAS core and the two shipped pacts.</summary>
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
        return "[GAS Validation] Passed tags, duration, stacking/periods, Scarlet Kiss, and Berserker Rage.";
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
                        PlayerStatType.BaseAttack,
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
