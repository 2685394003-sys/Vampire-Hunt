using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VampireHunt.Bootstrap;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;
using VampireHunt.Core.Contracts;
using VampireHunt.Infrastructure.Netcode;
using VampireHunt.Infrastructure.Netcode.Contracts;

namespace VampireHunt.Tests.Netcode
{
    public sealed class NetworkTopologyContractTests
    {
        private const string SampleScenePath = "Assets/Scenes/SampleScene.unity";
        private const string NetworkManagerTypeName = "Unity.Netcode.NetworkManager";
        private const string NetworkCommandAdapterTypeName = "VampireHunt.Infrastructure.Netcode.NetworkCommandRpcAdapter";
        private const string NetworkStateAdapterTypeName = "VampireHunt.Infrastructure.Netcode.NetworkStateRpcAdapter";
        private const string GameplayEventAdapterTypeName = "VampireHunt.Infrastructure.Netcode.GameplayEventRpcAdapter";

        [Test]
        public void HostAndOneClient_ReceiveConfirmedDamageExactlyOnce()
        {
            RuntimeGameplayEventHub serverHub = new();
            GameplayEventReplicator hostReplicator = new();
            GameplayEventReplicator remoteReplicator = new();
            RecordingIngress hostPresentation = new();
            RecordingIngress remotePresentation = new();
            using IDisposable outbound = serverHub.AddOutbound(hostReplicator);
            using IDisposable local = serverHub.AddLocalIngress(hostPresentation);
            MulticastEventTransport transport = new(
                (hostReplicator, hostPresentation),
                (remoteReplicator, remotePresentation));

            serverHub.Publish(CreateDamageEvent(101UL));
            Assert.That(hostReplicator.Flush(transport), Is.EqualTo(1));

            Assert.That(hostPresentation.Events, Has.Count.EqualTo(1));
            Assert.That(remotePresentation.Events, Has.Count.EqualTo(1));
            Assert.That(hostPresentation.Events[0].EventId, Is.EqualTo(101UL));
            Assert.That(remotePresentation.Events[0].EventId, Is.EqualTo(101UL));
        }

        [Test]
        public void DedicatedServerAndTwoClients_ReceiveTheSameConfirmedDamage()
        {
            RuntimeGameplayEventHub serverHub = new();
            GameplayEventReplicator serverReplicator = new();
            GameplayEventReplicator firstClient = new();
            GameplayEventReplicator secondClient = new();
            RecordingIngress firstPresentation = new();
            RecordingIngress secondPresentation = new();
            using IDisposable outbound = serverHub.AddOutbound(serverReplicator);
            MulticastEventTransport transport = new(
                (firstClient, firstPresentation),
                (secondClient, secondPresentation));

            serverHub.Publish(CreateDamageEvent(202UL));
            Assert.That(serverReplicator.Flush(transport), Is.EqualTo(1));

            Assert.That(firstPresentation.Events, Has.Count.EqualTo(1));
            Assert.That(secondPresentation.Events, Has.Count.EqualTo(1));
            Assert.That(firstPresentation.Events[0].EventId, Is.EqualTo(202UL));
            Assert.That(secondPresentation.Events[0].EventId, Is.EqualTo(202UL));
        }

        [Test]
        public void SampleScene_RuntimeRpcAdapters_AreUniqueAndExplicitlyBound()
        {
            Scene scene = OpenSampleScene(out bool openedByTest);
            try
            {
                List<MonoBehaviour> commands = FindSceneComponentsByType(scene, NetworkCommandAdapterTypeName);
                List<MonoBehaviour> states = FindSceneComponentsByType(scene, NetworkStateAdapterTypeName);
                List<MonoBehaviour> events = FindSceneComponentsByType(scene, GameplayEventAdapterTypeName);
                List<SceneBindings> bindings = FindSceneComponents<SceneBindings>(scene);

                Assert.That(commands, Has.Count.EqualTo(1), DescribeComponents(commands, "NetworkCommandRpcAdapter"));
                Assert.That(states, Has.Count.EqualTo(1), DescribeComponents(states, "NetworkStateRpcAdapter"));
                Assert.That(events, Has.Count.EqualTo(1), DescribeComponents(events, "GameplayEventRpcAdapter"));
                Assert.That(bindings, Has.Count.EqualTo(1), DescribeComponents(bindings, "SceneBindings"));

                SceneBindings binding = bindings[0];
                Assert.That(binding.AdapterBindings, Is.Not.Null, "SampleScene SceneBindings adapter collection is null.");
                AssertBoundExactlyOnce(binding, commands[0]);
                AssertBoundExactlyOnce(binding, states[0]);
                AssertBoundExactlyOnce(binding, events[0]);

                MonoBehaviour[] boundAdapters = binding.AdapterBindings.ToArray();
                Assert.That(
                    boundAdapters.Distinct().Count(),
                    Is.EqualTo(boundAdapters.Length),
                    "SceneBindings must not contain the same adapter component more than once.");
            }
            finally
            {
                if (openedByTest && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void SampleScene_CompositionRoots_AreTopLevelAndOutsideNetworkManagerSubtree()
        {
            Scene scene = OpenSampleScene(out bool openedByTest);
            try
            {
                List<GameObject> adapterRoots = FindSceneRoots(scene, "[RuntimeNetcodeAdapters]");
                List<GameObject> gameplayRoots = FindSceneRoots(scene, "[GameplayRoot]");
                List<MonoBehaviour> networkManagers = FindSceneComponentsByType(scene, NetworkManagerTypeName);

                Assert.That(
                    adapterRoots,
                    Has.Count.EqualTo(1),
                    DescribeGameObjects(adapterRoots, "[RuntimeNetcodeAdapters]"));
                Assert.That(
                    gameplayRoots,
                    Has.Count.EqualTo(1),
                    DescribeGameObjects(gameplayRoots, "[GameplayRoot]"));
                Assert.That(networkManagers, Has.Count.EqualTo(1), DescribeComponents(networkManagers, "NetworkManager"));

                Transform networkManager = networkManagers[0].transform;
                Transform adaptersRoot = adapterRoots[0].transform;
                Transform gameplayRoot = gameplayRoots[0].transform;

                Assert.That(adaptersRoot.parent, Is.Null, "[RuntimeNetcodeAdapters] must be a scene-root object.");
                Assert.That(gameplayRoot.parent, Is.Null, "[GameplayRoot] must be a scene-root object.");
                Assert.That(adaptersRoot.IsChildOf(networkManager), Is.False, "[RuntimeNetcodeAdapters] must not be under NetworkManager.");
                Assert.That(gameplayRoot.IsChildOf(networkManager), Is.False, "[GameplayRoot] must not be under NetworkManager.");

                List<SceneBindings> bindings = FindSceneComponents<SceneBindings>(scene);
                Assert.That(bindings, Has.Count.EqualTo(1), DescribeComponents(bindings, "SceneBindings"));
                Assert.That(
                    bindings[0].GameplayRoot,
                    Is.EqualTo(gameplayRoot),
                    "SceneBindings.GameplayRoot must explicitly reference the independent [GameplayRoot].");
            }
            finally
            {
                if (openedByTest && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void SampleScene_PersistedRuntimeWiring_IsCanonicalAndDuplicateFree()
        {
            // This test is intentionally read-only. Runtime wiring is a
            // persisted Scene/Prefab contract, so tests must inspect the
            // checked-in asset and never invoke an editor authoring utility or
            // mutate production-visible assets.
            Scene scene = OpenSampleScene(out bool openedByTest);
            try
            {
                Assert.That(
                    FindSceneRoots(scene, "[RuntimeNetcodeAdapters]"),
                    Has.Count.EqualTo(1),
                    "Persisted runtime wiring must contain exactly one adapter root.");
                Assert.That(
                    FindSceneRoots(scene, "[GameplayRoot]"),
                    Has.Count.EqualTo(1),
                    "Persisted runtime wiring must contain exactly one gameplay root.");

                Assert.That(
                    FindSceneComponentsByType(scene, NetworkCommandAdapterTypeName),
                    Has.Count.EqualTo(1),
                    "Persisted runtime wiring must not duplicate NetworkCommandRpcAdapter.");
                Assert.That(
                    FindSceneComponentsByType(scene, NetworkStateAdapterTypeName),
                    Has.Count.EqualTo(1),
                    "Persisted runtime wiring must not duplicate NetworkStateRpcAdapter.");
                Assert.That(
                    FindSceneComponentsByType(scene, GameplayEventAdapterTypeName),
                    Has.Count.EqualTo(1),
                    "Persisted runtime wiring must not duplicate GameplayEventRpcAdapter.");
            }
            finally
            {
                if (openedByTest && scene.IsValid() && scene.isLoaded)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static DamageConfirmedEvent CreateDamageEvent(ulong eventId)
        {
            VampireHunt.Core.EntityId source = new(1UL);
            VampireHunt.Core.EntityId target = new(2UL);
            return new DamageConfirmedEvent(
                eventId,
                1d,
                source,
                target,
                new DamageResult(7, 7, false, false, WorldPosition.Origin));
        }

        private static Scene OpenSampleScene(out bool openedByTest)
        {
            Scene scene = SceneManager.GetSceneByPath(SampleScenePath);
            openedByTest = !scene.IsValid() || !scene.isLoaded;
            if (!openedByTest)
                return scene;

            return EditorSceneManager.OpenScene(SampleScenePath, OpenSceneMode.Additive);
        }

        private static List<T> FindSceneComponents<T>(Scene scene) where T : Component
        {
            List<T> result = new();
            foreach (GameObject root in scene.GetRootGameObjects())
                result.AddRange(root.GetComponentsInChildren<T>(true));
            return result;
        }

        private static List<MonoBehaviour> FindSceneComponentsByType(Scene scene, string typeName)
        {
            List<MonoBehaviour> result = new();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                MonoBehaviour[] behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);
                for (int i = 0; i < behaviours.Length; i++)
                {
                    MonoBehaviour behaviour = behaviours[i];
                    if (behaviour != null && string.Equals(behaviour.GetType().FullName, typeName, StringComparison.Ordinal))
                        result.Add(behaviour);
                }
            }

            return result;
        }

        private static List<GameObject> FindSceneRoots(Scene scene, string name)
        {
            return scene.GetRootGameObjects()
                .Where(root => string.Equals(root.name, name, StringComparison.Ordinal))
                .ToList();
        }

        private static void AssertBoundExactlyOnce(SceneBindings bindings, MonoBehaviour expected)
        {
            int count = bindings.AdapterBindings.Count(value => value == expected);
            Assert.That(
                count,
                Is.EqualTo(1),
                $"SceneBindings must bind exactly one instance of {expected.GetType().Name}; found {count}.");
        }

        private static string DescribeComponents<T>(IReadOnlyCollection<T> components, string typeName)
            where T : Component
        {
            string locations = string.Join(
                "; ",
                components.Select(component => component == null
                    ? "<missing>"
                    : GetHierarchyPath(component.transform)));
            return $"Expected exactly one {typeName}; found {components.Count}: {locations}";
        }

        private static string DescribeGameObjects(IReadOnlyCollection<GameObject> objects, string name)
        {
            string locations = string.Join(
                "; ",
                objects.Select(gameObject => gameObject == null
                    ? "<missing>"
                    : GetHierarchyPath(gameObject.transform)));
            return $"Expected exactly one {name} scene root; found {objects.Count}: {locations}";
        }

        private static string GetHierarchyPath(Transform transform)
        {
            List<string> names = new();
            for (Transform current = transform; current != null; current = current.parent)
                names.Add(current.name);
            names.Reverse();
            return string.Join("/", names);
        }

        private sealed class RecordingIngress : IGameplayEventIngress
        {
            public readonly List<IGameplayEvent> Events = new();
            public void Push(IGameplayEvent @event) => Events.Add(@event);
        }

        private sealed class MulticastEventTransport : IGameplayEventTransport
        {
            private readonly (GameplayEventReplicator Replicator, IGameplayEventIngress Ingress)[] clients;

            public MulticastEventTransport(
                params (GameplayEventReplicator Replicator, IGameplayEventIngress Ingress)[] clients)
            {
                this.clients = clients ?? Array.Empty<(GameplayEventReplicator, IGameplayEventIngress)>();
            }

            public void Send(GameplayEventEnvelope envelope)
            {
                for (int i = 0; i < clients.Length; i++)
                    clients[i].Replicator.Receive(envelope, clients[i].Ingress);
            }
        }
    }
}
