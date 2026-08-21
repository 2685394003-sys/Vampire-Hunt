using System;
using System.IO;
using NUnit.Framework;

namespace VampireHunt.Tests.Player
{
    public sealed class PlayerAttackPresenterContractTests
    {
        private const string PlayerPrefabPath = "Assets/Prefabs/Network/Player.prefab";
        private const string PresenterPath = "Assets/Scripts/Player/PlayerAttackPresenter.cs";
        private const string ControllerPath = "Assets/Scripts/Player/PlayerController.cs";
        private const string NetworkStatePath = "Assets/Scripts/Player/PlayerNetworkState.cs";
        private const string AttackAnimationPath =
            "Assets/Sprites/Temporary assets/player/idle/攻击动画/gongji Animation.anim";
        private const string LegacyTypeName = "Player" + "Attact";
        private const string LegacyImpactEventName = "Attack" + "false";
        private const string LegacyDamageEventName = "Deal" + "Damage";

        [Test]
        public void Presenter_UsesOnlyTheMovedFromMarkerForTheRetiredType()
        {
            string presenter = File.ReadAllText(PresenterPath);

            Assert.That(Count(presenter, LegacyTypeName), Is.EqualTo(1));
            Assert.That(presenter, Does.Contain("[MovedFrom"));
            Assert.That(presenter, Does.Contain("[FormerlySerializedAs"));
            Assert.That(presenter, Does.Contain("OnAttackImpact"));
            Assert.That(presenter, Does.Contain("OnAttackAnimationCompleted"));
            Assert.That(presenter, Does.Not.Contain(LegacyImpactEventName));
            Assert.That(presenter, Does.Not.Contain(LegacyDamageEventName));
            Assert.That(presenter, Does.Not.Contain("RequestAttack"));
            Assert.That(presenter, Does.Not.Contain("SubmitAttack"));

            string controller = File.ReadAllText(ControllerPath);
            Assert.That(controller, Does.Contain("PlayerAttackPresenter _attackPresenter"));
            Assert.That(controller, Does.Contain("FormerlySerializedAs(\"playerAttack\")"));
            Assert.That(controller, Does.Not.Contain(LegacyTypeName));
            Assert.That(controller, Does.Not.Contain("SubmitAttackIntentFromAnimation"));
            Assert.That(controller, Does.Not.Contain("ServerTryStartDash"));
            Assert.That(controller, Does.Not.Contain("RequestDash("));

            string networkState = File.ReadAllText(NetworkStatePath);
            Assert.That(networkState, Does.Not.Contain("BloodPactScarletCost"));
            Assert.That(networkState, Does.Not.Contain("RollAttackDamage"));
            Assert.That(networkState, Does.Not.Contain("ReportAttackHit"));
            Assert.That(networkState, Does.Not.Contain("ReportGameplayEffectDamage"));
            Assert.That(networkState, Does.Not.Contain("ReportEnemyKilled"));
            Assert.That(networkState, Does.Contain("EmitGameplayCue"));
        }

        [Test]
        public void PlayerPrefab_UsesPresenterAndHasNoRetiredPlayerComponents()
        {
            string prefab = File.ReadAllText(PlayerPrefabPath);

            Assert.That(Count(prefab, "guid: 95030f8ba26c52846a5011176d9f348e"), Is.EqualTo(1));
            Assert.That(prefab, Does.Contain("PlayerAttackPresenter"));
            Assert.That(prefab, Does.Not.Contain("Assembly-CSharp::" + LegacyTypeName));
            Assert.That(prefab, Does.Not.Contain("Assembly-CSharp::AttackDamageForwarder"));
            Assert.That(prefab, Does.Not.Contain("Assembly-CSharp::PlayerMovement"));
            Assert.That(prefab, Does.Contain("_attackPresenter: {fileID: 755214740615585287}"));
            Assert.That(prefab, Does.Contain("_animator: {fileID: 3369520545468263824}"));
            Assert.That(prefab, Does.Contain("_attackAnimationDuration: 1.25"));
            Assert.That(prefab, Does.Not.Contain("_attackPoint:"));
            Assert.That(prefab, Does.Not.Contain("_hitDelay:"));
            Assert.That(prefab, Does.Not.Contain("_swordSlash"));
            Assert.That(prefab, Does.Not.Contain("m_Name: PlayerAttackpoint"));
            Assert.That(prefab, Does.Not.Contain("686029f596249e146921796244635e44"));
        }

        [Test]
        public void AttackAnimation_UsesSemanticPresentationEvents()
        {
            string animation = File.ReadAllText(AttackAnimationPath);

            Assert.That(animation, Does.Contain("functionName: OnAttackImpact"));
            Assert.That(animation, Does.Contain("functionName: OnAttackAnimationCompleted"));
            Assert.That(animation, Does.Not.Contain($"functionName: {LegacyImpactEventName}"));
            Assert.That(animation, Does.Not.Contain($"functionName: {LegacyDamageEventName}"));
        }

        [Test]
        public void RetiredPlayerScriptAssetsAreRemoved()
        {
            Assert.That(File.Exists($"Assets/Scripts/Player/{LegacyTypeName}.cs"), Is.False);
            Assert.That(File.Exists($"Assets/Scripts/Player/{LegacyTypeName}.cs.meta"), Is.False);
            Assert.That(File.Exists("Assets/Scripts/Player/Player Movement.cs"), Is.False);
            Assert.That(File.Exists("Assets/Scripts/Player/Player Movement.cs.meta"), Is.False);
            Assert.That(File.Exists("Assets/Scripts/Player/AttackDamageForwarder.cs"), Is.False);
            Assert.That(File.Exists("Assets/Scripts/Player/AttackDamageForwarder.cs.meta"), Is.False);
            Assert.That(File.ReadAllText(PresenterPath + ".meta"), Does.Contain(
                "guid: 95030f8ba26c52846a5011176d9f348e"));
        }

        private static int Count(string source, string value)
        {
            int count = 0;
            int offset = 0;
            while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
            {
                count++;
                offset += value.Length;
            }

            return count;
        }
    }
}
