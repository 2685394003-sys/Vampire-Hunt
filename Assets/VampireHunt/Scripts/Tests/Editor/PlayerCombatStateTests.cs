using System;
using Blocks.Gameplay.Core;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Netcode.Player;
using VampireHunt.Player;

namespace VampireHunt.Tests.Editor
{
    public sealed class PlayerCombatStateTests
    {
        private static PlayerCombatStateController Alive(double delay = 5)
        {
            var state = new PlayerCombatStateController(delay);
            state.Reset(true);
            return state;
        }

        [Test] public void ActionWithoutDamage_EntersCombatAndRefreshesDeadline()
        {
            var state = Alive();
            Assert.That(state.State, Is.EqualTo(PlayerCombatState.NonCombat));
            state.Record(0); // Whiff, cancelled effect or immune contact: no health input exists.
            state.Record(3);
            state.Tick(5);
            Assert.That(state.State, Is.EqualTo(PlayerCombatState.Combat));
            state.Tick(8);
            Assert.That(state.State, Is.EqualTo(PlayerCombatState.NonCombat));
        }

        [Test] public void NewActivityAtDeadline_WinsOverExpiry()
        {
            var state = Alive();
            state.Record(0);
            state.Record(5);
            state.Tick(5);
            Assert.That(state.ExitAt, Is.EqualTo(10));
            Assert.That(state.State, Is.EqualTo(PlayerCombatState.Combat));
        }

        [Test] public void SustainedAction_CannotExpireWhileChanneling()
        {
            var state = Alive();
            uint life = state.Generation;
            Assert.That(state.Begin(10, life, 0), Is.True);
            Assert.That(state.Begin(10, life, 1), Is.False);
            state.Tick(20);
            Assert.That(state.State, Is.EqualTo(PlayerCombatState.Combat));
            state.End(10, life, 20);
            state.Tick(24.99);
            Assert.That(state.State, Is.EqualTo(PlayerCombatState.Combat));
            state.Tick(25);
            Assert.That(state.State, Is.EqualTo(PlayerCombatState.NonCombat));
        }

        [Test] public void MultipleChannels_RequireLastChannelToEnd()
        {
            var state = Alive();
            state.Begin(1, state.Generation, 0);
            state.Begin(2, state.Generation, 0);
            state.End(1, state.Generation, 1);
            state.Tick(100);
            Assert.That(state.State, Is.EqualTo(PlayerCombatState.Combat));
            state.End(2, state.Generation, 100);
            state.Tick(105);
            Assert.That(state.State, Is.EqualTo(PlayerCombatState.NonCombat));
        }

        [Test] public void DeathAndRespawn_InvalidateOldChannelCallbacks()
        {
            var state = Alive();
            uint oldLife = state.Generation;
            state.Begin(1, oldLife, 0);
            state.Reset(false);
            state.Record(4);
            Assert.That(state.State, Is.EqualTo(PlayerCombatState.NonCombat));
            state.Reset(true);
            Assert.That(state.End(1, oldLife, 5), Is.False);
            Assert.That(state.Begin(1, oldLife, 5), Is.False);
            Assert.That(state.State, Is.EqualTo(PlayerCombatState.NonCombat));
        }

        [Test] public void ZeroDelay_ExpiresOnTickNotInsideActivity()
        {
            var state = Alive(0);
            state.Record(1);
            Assert.That(state.State, Is.EqualTo(PlayerCombatState.Combat));
            state.Tick(1);
            Assert.That(state.State, Is.EqualTo(PlayerCombatState.NonCombat));
        }

        [Test] public void InvalidTiming_IsRejected()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerCombatStateController(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlayerCombatStateController(-1));
        }

        [Test] public void Recovery_DoesNotIncludeTimeBeforeDelayOrDuringPause()
        {
            Assert.That(RegenerationMath.Amount(4.9, 5.05, 5, 100), Is.EqualTo(5).Within(0.0001));
            Assert.That(RegenerationMath.Amount(5, 5, 0, 100), Is.Zero);
            Assert.That(RegenerationMath.Amount(0, 1, 2, 100), Is.Zero);
            Assert.That(RegenerationMath.Amount(0, 1, 0, float.NaN), Is.Zero);
        }

        [Test] public void Recovery_RateSwitchSplitsIntervalWithoutDoubleCounting()
        {
            // Combat fixed 10/s up to t=5; out-of-combat 10000/60 per second afterwards.
            float amount = RegenerationMath.Amount(4.9, 5, 0, 10) +
                RegenerationMath.Amount(5, 5.1, 0, 10000f / 60);
            Assert.That(amount, Is.EqualTo(17.666667).Within(0.0001));
        }

        [Test] public void PlayerPrefab_HasExactlyOneRecoveryOwnerAndTemplateHealthRateRemainsZero()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VampireHunt/Prefabs/Characters/VH_Player.prefab");
            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponents<PlayerCombatStateHost>().Length, Is.EqualTo(1));
            var host = prefab.GetComponent<PlayerCombatStateHost>();
            Assert.That(host.GetRate(StatKeys.Health, 10000, 0), Is.EqualTo(10000f / 60).Within(0.001));
            Assert.That(host.GetRate(StatKeys.Stamina, 100, 15), Is.EqualTo(30));
            Assert.That(host.GetDelay(StatKeys.Stamina, StatUseKind.Burst, 0.4f), Is.EqualTo(0.2f));
            Assert.That(host.GetDelay(StatKeys.Stamina, StatUseKind.Standard, 0.4f), Is.EqualTo(0.4f));
            Assert.That(host.GetDelay(StatKeys.Stamina, StatUseKind.Continuous, 0.4f), Is.Zero);
            var health = AssetDatabase.LoadAssetAtPath<StatDefinition>("Assets/VampireHunt/Data/Player/Attributes/HealthStat.asset");
            Assert.That(health.regenRate, Is.Zero);
            Assert.That(AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/VampireHunt/Scripts/Infrastructure/Netcode/Player/PlayerHealthRegen.cs"), Is.Null);
            Assert.That(AssetDatabase.LoadAssetAtPath<MonoScript>("Assets/VampireHunt/Scripts/Infrastructure/Netcode/Player/StaminaRegenBoost.cs"), Is.Null);
        }
    }
}
