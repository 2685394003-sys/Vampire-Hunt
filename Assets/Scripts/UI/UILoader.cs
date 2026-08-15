using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Automatically loads the shared UI scene alongside the startup scene.
/// No scene component is required: Unity creates this loader after the first
/// scene has loaded.
/// </summary>
public sealed class UILoader : MonoBehaviour
{
    private const string UISceneName = "UI";

    private static bool loaderCreated;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ResetState()
    {
        loaderCreated = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateLoader()
    {
        if (loaderCreated || Application.isBatchMode)
        {
            return;
        }

        loaderCreated = true;

        if (SceneManager.GetSceneByName(UISceneName).isLoaded)
        {
            return;
        }

        if (!Application.CanStreamedLevelBeLoaded(UISceneName))
        {
            Debug.LogError(
                $"Cannot load the '{UISceneName}' scene. Add it to the active Build Profile's Scene List.");
            return;
        }

        GameObject loaderObject = new GameObject(nameof(UILoader));
        DontDestroyOnLoad(loaderObject);
        loaderObject.AddComponent<UILoader>();
    }

    private IEnumerator Start()
    {
        // The scene may have finished loading between CreateLoader and Start.
        if (SceneManager.GetSceneByName(UISceneName).isLoaded)
        {
            Destroy(gameObject);
            yield break;
        }

        AsyncOperation loadOperation = SceneManager.LoadSceneAsync(
            UISceneName,
            LoadSceneMode.Additive);

        if (loadOperation != null)
        {
            yield return loadOperation;
        }

        Destroy(gameObject);
    }
}
