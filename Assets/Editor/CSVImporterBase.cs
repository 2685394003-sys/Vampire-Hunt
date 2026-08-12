using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 通用 CSV 导入器基类。
/// 适配格式：第1行备注、第2行字段名、第3行类型、第4行中文说明、第5行起为数据。
/// 新数据表只需写一个子类，指定数据类型、SO 类型、输出路径和菜单名即可。
/// </summary>
public abstract class CSVImporterBase<TData, TSO>
    where TData : class, new()
    where TSO : CSVConfigSO<TData>
{
    /// <summary>生成的 SO 资产输出路径，子类必须实现。</summary>
    protected abstract string OutputPath { get; }

    /// <summary>导入完成后的提示名称，如 "玩家数据"。</summary>
    protected abstract string DataLabel { get; }

    /// <summary>
    /// 子类在 MenuItem 方法中调用此方法触发导入。
    /// </summary>
    protected void RunImport()
    {
        string csvPath = EditorUtility.OpenFilePanel($"选择{DataLabel}CSV", "", "csv");
        if (string.IsNullOrEmpty(csvPath)) return;
        if (!File.Exists(csvPath))
        {
            EditorUtility.DisplayDialog("导入失败", "文件不存在。", "确定");
            return;
        }

        try
        {
            List<TData> data = ParseCSV(csvPath);
            if (data == null || data.Count == 0)
            {
                EditorUtility.DisplayDialog("导入失败", "CSV 中没有有效数据行。", "确定");
                return;
            }

            SaveToSO(data);
            EditorUtility.DisplayDialog(
                "导入成功",
                $"成功导入 {data.Count} 条{DataLabel}。\n输出：{OutputPath}",
                "确定");
        }
        catch (System.Exception e)
        {
            EditorUtility.DisplayDialog("导入失败", e.Message, "确定");
            Debug.LogError($"[CSVImporter:{typeof(TData).Name}] {e}");
        }
    }

    private List<TData> ParseCSV(string csvPath)
    {
        string[] lines = File.ReadAllLines(csvPath, Encoding.UTF8);
        const int headerRows = 4;
        if (lines.Length < headerRows + 1)
        {
            throw new System.Exception($"CSV 行数不足，至少需要 {headerRows + 1} 行。");
        }

        string[] fieldNames = SplitLine(lines[1]); // 第2行：字段名
        string[] fieldTypes = SplitLine(lines[2]); // 第3行：类型

        // 建立 字段名 → FieldInfo 映射
        var fieldMap = new Dictionary<string, FieldInfo>();
        foreach (FieldInfo f in typeof(TData).GetFields(
            BindingFlags.Public | BindingFlags.Instance))
        {
            fieldMap[f.Name] = f;
        }

        var result = new List<TData>();
        for (int i = headerRows; i < lines.Length; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            string[] cells = SplitLine(line);
            if (cells.Length == 0 || string.IsNullOrWhiteSpace(cells[0])) continue;

            var entry = new TData();
            bool hasValue = false;
            string idValue = null;

            for (int col = 0; col < fieldNames.Length && col < cells.Length; col++)
            {
                string name = fieldNames[col].Trim();
                string value = cells[col].Trim();
                if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(value)) continue;

                if (!fieldMap.TryGetValue(name, out FieldInfo field))
                {
                    Debug.LogWarning($"[CSVImporter] 字段 '{name}' 在 {typeof(TData).Name} 中不存在，已跳过。");
                    continue;
                }

                AssignValue(field, entry, value);
                hasValue = true;
                if (col == 0) idValue = value;
            }

            if (hasValue && !string.IsNullOrEmpty(idValue))
            {
                result.Add(entry);
            }
        }
        return result;
    }

    private static void AssignValue(FieldInfo field, object target, string value)
    {
        try
        {
            System.Type t = field.FieldType;

            if (t == typeof(string))
            {
                field.SetValue(target, value);
            }
            else if (t == typeof(float) || t == typeof(double))
            {
                if (float.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float f))
                    field.SetValue(target, f);
            }
            else if (t == typeof(int))
            {
                if (int.TryParse(value, out int iv)) field.SetValue(target, iv);
            }
            else if (t == typeof(bool))
            {
                field.SetValue(target, value == "1" || value.ToLower() == "true");
            }
            else if (t.IsEnum)
            {
                field.SetValue(target, System.Enum.Parse(t, value, true));
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[CSVImporter] 字段 '{field.Name}' 赋值失败：{e.Message}");
        }
    }

    private void SaveToSO(List<TData> data)
    {
        string dir = Path.GetDirectoryName(OutputPath);
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
            AssetDatabase.Refresh();
        }

        var config = AssetDatabase.LoadAssetAtPath<TSO>(OutputPath);
        if (config == null)
        {
            config = ScriptableObject.CreateInstance<TSO>();
            AssetDatabase.CreateAsset(config, OutputPath);
        }

        config.SetAll(data);
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeObject = config;
        EditorGUIUtility.PingObject(config);
    }

    /// <summary>支持双引号包裹逗号的 CSV 行分割。</summary>
    private static string[] SplitLine(string line)
    {
        if (string.IsNullOrEmpty(line)) return new string[0];
        var result = new List<string>();
        var sb = new StringBuilder();
        bool inQuotes = false;
        foreach (char c in line)
        {
            if (c == '"') inQuotes = !inQuotes;
            else if (c == ',' && !inQuotes) { result.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        result.Add(sb.ToString());
        return result.ToArray();
    }
}
