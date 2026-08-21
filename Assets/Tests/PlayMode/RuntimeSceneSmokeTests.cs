using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace VampireHunt.Tests.PlayMode
{
    public sealed class RuntimeSceneSmokeTests
    {
        // MonsterSpawnConfig.FirstSpawnDelay is currently 3 seconds. Keep a
        // small bounded margin so Start, legacy pool prewarm, and the first
        // delayed spawn/update all execute without turning this into a long
        // wall-clock wait.
        private const float PostStartValidationWindowSeconds = 4f;
        private const int MinimumPostStartFrames = 30;

        [UnityTest]
        public IEnumerator EnabledBuildScenes_LoadAndRunBoundedWindowWithoutMissingScripts()
        {
            Assert.That(SceneManager.sceneCountInBuildSettings, Is.GreaterThan(0));

            for (int buildIndex = 0; buildIndex < SceneManager.sceneCountInBuildSettings; buildIndex++)
            {
                string scenePath = SceneUtility.GetScenePathByBuildIndex(buildIndex);
                Scene scene = SceneManager.GetSceneByPath(scenePath);
                bool wasAlreadyLoaded = scene.IsValid() && scene.isLoaded;
                if (!wasAlreadyLoaded)
                {
                    // Do not toggle LogAssert.ignoreFailingMessages here. The property emits its own
                    // "IgnoreFailingMessages:true/false" log entries and, more importantly, would hide
                    // genuine startup exceptions. Unity Test Runner checks failing Error/Exception logs
                    // on every yielded frame while the hierarchy scan below checks Missing Script directly.
                    AsyncOperation load = SceneManager.LoadSceneAsync(buildIndex, LoadSceneMode.Additive);
                    Assert.That(load, Is.Not.Null, $"Could not start loading build scene {scenePath}.");
                    while (!load.isDone) yield return null;
                    scene = SceneManager.GetSceneByBuildIndex(buildIndex);
                }

                Assert.That(scene.IsValid() && scene.isLoaded, Is.True, $"Build scene did not load: {scenePath}");

                List<string> missing = new List<string>();
                List<string> networkTopologyViolations = new List<string>();
                float validationStart = Time.realtimeSinceStartup;
                int validationFrames = 0;
                do
                {
                    // One yielded frame lets Start/Awake complete. The bounded
                    // realtime window then reaches legacy pool prewarm and the
                    // first delayed update without using an arbitrary sleep.
                    yield return null;
                    validationFrames++;

                    foreach (GameObject root in scene.GetRootGameObjects())
                        CollectNetworkTopologyViolations(root, scenePath, networkTopologyViolations);
                }
                while (validationFrames < MinimumPostStartFrames ||
                       Time.realtimeSinceStartup - validationStart < PostStartValidationWindowSeconds);

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    CollectMissingScripts(root.transform, scenePath, missing);
                    CollectNetworkTopologyViolations(root, scenePath, networkTopologyViolations);
                }

                Assert.That(
                    missing,
                    Is.Empty,
                    "Build scene contains Missing Script components: " + string.Join("; ", missing));
                Assert.That(
                    networkTopologyViolations,
                    Is.Empty,
                    "Runtime NetworkManager roots and their descendants must not contain NetworkObject or " +
                    "NetworkBehaviour components: " + string.Join("; ", networkTopologyViolations));

                if (!wasAlreadyLoaded)
                {
                    AsyncOperation unload = SceneManager.UnloadSceneAsync(scene);
                    if (unload != null)
                        while (!unload.isDone) yield return null;
                }
            }

            // Warnings from existing authored assets are intentionally not treated as startup failures.
            // Error/Exception logs remain fatal through Unity Test Runner's per-frame LogScope, and
            // Missing Script components are asserted explicitly above.
        }

        private static void CollectMissingScripts(
            Transform transform,
            string scenePath,
            ICollection<string> missing)
        {
            MonoBehaviour[] behaviours = transform.GetComponents<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] == null)
                    missing.Add($"{scenePath}/{GetHierarchyPath(transform)} has a Missing Script component");
            }

            for (int i = 0; i < transform.childCount; i++)
                CollectMissingScripts(transform.GetChild(i), scenePath, missing);
        }

        private static void CollectNetworkTopologyViolations(
            GameObject root,
            string scenePath,
            ICollection<string> violations)
        {
            Component[] components = root.GetComponentsInChildren<Component>(true);
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component == null || !IsNetcodeType(component, "Unity.Netcode.NetworkManager"))
                    continue;

                Component[] descendants = component.GetComponentsInChildren<Component>(true);
                for (int j = 0; j < descendants.Length; j++)
                {
                    Component descendant = descendants[j];
                    if (descendant == null)
                        continue;

                    if (IsNetcodeType(descendant, "Unity.Netcode.NetworkObject") ||
                        IsNetcodeType(descendant, "Unity.Netcode.NetworkBehaviour"))
                    {
                        string violation =
                            $"{scenePath}/{GetHierarchyPath(descendant.transform)} contains " +
                            $"{descendant.GetType().FullName} below NetworkManager.";
                        if (!violations.Contains(violation))
                            violations.Add(violation);
                    }
                }
            }
        }

        private static bool IsNetcodeType(Component component, string baseTypeFullName)
        {
            for (Type current = component.GetType(); current != null; current = current.BaseType)
            {
                if (string.Equals(current.FullName, baseTypeFullName, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static string GetHierarchyPath(Transform transform)
        {
            List<string> names = new List<string>();
            for (Transform current = transform; current != null; current = current.parent)
                names.Add(current.name);
            names.Reverse();
            return string.Join("/", names);
        }
    }
}
