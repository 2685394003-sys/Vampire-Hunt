using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// 通用数据表 ScriptableObject 基类。
/// 任何新数据表只需继承本类并指定数据类型，自动获得列表存储和按 ID 查找。
/// ID 字段约定：名为 id 或 以 Id 结尾的字段（如 playerId、skillId、itemId）。
/// </summary>
public abstract class CSVConfigSO<TData> : ScriptableObject
    where TData : class, new()
{
    [SerializeField] protected List<TData> items = new List<TData>();

    /// <summary>所有数据的只读列表。</summary>
    public IReadOnlyList<TData> Items => items;

    private static FieldInfo cachedIdField;

    /// <summary>
    /// 按 ID 查找数据。ID 字段通过反射自动识别（id / xxxId）。
    /// </summary>
    public TData GetById(string id)
    {
        if (string.IsNullOrEmpty(id) || items.Count == 0) return null;

        if (cachedIdField == null)
        {
            cachedIdField = FindIdField(typeof(TData));
        }

        if (cachedIdField == null)
        {
            Debug.LogWarning($"[{typeof(TData).Name}] 未找到 ID 字段（id 或 xxxId），GetById 不可用。");
            return null;
        }

        foreach (TData item in items)
        {
            if (item == null) continue;
            object val = cachedIdField.GetValue(item);
            if (val != null && val.ToString() == id) return item;
        }
        return null;
    }

    /// <summary>导入工具内部使用：清空并替换全部数据。</summary>
    public void SetAll(List<TData> data)
    {
        items.Clear();
        if (data != null) items.AddRange(data);
    }

    private static FieldInfo FindIdField(System.Type type)
    {
        FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);
        // 优先精确匹配 "id"
        foreach (FieldInfo f in fields)
        {
            if (f.Name.ToLower() == "id") return f;
        }
        // 其次匹配以 Id 结尾
        foreach (FieldInfo f in fields)
        {
            if (f.Name.EndsWith("Id")) return f;
        }
        return null;
    }
}
