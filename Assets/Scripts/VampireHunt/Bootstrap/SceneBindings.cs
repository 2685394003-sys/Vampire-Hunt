using System;
using System.Collections.Generic;
using UnityEngine;

namespace VampireHunt.Bootstrap
{
    /// <summary>
    /// Explicit scene-level references consumed by the composition root. This
    /// component never performs a scene-wide lookup.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SceneBindings : MonoBehaviour
    {
        [SerializeField] private Transform gameplayRoot;
        [SerializeField] private Camera mainCamera;
        [SerializeField] private MonoBehaviour[] adapterBindings = Array.Empty<MonoBehaviour>();

        public Transform GameplayRoot => gameplayRoot;
        public Camera MainCamera => mainCamera;
        public IReadOnlyList<MonoBehaviour> AdapterBindings => adapterBindings;

        public void Bind(Transform gameplayRoot, Camera mainCamera)
        {
            this.gameplayRoot = gameplayRoot;
            this.mainCamera = mainCamera;
        }

        public void BindAdapters(params MonoBehaviour[] bindings)
        {
            adapterBindings = bindings == null
                ? Array.Empty<MonoBehaviour>()
                : (MonoBehaviour[])bindings.Clone();
        }

        public IEnumerable<T> EnumerateBindings<T>() where T : class
        {
            MonoBehaviour[] snapshot = adapterBindings ?? Array.Empty<MonoBehaviour>();
            for (int i = 0; i < snapshot.Length; i++)
            {
                if (snapshot[i] is T binding) yield return binding;
            }
        }

        public void Validate(bool requireCamera = true)
        {
            if (gameplayRoot == null)
                throw new InvalidOperationException("SceneBindings requires a GameplayRoot reference.");
            if (requireCamera && mainCamera == null)
                throw new InvalidOperationException("SceneBindings requires a MainCamera reference for client runtime.");

            if (adapterBindings == null)
                throw new InvalidOperationException("SceneBindings adapter collection must be initialized.");
        }
    }
}
