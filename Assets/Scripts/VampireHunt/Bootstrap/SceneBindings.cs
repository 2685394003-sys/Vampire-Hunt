using System;
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

        public Transform GameplayRoot => gameplayRoot;
        public Camera MainCamera => mainCamera;

        public void Bind(Transform gameplayRoot, Camera mainCamera)
        {
            this.gameplayRoot = gameplayRoot;
            this.mainCamera = mainCamera;
        }

        public void Validate(bool requireCamera = true)
        {
            if (gameplayRoot == null)
                throw new InvalidOperationException("SceneBindings requires a GameplayRoot reference.");
            if (requireCamera && mainCamera == null)
                throw new InvalidOperationException("SceneBindings requires a MainCamera reference for client runtime.");
        }
    }
}
