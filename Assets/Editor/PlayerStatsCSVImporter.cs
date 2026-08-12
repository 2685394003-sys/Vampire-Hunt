using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 玩家数据表 CSV 导入工具。
/// 适配格式：第1行备注、第2行字段名、第3行类型、第4行中文说明、第5行起为数据。
/// 菜单：Tools / 导入玩家数据表CSV
/// </summary>
public static class PlayerStatsCSVImporter
{
    private const string OutputPath = "Assets/Config/PlayerStatsConfig.asset";
    private const int HeaderRowCount = 4; // 备注 + 字段名 + 类型 + 中文说明

    [MenuItem("Tools/导入玩家数据表CSV")]
    public static void ImportCSV()
    {
        string csvPath = EditorUtility.OpenFilePanel(
            "选择玩家数据表 CSV",
            "",
            "csv");

        if (string.IsNullOrEmpty(csvPath)) return;

        if (!File.Exists(csvPath))
        {
            EditorUtility.DisplayDialog("导入失败", "文件不存在。", "确定");
            return;
        }

        try
        {
            List<PlayerStatsData> data = ParseCSV(csvPath);
            if (data == null || data.Count == 0)
            {
                EditorUtility.DisplayDialog("导入失败", "CSV 中没有有效数据行。", "确定");
                return;
            }

            SaveToScriptableObject(data);
            EditorUtility.DisplayDialog(
                "导入成功",
                $"成功导入 {data.Count} 条玩家数据。\n输出路径：{OutputPath}",
                "确定");
        }
        catch (System.Exception e)
        {
            EditorUtility.DisplayDialog("导入失败", e.Message, "确定");
            Debug.LogError($"[PlayerStatsCSVImporter] {e}");
        }
    }

    private static List<PlayerStatsData> ParseCSV(string csvPath)
    {
        // 用 UTF-8 读取，自动处理 BOM
        string[] lines = File.ReadAllLines(csvPath, Encoding.UTF8);
        if (lines.Length < HeaderRowCount + 1)
        {
            throw new System.Exception($"CSV 行数不足，至少需要 {HeaderRowCount + 1} 行（含表头）。");
        }

        // 第2行（索引1）= 字段名
        string[] fieldNames = SplitCSVLine(lines[1]);
        // 第3行（索引2）= 类型
        string[] fieldTypes = SplitCSVLine(lines[2]);

        if (fieldNames.Length != fieldTypes.Length)
        {
            throw new System.Exception("字段名行与类型行列数不一致。");
        }

        // 建立字段名 → 反射 FieldInfo 的映射
        var fieldMap = new Dictionary<string, FieldInfo>();
        FieldInfo[] allFields = typeof(PlayerStatsData).GetFields(
            BindingFlags.Public | BindingFlags.Instance);
        foreach (FieldInfo f in allFields)
        {
            fieldMap[f.Name] = f;
        }

        var result = new List<PlayerStatsData>();

        // 第5行起（索引4）= 数据
        for (int i = HeaderRowCount; i < lines.Length; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            string[] cells = SplitCSVLine(line);
            if (cells.Length == 0 || string.IsNullOrWhiteSpace(cells[0])) continue;

            var entry = new PlayerStatsData();
            bool hasAnyValue = false;

            for (int col = 0; col < fieldNames.Length && col < cells.Length; col++)
            {
                string name = fieldNames[col].Trim();
                string value = cells[col].Trim();

                if (string.IsNullOrEmpty(name)) continue;
                if (!fieldMap.TryGetValue(name, out FieldInfo field))
                {
                    Debug.LogWarning($"[PlayerStatsCSVImporter] 字段 '{name}' 在 PlayerStatsData 中不存在，已跳过。");
                    continue;
                }

                if (string.IsNullOrEmpty(value)) continue;
                hasAnyValue = true;

                AssignField(field, entry, value, fieldTypes[col].Trim());
            }

            if (hasAnyValue && !string.IsNullOrEmpty(entry.playerId))
            {
                result.Add(entry);
            }
        }

        return result;
    }

    private static void AssignField(FieldInfo field, object target, string value, string typeHint)
    {
        try
        {
            if (field.FieldType == typeof(string))
            {
                field.SetValue(target, value);
            }
            else if (field.FieldType == typeof(float) || field.FieldType == typeof(double))
            {
                if (float.TryParse(value,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out float f))
                {
                    field.SetValue(target, f);
                }
            }
            else if (field.FieldType == typeof(int))
            {
                if (int.TryParse(value, out int iv)) field.SetValue(target, iv);
            }
            else if (field.FieldType == typeof(bool))
            {
                field.SetValue(target, value == "1" || value.ToLower() == "true");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[PlayerStatsCSVImporter] 字段 '{field.Name}' 赋值失败：{e.Message}");
        }
    }

    private static void SaveToScriptableObject(List<PlayerStatsData> data)
    {
        string dir = Path.GetDirectoryName(OutputPath);
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
            AssetDatabase.Refresh();
        }

        var config = AssetDatabase.LoadAssetAtPath<PlayerStatsConfig_SO>(OutputPath);
        if (config == null)
        {
            config = ScriptableObject.CreateInstance<PlayerStatsConfig_SO>();
            AssetDatabase.CreateAsset(config, OutputPath);
        }

        config.SetAll(data);
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // 在 Project 窗口中选中生成的资产
        Selection.activeObject = config;
        EditorGUIUtility.PingObject(config);
    }

    /// <summary>
    /// 简单 CSV 行分割，支持双引号包裹的含逗号字段。
    /// </summary>
    private static string[] SplitCSVLine(string line)
    {
        if (string.IsNullOrEmpty(line)) return new string[0];

        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(sb.ToString());
                sb.Clear();
            }
            else
            {
                sb.Append(c);
            }
        }
        result.Add(sb.ToString());
        return result.ToArray();
    }
}
