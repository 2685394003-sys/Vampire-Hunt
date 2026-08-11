using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ImportedUnityURPCloud.Editor
{
    /// <summary>
    /// Structural checks for the isolated volumetric-cloud preview.
    /// This does not alter the project's normal render-pipeline settings.
    /// </summary>
    public static class CloudPreviewValidator
    {
        private const string ScenePath =
            "Assets/Art/VFX/VolumetricCloud/New Scene.unity";
        private const string PipelinePath =
            "Assets/Art/VFX/VolumetricCloud/ImportedUnityURPCloud/RenderPipeline/UniversalRenderPipelineAsset.asset";
        private const string RendererPath =
            "Assets/Art/VFX/VolumetricCloud/ImportedUnityURPCloud/RenderPipeline/UniversalRenderPipelineAsset_Renderer.asset";
        private const string MaterialPath =
            "Assets/Art/VFX/VolumetricCloud/ImportedUnityURPCloud/Materials/Cloud_RealTime.mat";
        private const string ShapeNoisePath =
            "Assets/Art/VFX/VolumetricCloud/ImportedUnityURPCloud/Textures/Clouds Test_ShapeNoise.asset";
        private const string DetailNoisePath =
            "Assets/Art/VFX/VolumetricCloud/ImportedUnityURPCloud/Textures/Clouds Test_DetailNoise.asset";

        [MenuItem("Tools/Volumetric Cloud/Validate Preview Scene")]
        public static void ValidateFromMenu()
        {
            Validate();
        }

        public static void ValidateBatch()
        {
            try
            {
                Validate();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                throw;
            }
        }

        private static void Validate()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var pipeline = AssetDatabase.LoadAssetAtPath<RenderPipelineAsset>(PipelinePath);
            var renderer = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(RendererPath);
            var cloudMaterial = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            var shapeNoise = AssetDatabase.LoadAssetAtPath<Texture3D>(ShapeNoisePath);
            var detailNoise = AssetDatabase.LoadAssetAtPath<Texture3D>(DetailNoisePath);

            Require(scene.IsValid() && scene.isLoaded, "Preview scene could not be loaded.");
            Require(pipeline != null, "The isolated URP pipeline asset is missing.");
            Require(renderer != null, "The isolated URP renderer asset is missing.");
            Require(cloudMaterial != null, "Cloud_RealTime material is missing.");
            Require(cloudMaterial.shader != null && cloudMaterial.shader.isSupported,
                "The real-time cloud shader is missing or unsupported.");
            Require(shapeNoise != null && detailNoise != null,
                "The required 3D shape/detail noise textures are missing.");

            var missingScriptCount = 0;
            foreach (var root in scene.GetRootGameObjects())
            {
                foreach (var child in root.GetComponentsInChildren<Transform>(true))
                    missingScriptCount += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject);
            }

            Require(missingScriptCount == 0,
                $"The preview scene contains {missingScriptCount} missing script reference(s).");

            var switchers = UnityEngine.Object.FindObjectsOfType<CloudPreviewPipelineSwitcher>(true);
            Require(switchers.Length == 1, "The scene must contain exactly one cloud pipeline switcher.");
            var switcherPipeline = new SerializedObject(switchers[0])
                .FindProperty("previewPipeline").objectReferenceValue;
            Require(switcherPipeline == pipeline,
                "The scene pipeline switcher is not linked to the isolated cloud pipeline.");

            Camera activeCamera = null;
            foreach (var camera in UnityEngine.Object.FindObjectsOfType<Camera>(true))
            {
                if (camera.enabled && camera.gameObject.activeInHierarchy)
                {
                    activeCamera = camera;
                    break;
                }
            }

            Require(activeCamera != null, "No active preview camera was found.");
            Require(activeCamera.farClipPlane >= 1000000f,
                "The active camera far clipping plane is too short for the atmospheric cloud layer.");

            var rendererFeatures = new SerializedObject(renderer).FindProperty("m_RendererFeatures");
            Require(rendererFeatures != null && rendererFeatures.arraySize == 1,
                "The isolated renderer must contain exactly one renderer feature.");
            Require(rendererFeatures.GetArrayElementAtIndex(0).objectReferenceValue != null,
                "The cloud renderer feature reference is missing.");

            Debug.Log(
                $"[CloudMigrationValidation] PASS - scene={ScenePath}, " +
                $"camera={activeCamera.name}, farClip={activeCamera.farClipPlane}, " +
                $"rendererFeatures={rendererFeatures.arraySize}, missingScripts={missingScriptCount}");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException($"[CloudMigrationValidation] {message}");
        }
    }
}
