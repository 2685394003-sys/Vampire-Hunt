using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// 地板贴合工具(Floor Snap)。
/// 作用:把地图(M 物体)下、地板(Cube diban)上方的所有物体贴合到地板顶面(只调 Y、保持 XZ),
/// 防止角色穿模、物体浮空或半埋。
/// 含三部分:
///  1) FloorSnapCore  —— 核心算法(纯 UnityEngine,无编辑器依赖)
///  2) MapFloorSnapper —— 挂在 Map 上的组件,编辑模式下自动贴合"之后新增"的物体
///  3) MapFloorSnapMenu —— 菜单 Tools/地图/贴合地板,一键贴合全部
/// </summary>

// ============ 1) 核心算法 ============
public static class FloorSnapCore
{
    [Serializable]
    public class Options
    {
        public string floorNameContains = "diban";               // 地板名字包含此串
        public string mapRootName = "Map";                       // 地图根物体名
        public LayerMask excludeLayers = 0;                      // 排除的层(如角色层)
        public string[] excludeNamesContains = new string[0];    // 名字含这些串则跳过
    }

    // 按名字(含子串,忽略大小写)找地板
    public static Transform FindFloor(Options opt)
    {
        foreach (var t in GameObject.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (!string.IsNullOrEmpty(opt.floorNameContains) &&
                t.name.IndexOf(opt.floorNameContains, StringComparison.OrdinalIgnoreCase) >= 0)
                return t;
        }
        return null;
    }

    // 按名字找地图根物体
    public static Transform FindMapRoot(Options opt)
    {
        foreach (var t in GameObject.FindObjectsByType<Transform>(FindObjectsSortMode.None))
        {
            if (t.name == opt.mapRootName) return t;
        }
        return null;
    }

    // 地板顶面世界 Y(优先 Renderer,其次 Collider,最后用缩放估算)
    public static float? GetFloorTopY(Transform floor)
    {
        if (floor == null) return null;
        var r = floor.GetComponent<Renderer>();
        if (r != null) return r.bounds.max.y;
        var c = floor.GetComponent<Collider>();
        if (c != null) return c.bounds.max.y;
        return floor.position.y + 0.5f * floor.lossyScale.y;
    }

    // 物体几何底部世界 Y(合并所有子 Renderer/Collider 包围盒)
    public static float? GetBottomY(Transform t)
    {
        var renderers = t.GetComponentsInChildren<Renderer>();
        if (renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) b.Encapsulate(renderers[i].bounds);
            return b.min.y;
        }
        var colliders = t.GetComponentsInChildren<Collider>();
        if (colliders.Length > 0)
        {
            Bounds b = colliders[0].bounds;
            for (int i = 1; i < colliders.Length; i++) b.Encapsulate(colliders[i].bounds);
            return b.min.y;
        }
        return null; // 无几何的空物体(如生成点)
    }

    public static bool ShouldExclude(Transform t, Options opt)
    {
        if ((opt.excludeLayers.value & (1 << t.gameObject.layer)) != 0) return true;
        foreach (var n in opt.excludeNamesContains)
        {
            if (!string.IsNullOrEmpty(n) && t.name.IndexOf(n, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    // 贴合单个物体:底面贴到地板顶面,仅改 Y。返回是否发生移动
    public static bool SnapOne(Transform t, float floorTopY, Options opt)
    {
        if (t == null) return false;
        if (ShouldExclude(t, opt)) return false;
        float? bottom = GetBottomY(t);
        if (bottom == null) return false; // 无几何物体跳过
        float delta = floorTopY - bottom.Value;
        if (Mathf.Abs(delta) < 1e-4f) return false;
        t.position = new Vector3(t.position.x, t.position.y + delta, t.position.z);
        return true;
    }

    // 贴合 root 下所有子物体(不含 root 与地板本身)。返回移动数量
    public static int SnapAll(Transform root, Options opt)
    {
        Transform floor = FindFloor(opt);
        float? top = GetFloorTopY(floor);
        if (top == null)
        {
            Debug.LogWarning("[地板贴合] 找不到地板(名称含 " + opt.floorNameContains + "),已跳过。");
            return 0;
        }
        float floorTopY = top.Value;
        if (root == null) root = FindMapRoot(opt);
        if (root == null)
        {
            Debug.LogWarning("[地板贴合] 找不到地图根物体(名称 " + opt.mapRootName + ")。");
            return 0;
        }
        int changed = 0;
        foreach (var c in root.GetComponentsInChildren<Transform>(true))
        {
            if (c == root) continue;
            if (c == floor) continue;
            if (SnapOne(c, floorTopY, opt)) changed++;
        }
        return changed;
    }
}

// ============ 2) 自动贴合组件(挂 Map 上) ============
[DisallowMultipleComponent]
public class MapFloorSnapper : MonoBehaviour
{
    [Header("地板贴合 (Floor Snap)")]
    [Tooltip("地板名字包含此字符串(默认 diban),用于定位地板")]
    public string floorNameContains = "diban";
    [Tooltip("新添加到 Map 的子物体是否自动贴合(编辑模式下)")]
    public bool autoSnapNew = true;
    [Tooltip("排除的 Layer(层);例如角色层不应被贴合")]
    public LayerMask excludeLayers = 0;
    [Tooltip("名字包含这些字符串的物体不贴合(可填 Player/Boss/shengcheng 等)")]
    public string[] excludeNamesContains = new string[0];

    [Header("状态 (只读)")]
    [Tooltip("已处理过的物体 instanceID(自动维护,勿手改)")]
    [SerializeField] private List<int> processed = new List<int>();

    private FloorSnapCore.Options BuildOptions()
    {
        return new FloorSnapCore.Options
        {
            floorNameContains = floorNameContains,
            mapRootName = gameObject.name,
            excludeLayers = excludeLayers,
            excludeNamesContains = excludeNamesContains,
        };
    }

    [ContextMenu("立即全部贴合地板 Snap All Now")]
    public void SnapAllNow()
    {
        int n = FloorSnapCore.SnapAll(transform, BuildOptions());
        Debug.Log("[地板贴合] 已贴合 " + n + " 个物体到地板。", this);
#if UNITY_EDITOR
        MarkAllProcessed();
        EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
    }

    private void MarkAllProcessed()
    {
        processed.Clear();
        foreach (var c in GetComponentsInChildren<Transform>(true))
            if (c != transform) processed.Add(c.GetHashCode());
    }

#if UNITY_EDITOR
    private void OnEnable()
    {
        // 现有物体标记为已处理(不改动),只认之后新增的
        MarkAllProcessed();
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
    }
    private void OnDisable()
    {
        EditorApplication.hierarchyChanged -= OnHierarchyChanged;
    }
    private void OnHierarchyChanged()
    {
        if (Application.isPlaying) return;
        if (!autoSnapNew) return;
        var opt = BuildOptions();
        float? top = FloorSnapCore.GetFloorTopY(FloorSnapCore.FindFloor(opt));
        if (top == null) return;
        int changed = 0;
        foreach (var c in GetComponentsInChildren<Transform>(true))
        {
            if (c == transform) continue;
            int id = c.GetHashCode();
            if (processed.Contains(id)) continue; // 已处理过则跳过
            if (FloorSnapCore.SnapOne(c, top.Value, opt))
            {
                changed++;
                processed.Add(id);
            }
        }
        if (changed > 0)
        {
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
            Debug.Log("[地板贴合] 自动贴合了 " + changed + " 个新物体到地板。", this);
        }
    }
#endif
}

// ============ 3) 菜单一键贴合 ============
#if UNITY_EDITOR
public static class MapFloorSnapMenu
{
    [MenuItem("Tools/地图/贴合地板 Snap All To Floor")]
    public static void SnapAllToFloor()
    {
        int n = FloorSnapCore.SnapAll(null, new FloorSnapCore.Options());
        if (n > 0)
        {
            var map = FloorSnapCore.FindMapRoot(new FloorSnapCore.Options());
            if (map != null) EditorSceneManager.MarkSceneDirty(map.gameObject.scene);
            Debug.Log("[地板贴合] 已完成,共贴合 " + n + " 个物体。");
        }
        else
        {
            Debug.Log("[地板贴合] 没有需要贴合的物体(都在地板上,或找不到地板/地图)。");
        }
    }
}
#endif
