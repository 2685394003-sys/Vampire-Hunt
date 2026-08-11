using System;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class CloudShapeNoiseGenerator
{
    private const int Resolution = 128;

    private const string ComputeShaderPath =
        "Assets/Art/VFX/VolumetricCloud/CloudShapeNoise.compute";

    private const string OutputTexturePath =
        "Assets/Art/VFX/VolumetricCloud/T_CloudShapeNoise1.asset";

    [MenuItem("Tools/Volumetric Cloud/Generate Shape Noise")]
    private static void Generate()
    {
        ComputeShader computeShader =
            AssetDatabase.LoadAssetAtPath<ComputeShader>(
                ComputeShaderPath
            );

        if (computeShader == null)
        {
            Debug.LogError(
                "找不到 Compute Shader："
                + ComputeShaderPath
            );

            return;
        }

        RenderTexture renderTexture = null;

        try
        {
            EditorUtility.DisplayProgressBar(
                "生成 Shape Noise",
                "正在创建 128³ RenderTexture……",
                0.05f
            );

            renderTexture = new RenderTexture(
                Resolution,
                Resolution,
                0,
                RenderTextureFormat.ARGBFloat
            );

            renderTexture.name =
                "RT_CloudShapeNoise";

            renderTexture.dimension =
                TextureDimension.Tex3D;

            renderTexture.volumeDepth =
                Resolution;

            renderTexture.enableRandomWrite =
                true;

            renderTexture.useMipMap =
                false;

            renderTexture.autoGenerateMips =
                false;

            renderTexture.wrapMode =
                TextureWrapMode.Repeat;

            renderTexture.filterMode =
                FilterMode.Bilinear;

            if (!renderTexture.Create())
            {
                Debug.LogError(
                    "Shape Noise 三维 RenderTexture 创建失败。"
                );

                return;
            }

            int kernel =
                computeShader.FindKernel(
                    "GenerateShapeNoise"
                );

            computeShader.SetTexture(
                kernel,
                "_ShapeNoise",
                renderTexture
            );

            EditorUtility.DisplayProgressBar(
                "生成 Shape Noise",
                "Compute Shader 正在生成噪声……",
                0.15f
            );

            computeShader.Dispatch(
                kernel,
                Resolution / 8,
                Resolution / 8,
                Resolution / 8
            );

            int slicePixelCount =
                Resolution * Resolution;

            int voxelCount =
                slicePixelCount * Resolution;

            Color[] pixels =
                new Color[voxelCount];

            for (int z = 0; z < Resolution; z++)
            {
                float progress =
                    0.2f +
                    0.65f *
                    ((z + 1.0f) / Resolution);

                EditorUtility.DisplayProgressBar(
                    "生成 Shape Noise",
                    $"正在读取第 {z + 1}/{Resolution} 层……",
                    progress
                );

                AsyncGPUReadbackRequest request =
                    AsyncGPUReadback.Request(
                        renderTexture,
                        0,
                        0,
                        Resolution,
                        0,
                        Resolution,
                        z,
                        1,
                        TextureFormat.RGBAFloat,
                        null
                    );

                request.WaitForCompletion();

                if (request.hasError)
                {
                    Debug.LogError(
                        $"读取 Shape Noise 第 {z} 层失败。"
                    );

                    return;
                }

                NativeArray<Vector4> sliceData =
                    request.GetData<Vector4>();

                if (sliceData.Length != slicePixelCount)
                {
                    Debug.LogError(
                        $"第 {z} 层数据数量错误。"
                        + $"实际：{sliceData.Length}，"
                        + $"预计：{slicePixelCount}。"
                    );

                    return;
                }

                int destinationStart =
                    z * slicePixelCount;

                for (int i = 0; i < slicePixelCount; i++)
                {
                    Vector4 value =
                        sliceData[i];

                    pixels[destinationStart + i] =
                        new Color(
                            value.x,
                            value.y,
                            value.z,
                            value.w
                        );
                }
            }

            float rMin = float.MaxValue;
            float rMax = float.MinValue;

            float gMin = float.MaxValue;
            float gMax = float.MinValue;

            float bMin = float.MaxValue;
            float bMax = float.MinValue;

            float aMin = float.MaxValue;
            float aMax = float.MinValue;

            for (int i = 0; i < pixels.Length; i++)
            {
                Color value = pixels[i];

                rMin = Mathf.Min(rMin, value.r);
                rMax = Mathf.Max(rMax, value.r);

                gMin = Mathf.Min(gMin, value.g);
                gMax = Mathf.Max(gMax, value.g);

                bMin = Mathf.Min(bMin, value.b);
                bMax = Mathf.Max(bMax, value.b);

                aMin = Mathf.Min(aMin, value.a);
                aMax = Mathf.Max(aMax, value.a);
            }

            Debug.Log(
                "Shape Noise 数据检查："
                + $"\nR Perlin-Worley：{rMin} ～ {rMax}"
                + $"\nG Low Worley：{gMin} ～ {gMax}"
                + $"\nB Mid Worley：{bMin} ～ {bMax}"
                + $"\nA High Worley：{aMin} ～ {aMax}"
            );

            if (
                rMax <= 0.00001f &&
                gMax <= 0.00001f &&
                bMax <= 0.00001f &&
                aMax <= 0.00001f
            )
            {
                Debug.LogError(
                    "Shape Noise 数据全黑，未创建 Texture3D。"
                );

                return;
            }

            EditorUtility.DisplayProgressBar(
                "生成 Shape Noise",
                "正在保存 Texture3D……",
                0.9f
            );

            Texture3D texture =
                new Texture3D(
                    Resolution,
                    Resolution,
                    Resolution,
                    TextureFormat.RGBAFloat,
                    false
                );

            texture.name =
                "T_CloudShapeNoise";

            texture.wrapMode =
                TextureWrapMode.Repeat;

            texture.filterMode =
                FilterMode.Bilinear;

            texture.anisoLevel = 0;

            texture.SetPixels(
                pixels
            );

            texture.Apply(
                false,
                false
            );

            Texture3D oldTexture =
                AssetDatabase.LoadAssetAtPath<Texture3D>(
                    OutputTexturePath
                );

            if (oldTexture != null)
            {
                AssetDatabase.DeleteAsset(
                    OutputTexturePath
                );
            }

            AssetDatabase.CreateAsset(
                texture,
                OutputTexturePath
            );

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject =
                texture;

            EditorGUIUtility.PingObject(
                texture
            );

            Debug.Log(
                "Shape Noise 生成完成："
                + OutputTexturePath
                + "\n分辨率："
                + Resolution
                + " × "
                + Resolution
                + " × "
                + Resolution
            );
        }
        catch (Exception exception)
        {
            Debug.LogException(
                exception
            );
        }
        finally
        {
            if (renderTexture != null)
            {
                renderTexture.Release();

                UnityEngine.Object.DestroyImmediate(
                    renderTexture
                );
            }

            EditorUtility.ClearProgressBar();
        }
    }
}