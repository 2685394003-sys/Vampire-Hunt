using UnityEngine;
using UnityEngine.Rendering;

namespace ImportedUnityURPCloud
{
    /// <summary>
    /// Uses the migrated cloud renderer only while the preview scene is playing.
    /// The project's original render pipeline is restored when Play Mode ends.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public sealed class CloudPreviewPipelineSwitcher : MonoBehaviour
    {
        [SerializeField] private RenderPipelineAsset previewPipeline;

        private RenderPipelineAsset previousDefaultPipeline;
        private RenderPipelineAsset previousQualityPipeline;
        private bool isApplied;

        private void Awake()
        {
            ApplyPreviewPipeline();
        }

        private void OnEnable()
        {
            ApplyPreviewPipeline();
        }

        private void OnDisable()
        {
            RestoreOriginalPipeline();
        }

        private void OnDestroy()
        {
            RestoreOriginalPipeline();
        }

        private void ApplyPreviewPipeline()
        {
            if (!Application.isPlaying || isApplied || previewPipeline == null)
                return;

            previousDefaultPipeline = GraphicsSettings.defaultRenderPipeline;
            previousQualityPipeline = QualitySettings.renderPipeline;

            GraphicsSettings.defaultRenderPipeline = previewPipeline;
            QualitySettings.renderPipeline = previewPipeline;
            isApplied = true;
        }

        private void RestoreOriginalPipeline()
        {
            if (!isApplied)
                return;

            GraphicsSettings.defaultRenderPipeline = previousDefaultPipeline;
            QualitySettings.renderPipeline = previousQualityPipeline;
            isApplied = false;
        }
    }
}
