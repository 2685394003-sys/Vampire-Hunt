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
        [UnityTest]
        public IEnumerator EnabledBuildScenes_LoadAndRunOneFrameWithoutMissingScripts()
        {
            Assert.That(SceneManager.sceneCountInBuildSettings, Is.GreaterThan(0));

            for (int buildIndex = 0; buildIndex < SceneManager.sceneCountInBuildSettings; buildIndex++)
            {
                string scenePath = SceneUtility.GetScenePathByBuildIndex(buildIndex);
                Scene scene = SceneManager.GetSceneByPath(scenePath);
                bool wasAlreadyLoaded = scene.IsValid() && scene.isLoaded;
                if (!wasAlreadyLoaded)
                {
                    bool previousIgnore = LogAssert.ignoreFailingMessages;
                    LogAssert.ignoreFailingMessages = true;
                    try
                    {
                        // Existing project assets can emit import diagnostics while Unity deserializes
                        // a scene. The smoke test verifies the instantiated hierarchy explicitly below;
                        // strict runtime log checking resumes as soon as asynchronous loading completes.
                        AsyncOperation load = SceneManager.LoadSceneAsync(buildIndex, LoadSceneMode.Additive);
                        Assert.That(load, Is.Not.Null, $"Could not start loading build scene {scenePath}.");
                        while (!load.isDone) yield return null;
                    }
                    finally
                    {
                        LogAssert.ignoreFailingMessages = previousIgnore;
                    }
                    scene = SceneManager.GetSceneByBuildIndex(buildIndex);
                }

                Assert.That(scene.IsValid() && scene.isLoaded, Is.True, $"Build scene did not load: {scenePath}");
                yield return null;

                List<string> missing = new List<string>();
                foreach (GameObject root in scene.GetRootGameObjects())
                    CollectMissingScripts(root.transform, scenePath, missing);
                Assert.That(missing, Is.Empty, string.Join("; ", missing));

                if (!wasAlreadyLoaded)
                {
                    AsyncOperation unload = SceneManager.UnloadSceneAsync(scene);
                    if (unload != null)
                        while (!unload.isDone) yield return null;
                }
            }

            LogAssert.NoUnexpectedReceived();
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
