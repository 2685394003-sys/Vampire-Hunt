using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Unity;
using VampireHunt.Presentation.Combat;
using Object = UnityEngine.Object;

namespace VampireHunt.Tests.Editor
{
    public sealed class StatusMeshVfxTests
    {
        private const string EnemyPath = "Assets/VampireHunt/Prefabs/Enemies/VH_Melee.prefab";
        private Scene m_Scene;

        [SetUp] public void SetUp() => m_Scene = EditorSceneManager.NewPreviewScene();
        [TearDown] public void TearDown() => EditorSceneManager.ClosePreviewScene(m_Scene);

        private GameObject Enemy(string path = EnemyPath) =>
            (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), m_Scene);

        private static StatusEffectPresentationPayload Status(uint id) =>
            new StatusEffectPresentationPayload { statusId = id, stacks = 1 };

        private static void AssertRestored(Renderer renderer, Material[] baseline) =>
            CollectionAssert.AreEqual(baseline, renderer.sharedMaterials);

        [Test]
        public void BurnAndFrozen_RefreshRetainsInstances_IndependentRemovalRestoresMaterials()
        {
            var enemy = Enemy();
            var driver = enemy.GetComponent<StatusEffectVfxDriver>();
            var renderer = enemy.GetComponentInChildren<SkinnedMeshRenderer>();
            var baseline = renderer.sharedMaterials;
            driver.ApplyStatus(Status(StatusEffectIds.Burn), enemy.transform);
            var fire = enemy.GetComponentsInChildren<OverlayFX>().Single();
            Assert.That(fire.targetRenderer, Is.SameAs(renderer));
            Assert.That(renderer.sharedMaterials.Length, Is.EqualTo(baseline.Length + 1));
            driver.ApplyStatus(Status(StatusEffectIds.Burn), enemy.transform);
            Assert.That(enemy.GetComponentsInChildren<OverlayFX>().Single(), Is.SameAs(fire));
            driver.ApplyStatus(Status(StatusEffectIds.Frozen), enemy.transform);
            Assert.That(renderer.sharedMaterials.Length, Is.EqualTo(baseline.Length + 2));
            Assert.That(enemy.GetComponentsInChildren<MeshRenderer>().Any(r => r.name.StartsWith("ElementGlow_")), Is.False);

            foreach (var overlay in enemy.GetComponentsInChildren<OverlayFX>())
            foreach (var particles in overlay.particleSystems)
            {
                Assert.That(particles.shape.shapeType, Is.EqualTo(ParticleSystemShapeType.SkinnedMeshRenderer));
                Assert.That(particles.shape.skinnedMeshRenderer, Is.SameAs(renderer));
                Assert.That(renderer.sharedMesh.isReadable, Is.True);
                particles.Simulate(0.2f, false, true);
            }

            driver.RemoveStatus(Status(StatusEffectIds.Burn), enemy.transform);
            Assert.That(renderer.sharedMaterials.Length, Is.EqualTo(baseline.Length + 1));
            Assert.That(enemy.GetComponentsInChildren<OverlayFX>().Single().overlayMaterial.name, Does.Contain("Frost"));
            driver.RemoveStatus(Status(StatusEffectIds.Frozen), enemy.transform);
            AssertRestored(renderer, baseline);
        }

        [Test]
        public void TwoEnemies_DoNotShareRuntimeMaterials_ClearingDriverCleansOnlyItsEnemy()
        {
            var first = Enemy();
            var second = Enemy();
            var a = first.GetComponent<StatusEffectVfxDriver>();
            var b = second.GetComponent<StatusEffectVfxDriver>();
            var ra = first.GetComponentInChildren<SkinnedMeshRenderer>();
            var rb = second.GetComponentInChildren<SkinnedMeshRenderer>();
            var baseline = ra.sharedMaterials;
            a.ApplyStatus(Status(StatusEffectIds.Burn), first.transform);
            b.ApplyStatus(Status(StatusEffectIds.Burn), second.transform);
            Assert.That(ra.sharedMaterials.Last(), Is.Not.SameAs(rb.sharedMaterials.Last()));
            // In EditMode ordinary MonoBehaviours do not receive OnDisable; Clear is the
            // production disable/despawn cleanup entry point invoked by the presenter.
            a.Clear(first.transform);
            AssertRestored(ra, baseline);
            Assert.That(rb.sharedMaterials.Length, Is.EqualTo(baseline.Length + 1));
            b.Clear(second.transform);
            AssertRestored(rb, baseline);
        }

        [Test]
        public void Boss_AllBodyPartsCovered_OnlyOneParticleSet_PerStatus()
        {
            var boss = Enemy("Assets/VampireHunt/Prefabs/Boss/VH_BossHand.prefab");
            var driver = boss.GetComponent<StatusEffectVfxDriver>();
            var renderers = boss.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var baselines = renderers.ToDictionary(r => r, r => r.sharedMaterials);
            driver.ApplyStatus(Status(StatusEffectIds.Burn), boss.transform);
            driver.ApplyStatus(Status(StatusEffectIds.Frozen), boss.transform);
            foreach (var renderer in renderers)
            {
                Assert.That(renderer.sharedMesh.subMeshCount, Is.EqualTo(1), renderer.name);
                Assert.That(renderer.sharedMaterials.Length, Is.EqualTo(baselines[renderer].Length + 2), renderer.name);
            }
            var overlays = boss.GetComponentsInChildren<OverlayFX>();
            Assert.That(overlays.Length, Is.EqualTo(renderers.Length * 2));
            Assert.That(overlays.Count(o => o.particleSystems.Count > 0), Is.EqualTo(2));
            foreach (var o in overlays.Where(o => o.particleSystems.Count > 0))
                Assert.That(o.targetRenderer.name, Is.EqualTo("016_衣"));
            driver.RemoveStatus(Status(StatusEffectIds.Frozen), boss.transform);
            foreach (var renderer in renderers)
                Assert.That(renderer.sharedMaterials.Length, Is.EqualTo(baselines[renderer].Length + 1));
            driver.Clear(boss.transform);
            foreach (var renderer in renderers) AssertRestored(renderer, baselines[renderer]);
        }

        [Test]
        public void StaticMeshEnemy_BindsMeshRenderer_AndClearRestoresMaterials()
        {
            var enemy = Enemy("Assets/VampireHunt/Prefabs/Enemies/VH_MeleeEnemy.prefab");
            var driver = enemy.GetComponent<StatusEffectVfxDriver>();
            var renderers = enemy.GetComponentsInChildren<MeshRenderer>(true);
            Assert.That(renderers.Length, Is.GreaterThan(0));
            var baselines = renderers.ToDictionary(r => r, r => r.sharedMaterials);
            driver.ApplyStatus(Status(StatusEffectIds.Frozen), enemy.transform);
            var effects = enemy.GetComponentsInChildren<OverlayFX>();
            Assert.That(effects.Length, Is.EqualTo(renderers.Length));
            foreach (var fx in effects)
            foreach (var ps in fx.particleSystems)
                Assert.That(ps.shape.meshRenderer, Is.SameAs(fx.targetRenderer));
            driver.Clear(enemy.transform);
            foreach (var renderer in renderers) AssertRestored(renderer, baselines[renderer]);
        }

        [Test]
        public void RebindOverlay_LeavesOtherEffectAndOriginalHiddenMaterialUntouched()
        {
            var first = Enemy();
            var second = Enemy();
            var driver = first.GetComponent<StatusEffectVfxDriver>();
            var a = first.GetComponentInChildren<SkinnedMeshRenderer>();
            var b = second.GetComponentInChildren<SkinnedMeshRenderer>();
            var baseA = a.sharedMaterials;
            var baseB = b.sharedMaterials;
            var unrelated = new Material(baseA[0]) { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                a.sharedMaterials = baseA.Concat(new[] { unrelated, (Material)null }).ToArray();
                var originalSlots = a.sharedMaterials;
                driver.ApplyStatus(Status(StatusEffectIds.Burn), first.transform);
                var fire = first.GetComponentsInChildren<OverlayFX>().Single();
                driver.ApplyStatus(Status(StatusEffectIds.Frozen), first.transform);
                fire.SetTargetRenderer(b);
                Assert.That(a.sharedMaterials.Length, Is.EqualTo(originalSlots.Length + 1));
                Assert.That(a.sharedMaterials, Does.Contain(unrelated));
                Assert.That(b.sharedMaterials.Length, Is.EqualTo(baseB.Length + 1));
                driver.Clear(first.transform);
                AssertRestored(a, originalSlots);
                AssertRestored(b, baseB);
            }
            finally
            {
                a.sharedMaterials = baseA;
                Object.DestroyImmediate(unrelated);
            }
        }

        [Test]
        public void EnemyPrefabsShareConfig_PlayerHasNoMeshDriver()
        {
            var config = AssetDatabase.LoadAssetAtPath<StatusMeshVfxConfig>("Assets/VampireHunt/Presentation/StatusMeshVfxConfig.asset");
            Assert.That(config.GetPrefab(StatusEffectIds.Burn).name, Is.EqualTo("MeshFX_Fire"));
            Assert.That(config.GetPrefab(StatusEffectIds.Frozen).name, Is.EqualTo("MeshFX_Frozen"));
            Assert.That(config.GetPrefab(StatusEffectIds.Frost), Is.Null);
            foreach (var path in new[] { EnemyPath, "Assets/VampireHunt/Prefabs/Enemies/VH_Ranged.prefab", "Assets/VampireHunt/Prefabs/Enemies/VH_MeleeEnemy.prefab", "Assets/VampireHunt/Prefabs/Boss/VH_BossHand.prefab" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var driver = prefab.GetComponent<StatusEffectVfxDriver>();
                Assert.That(new SerializedObject(driver).FindProperty("meshVfxConfig").objectReferenceValue, Is.SameAs(config));
                foreach (var renderer in prefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    Assert.That(renderer.sharedMesh.subMeshCount, Is.EqualTo(1), path + "/" + renderer.name);
            }
            var player = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VampireHunt/Prefabs/Characters/VH_Player.prefab");
            Assert.That(player.GetComponent<StatusEffectVfxDriver>(), Is.Null);
        }
    }
}
