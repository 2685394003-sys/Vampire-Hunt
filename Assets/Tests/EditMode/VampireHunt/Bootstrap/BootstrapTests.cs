using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using VampireHunt.Boss.Authoring;
using VampireHunt.Boss.Contracts;
using VampireHunt.Bootstrap;
using VampireHunt.Player.Authoring;
using VampireHunt.Spawning.Authoring;

namespace VampireHunt.Tests.Bootstrap
{
    public sealed class BootstrapTests
    {
        [Test]
        public void ConfigCatalog_BuildSpecsConvertsAuthoringWithoutRetainingAssetsAsRuntimeSpecs()
        {
            PlayerDefinition player = ScriptableObject.CreateInstance<PlayerDefinition>();
            BossDefinition boss = CreateBossDefinition();
            EnemySpawnConfig spawn = ScriptableObject.CreateInstance<EnemySpawnConfig>();
            ConfigCatalog catalog = ScriptableObject.CreateInstance<ConfigCatalog>();

            try
            {
                catalog.SetAuthoring(player, boss, spawn);
                GameSpecs specs = catalog.BuildSpecs();

                Assert.That(specs.Player.MaxHealth, Is.EqualTo(player.MaxHealth));
                Assert.That(specs.Boss.MaxHealth, Is.EqualTo(boss.MaxHealth));
                Assert.That(specs.EnemySpawn.SpawnInterval, Is.EqualTo(spawn.SpawnInterval));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(catalog);
                UnityEngine.Object.DestroyImmediate(spawn);
                UnityEngine.Object.DestroyImmediate(boss);
                UnityEngine.Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void CompositionRoot_InstallsInOrderAndDisposesInReverseOrder()
        {
            ConfigCatalog catalog = CreateCatalog(out List<UnityEngine.Object> assets);
            List<string> order = new();
            GameplayModuleInstaller gameplay = new(new RecordingFactory(order, "gameplay"));
            NetcodeModuleInstaller netcode = new(
                offlineFactory: new RecordingFactory(order, "offline"));
            PresentationModuleInstaller presentation = new(new RecordingFactory(order, "presentation"));
            UiModuleInstaller ui = new(new RecordingFactory(order, "ui"));
            GameCompositionRoot root = new(gameplay, netcode, presentation, ui);

            try
            {
                root.Compose(catalog);
                Assert.That(root.IsComposed, Is.True);
                CollectionAssert.AreEqual(
                    new[] { "gameplay", "offline", "presentation", "ui" },
                    order);

                root.Dispose();
                CollectionAssert.AreEqual(
                    new[] { "gameplay", "offline", "presentation", "ui", "ui.dispose", "presentation.dispose", "offline.dispose", "gameplay.dispose" },
                    order);
                Assert.That(root.IsComposed, Is.False);
                root.Dispose();
            }
            finally
            {
                root.Dispose();
                DestroyAssets(assets);
            }
        }

        [Test]
        public void DedicatedServer_SkipsPresentationAndUiInstallers()
        {
            ConfigCatalog catalog = CreateCatalog(out List<UnityEngine.Object> assets);
            int presentationCreates = 0;
            int uiCreates = 0;
            GameCompositionRoot root = new(
                presentationInstaller: new PresentationModuleInstaller(
                    new DelegateModuleFactory(_ =>
                    {
                        presentationCreates++;
                        return null;
                    })),
                uiInstaller: new UiModuleInstaller(
                    new DelegateModuleFactory(_ =>
                    {
                        uiCreates++;
                        return null;
                    })));

            try
            {
                root.Compose(catalog, RuntimeMode.Offline, isDedicatedServer: true);
                Assert.That(root.Context.IsDedicatedServer, Is.True);
                Assert.That(root.Context.PresentationEnabled, Is.False);
                Assert.That(root.PresentationInstaller.IsInstalled, Is.False);
                Assert.That(root.UiInstaller.IsInstalled, Is.False);
                Assert.That(presentationCreates, Is.Zero);
                Assert.That(uiCreates, Is.Zero);
            }
            finally
            {
                root.Dispose();
                DestroyAssets(assets);
            }
        }

        [Test]
        public void NetcodeInstaller_RequiresExplicitNetcodeFactory()
        {
            GameCompositionContext context = CreateContext(RuntimeMode.Netcode, false, out List<UnityEngine.Object> assets);
            NetcodeModuleInstaller installer = new();
            try
            {
                Assert.Throws<InvalidOperationException>(() => installer.Install(context));
            }
            finally
            {
                installer.Dispose();
                context.Dispose();
                DestroyAssets(assets);
            }
        }

        private static ConfigCatalog CreateCatalog(out List<UnityEngine.Object> assets)
        {
            PlayerDefinition player = ScriptableObject.CreateInstance<PlayerDefinition>();
            BossDefinition boss = CreateBossDefinition();
            EnemySpawnConfig spawn = ScriptableObject.CreateInstance<EnemySpawnConfig>();
            ConfigCatalog catalog = ScriptableObject.CreateInstance<ConfigCatalog>();
            catalog.SetAuthoring(player, boss, spawn);
            assets = new List<UnityEngine.Object> { catalog, spawn, boss, player };
            return catalog;
        }

        private static GameCompositionContext CreateContext(
            RuntimeMode mode,
            bool dedicated,
            out List<UnityEngine.Object> assets)
        {
            ConfigCatalog catalog = CreateCatalog(out assets);
            return new GameCompositionContext(
                null,
                catalog,
                catalog.BuildSpecs(),
                mode,
                dedicated);
        }

        private static BossDefinition CreateBossDefinition()
        {
            BossDefinition boss = ScriptableObject.CreateInstance<BossDefinition>();
            BossPhaseDefinition phase = new();
            BossAttackDefinition attack = ScriptableObject.CreateInstance<BossAttackDefinition>();
            SetField(phase, "phase", BossPhase.PhaseOne);
            SetField(phase, "enterAtHealthRatio", 1f);
            SetField(boss, "phases", new[] { phase });
            SetField(boss, "attacks", new[] { attack });
            return boss;
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) throw new MissingFieldException(target.GetType().FullName, fieldName);
            field.SetValue(target, value);
        }

        private static void DestroyAssets(List<UnityEngine.Object> assets)
        {
            for (int i = 0; i < assets.Count; i++)
            {
                if (assets[i] != null) UnityEngine.Object.DestroyImmediate(assets[i]);
            }
        }

        private sealed class RecordingFactory : IModuleFactory
        {
            private readonly List<string> order;
            private readonly string name;

            public RecordingFactory(List<string> order, string name)
            {
                this.order = order;
                this.name = name;
            }

            public IDisposable Create(GameCompositionContext context)
            {
                order.Add(name);
                return new DelegateDisposable(() => order.Add(name + ".dispose"));
            }
        }

        private sealed class DelegateDisposable : IDisposable
        {
            private Action action;

            public DelegateDisposable(Action action)
            {
                this.action = action;
            }

            public void Dispose()
            {
                Action current = action;
                action = null;
                current?.Invoke();
            }
        }
    }
}
