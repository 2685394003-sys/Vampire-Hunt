using UnityEngine;
using Unity.Cinemachine;

namespace Blocks.Gameplay.Core
{
    public class CoreDirector : MonoBehaviour
    {
        private static CoreDirector s_Instance;
        public static CoreDirector GetInstance() => s_Instance;

        [Header("Camera Shake Settings")]
        [Tooltip("The Cinemachine Impulse Source component for camera shake effects.")]
        [SerializeField] private CinemachineImpulseSource impulseSource;

        // 声音系统已弃用（2026-08-29）：项目声音统一走 Wwise。
        // CoreDirector.RequestAudio 现为静默空操作，游戏代码中的调用点由 haohao 后续接 Wwise PostEvent。
        // 相关旧字段（unityAudioMixer/soundGameObjectPrefab/maxSoundGameObjects/maxSoundEmitters）与 SoundSystem 类保留未删，
        // 仅在本类不再初始化。

        private void Awake()
        {
            if (s_Instance != null && s_Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            s_Instance = this;
            DontDestroyOnLoad(gameObject);

            if (impulseSource == null)
            {
                impulseSource = GetComponent<CinemachineImpulseSource>();
                if (impulseSource == null)
                {
                    Debug.LogWarning("[CoreDirector] No CinemachineImpulseSource found. Camera shake will not work. Add a CinemachineImpulseSource component to the CoreDirector prefab.");
                }
            }
        }

        /// <summary>
        /// Begins a new sound request using a fluent builder pattern.
        /// </summary>
        /// <param name="soundDef">The SoundDef to play.</param>
        /// <returns>A SoundRequestBuilder to configure and play the sound.</returns>
        /// <remarks>
        /// 声音系统已弃用（2026-08-29，声音统一走 Wwise）。
        /// 本方法始终返回空操作的 builder，调用点不会报错也不会发声；
        /// 各调用点后续由 haohao 接成 Wwise PostEvent。
        /// </remarks>
        public static SoundRequestBuilder RequestAudio(SoundDef soundDef)
        {
            return new SoundRequestBuilder(null, null);
        }

        /// <summary>
        /// Begins a new camera shake request using a fluent builder pattern.
        /// </summary>
        /// <returns>A CameraShakeBuilder to configure and execute the camera shake.</returns>
        public static CameraShakeBuilder RequestCameraShake()
        {
            var instance = GetInstance();
            if (instance == null || instance.impulseSource == null)
            {
                Debug.LogError("[CoreDirector] ImpulseSource is not initialized. Ensure CoreDirector exists and has a CinemachineImpulseSource component.");
                // Return a builder with null source - it will safely do nothing
                return new CameraShakeBuilder(null);
            }
            return new CameraShakeBuilder(instance.impulseSource);
        }

        /// <summary>
        /// Creates a visual effect builder using a prefab GameObject.
        /// </summary>
        public static EffectBuilder CreatePrefabEffect(GameObject prefab)
        {
            return new EffectBuilder(prefab);
        }

        /// <summary>
        /// Creates a tracer effect between two points using a cylinder primitive.
        /// </summary>
        public static EffectBuilder CreateTracer(Vector3 startPosition, Vector3 endPosition)
        {
            return new EffectBuilder(PrimitiveType.Cylinder)
                .WithTracerPositioning(startPosition, endPosition)
                .WithName("Tracer")
                .WithDuration(0.1f);
        }

        /// <summary>
        /// Creates an impact marker at a position using a sphere primitive.
        /// </summary>
        public static EffectBuilder CreateImpactMarker(Vector3 position)
        {
            return new EffectBuilder(PrimitiveType.Sphere)
                .WithPosition(position)
                .WithScale(Vector3.one * 0.1f)
                .WithName("ImpactMarker")
                .WithDuration(0.5f);
        }
    }
}
