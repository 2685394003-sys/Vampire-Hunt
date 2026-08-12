using UnityEditor;
using UnityEngine;

public class ConvertMaterialsToURP : EditorWindow
{
    [MenuItem("Tools/转换所有材质到URP")]
    public static void ConvertAll()
    {
        string[] shaderNames = {
            "Autodesk Interactive",
            "Autodesk/Interactive",
            "Standard",
            "Standard (Specular setup)"
        };

        Shader urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit == null)
        {
            Debug.LogError("找不到 URP/Lit 着色器，请确认 URP 已安装。");
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:Material");
        int converted = 0;
        int skipped = 0;

        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null) continue;

                bool needConvert = false;
                foreach (string sn in shaderNames)
                {
                    if (mat.shader != null && mat.shader.name == sn)
                    {
                        needConvert = true;
                        break;
                    }
                }

                if (!needConvert)
                {
                    skipped++;
                    continue;
                }

                EditorUtility.DisplayProgressBar("转换材质", path, (float)i / guids.Length);

                // 保存旧属性
                Texture mainTex = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
                Texture baseMap = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap") : null;
                Color color = mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.white;
                Color baseColor = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : Color.white;
                Texture bumpMap = mat.HasProperty("_BumpMap") ? mat.GetTexture("_BumpMap") : null;
                float bumpScale = mat.HasProperty("_BumpScale") ? mat.GetFloat("_BumpScale") : 1f;
                Texture metallicGlossMap = mat.HasProperty("_MetallicGlossMap") ? mat.GetTexture("_MetallicGlossMap") : null;
                float metallic = mat.HasProperty("_Metallic") ? mat.GetFloat("_Metallic") : 0f;
                float glossiness = mat.HasProperty("_Glossiness") ? mat.GetFloat("_Glossiness") : 0.5f;
                Texture emissionMap = mat.HasProperty("_EmissionMap") ? mat.GetTexture("_EmissionMap") : null;
                Color emissionColor = mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor") : Color.black;
                float cutoff = mat.HasProperty("_Cutoff") ? mat.GetFloat("_Cutoff") : 0.5f;

                // 切换着色器
                mat.shader = urpLit;

                // 恢复属性
                Texture albedo = baseMap != null ? baseMap : mainTex;
                if (albedo != null) mat.SetTexture("_BaseMap", albedo);
                Color finalColor = (baseMap != null) ? baseColor : color;
                mat.SetColor("_BaseColor", finalColor);

                if (bumpMap != null)
                {
                    mat.SetTexture("_BumpMap", bumpMap);
                    mat.SetFloat("_BumpScale", bumpScale);
                    mat.EnableKeyword("_NORMALMAP");
                }

                if (metallicGlossMap != null)
                {
                    mat.SetTexture("_MetallicGlossMap", metallicGlossMap);
                }
                mat.SetFloat("_Metallic", metallic);
                mat.SetFloat("_Smoothness", glossiness);
                mat.SetFloat("_Glossiness", glossiness);

                if (emissionMap != null || emissionColor.maxColorComponent > 0f)
                {
                    if (emissionMap != null) mat.SetTexture("_EmissionMap", emissionMap);
                    mat.SetColor("_EmissionColor", emissionColor);
                    mat.EnableKeyword("_EMISSION");
                    mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }

                // 透明裁剪
                if (mat.HasProperty("_Mode") && mat.GetFloat("_Mode") == 1f)
                {
                    mat.SetFloat("_AlphaClip", 1f);
                    mat.SetFloat("_Cutoff", cutoff);
                    mat.EnableKeyword("_ALPHATEST_ON");
                }

                EditorUtility.SetDirty(mat);
                converted++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"材质转换完成！成功转换 {converted} 个，跳过 {skipped} 个（已是URP着色器）。");
    }
}
