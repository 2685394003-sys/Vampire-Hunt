using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VampireHunt.EditorTools
{
    /// <summary>
    /// 开发阶段用的网格摆放工具（不是游戏内功能）。
    /// 在 Scene 视图里把房屋/地形预制体吸附到 FlowFieldManager 的网格上，
    /// 点一下就实例化一个"真物体"放进场景，随 .unity 文件一起保存，退出不会丢。
    /// 快捷键：左键放置 / R 旋转 / X 删除光标下物体 / PageUp·PageDown 抬高降低。
    /// </summary>
    public class GridBuilderTool : EditorWindow
    {
        private GridBuilderCatalog catalog;
        private FlowFieldManager flowField;
        private GameObject buildRoot;

        // 没有 FlowFieldManager 时的手动网格兜底
        private float manualCellSize = 1.2f;
        private Vector3 manualOrigin = Vector3.zero;

        private int selectedIndex = 0;
        private int rotationSteps;            // 0..3 => 0/90/180/270 度
        private float heightStep;             // 额外抬高(沿世界 Y)

        private bool hasGhost;
        private Vector3 ghostPos;
        private Vector2Int ghostBaseCell;
        private Vector2Int ghostDims;
        private bool ghostInBounds = true;

        private GameObject ghostInstance;     // 跟随光标的预览实例(不进存档)
        private GameObject ghostPrefabRef;    // 当前预览用的是哪个 prefab

        private const string RootName = "DevBuildRoot";
        private const int ObstacleLayerDefault = 3;
        private const string CatalogPath = "Assets/Editor/GridBuilderCatalog.asset";

        [MenuItem("Tools/Vampire Hunt/Grid Builder")]
        static void Open() => GetWindow<GridBuilderTool>("Grid Builder");

        void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            if (flowField == null) flowField = FindFirstObjectByType<FlowFieldManager>();
            buildRoot = GameObject.Find(RootName);
            if (catalog == null) catalog = AssetDatabase.LoadAssetAtPath<GridBuilderCatalog>(CatalogPath);
            SceneView.RepaintAll();
        }

        void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            DestroyGhost();
        }

        // ---- 网格换算(优先复用 FlowFieldManager 的网格) ----
        private bool HasFlowField => flowField != null;
        private float CellSize => HasFlowField ? flowField.cellSize : manualCellSize;
        private float GroundY => HasFlowField ? flowField.transform.position.y : manualOrigin.y;

        private Vector2Int WorldToGrid(Vector3 w)
        {
            Vector3 o = HasFlowField ? flowField.transform.position : manualOrigin;
            float cs = CellSize;
            return new Vector2Int(
                Mathf.FloorToInt((w.x - o.x) / cs),
                Mathf.FloorToInt((w.z - o.z) / cs));
        }

        private Vector3 GridToWorld(Vector2Int g)
        {
            Vector3 o = HasFlowField ? flowField.transform.position : manualOrigin;
            float cs = CellSize;
            return new Vector3(o.x + (g.x + 0.5f) * cs, o.y, o.z + (g.y + 0.5f) * cs);
        }

        private bool InBounds(Vector2Int baseCell, Vector2Int dims)
        {
            if (!HasFlowField) return true;
            for (int x = 0; x < dims.x; x++)
                for (int y = 0; y < dims.y; y++)
                {
                    Vector2Int c = new Vector2Int(baseCell.x + x, baseCell.y + y);
                    if (!flowField.IsInGrid(c.x, c.y)) return false;
                }
            return true;
        }

        private static int FirstLayer(LayerMask mask)
        {
            int m = mask.value;
            for (int i = 0; i < 32; i++)
                if ((m & (1 << i)) != 0) return i;
            return 0;
        }

        private int ObstacleLayer => HasFlowField ? FirstLayer(flowField.obstacleLayer) : ObstacleLayerDefault;

        // ---- Scene 视图交互 ----
        void OnSceneGUI(SceneView sv)
        {
            if (Application.isPlaying)
            {
                Handles.BeginGUI();
                GUILayout.Label("Grid Builder：请在非运行(Edit)模式下使用。", EditorStyles.boldLabel);
                Handles.EndGUI();
                return;
            }
            if (catalog == null || catalog.entries.Count == 0) return;

            selectedIndex = Mathf.Clamp(selectedIndex, 0, catalog.entries.Count - 1);
            GridBuilderEntry e = catalog.entries[selectedIndex];

            Event ev = Event.current;
            Ray ray = HandleUtility.GUIPointToWorldRay(ev.mousePosition);
            Plane ground = new Plane(Vector3.up, new Vector3(0f, GroundY, 0f));

            hasGhost = false;
            if (ground.Raycast(ray, out float dist))
            {
                ComputePlacement(ray.GetPoint(dist), e, out ghostPos, out ghostBaseCell, out ghostDims);
                ghostInBounds = InBounds(ghostBaseCell, ghostDims);
                hasGhost = true;
            }

            if (ev.type == EventType.KeyDown)
            {
                if (ev.keyCode == KeyCode.R) { rotationSteps = (rotationSteps + 1) % 4; ev.Use(); }
                else if (ev.keyCode == KeyCode.X) { TryDeleteUnderCursor(ray); ev.Use(); }
                else if (ev.keyCode == KeyCode.PageUp) { heightStep += CellSize * 0.5f; ev.Use(); }
                else if (ev.keyCode == KeyCode.PageDown) { heightStep -= CellSize * 0.5f; ev.Use(); }
            }

            if (ev.type == EventType.MouseDown && ev.button == 0 && hasGhost && ghostInBounds && !ev.alt)
            {
                Place(e, ghostPos, rotationSteps);
                ev.Use();
            }

            UpdateGhostPrefab(e);
            if (ghostInstance != null && hasGhost)
            {
                ghostInstance.transform.position = ghostPos;
                ghostInstance.transform.rotation = Quaternion.Euler(0f, rotationSteps * 90f, 0f);
                ghostInstance.transform.localScale = Vector3.one;
                ghostInstance.SetActive(true);
            }
            else if (ghostInstance != null)
            {
                ghostInstance.SetActive(false);
            }

            DrawGhostBox();

            HandleUtility.Repaint();
            sv.Repaint();
        }

        private void ComputePlacement(Vector3 cursorWorld, GridBuilderEntry e,
            out Vector3 pos, out Vector2Int baseCell, out Vector2Int dims)
        {
            dims = (rotationSteps % 2 == 1)
                ? new Vector2Int(e.sizeZ, e.sizeX)
                : new Vector2Int(e.sizeX, e.sizeZ);

            baseCell = WorldToGrid(cursorWorld);
            baseCell.x -= Mathf.FloorToInt((dims.x - 1) / 2f);
            baseCell.y -= Mathf.FloorToInt((dims.y - 1) / 2f);

            Vector3 c = GridToWorld(baseCell);
            c.x += (dims.x - 1) * 0.5f * CellSize;
            c.z += (dims.y - 1) * 0.5f * CellSize;
            c.y = GroundY + e.yOffset + heightStep;
            pos = c;
        }

        private void DrawGhostBox()
        {
            if (!hasGhost) return;
            Color c = ghostInBounds ? new Color(0.2f, 0.9f, 0.3f, 0.9f) : new Color(0.9f, 0.2f, 0.2f, 0.9f);
            Vector3 size = new Vector3(ghostDims.x * CellSize, 0.05f, ghostDims.y * CellSize);
            Vector3 center = ghostPos + Vector3.up * 0.05f;
            Handles.color = c;
            Handles.DrawWireCube(center, size);
            Handles.color = Color.white;
        }

        private void UpdateGhostPrefab(GridBuilderEntry e)
        {
            if (ghostPrefabRef != e.prefab)
            {
                DestroyGhost();
                if (e.prefab != null)
                {
                    ghostInstance = (GameObject)PrefabUtility.InstantiatePrefab(e.prefab);
                    if (ghostInstance != null)
                        ghostInstance.hideFlags = HideFlags.HideAndDontSave;
                }
                ghostPrefabRef = e.prefab;
            }
        }

        private void DestroyGhost()
        {
            if (ghostInstance != null) DestroyImmediate(ghostInstance);
            ghostInstance = null;
            ghostPrefabRef = null;
        }

        // ---- 放置 / 删除 ----
        private void Place(GridBuilderEntry e, Vector3 pos, int rotSteps)
        {
            if (e.prefab == null) return;
            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(e.prefab);
            if (go == null) go = Instantiate(e.prefab);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.Euler(0f, rotSteps * 90f, 0f);
            go.transform.SetParent(EnsureRoot().transform, false);
            if (e.placeOnObstacleLayer)
                SetLayerRecursively(go, ObstacleLayer);
            Undo.RegisterCreatedObjectUndo(go, "Place " + e.displayName);
            EditorSceneManager.MarkSceneDirty(go.scene);
            Debug.Log($"[GridBuilder] 已放置: {e.displayName} @ {pos}");
        }

        private void TryDeleteUnderCursor(Ray ray)
        {
            if (!Physics.Raycast(ray, out RaycastHit hit, 2000f)) return;
            Transform t = hit.transform;
            if (t == null) return;
            if (buildRoot != null && t.IsChildOf(buildRoot.transform))
            {
                GameObject target = t.gameObject;
                while (target.transform.parent != buildRoot.transform)
                    target = target.transform.parent.gameObject;
                Undo.DestroyObjectImmediate(target);
                EditorSceneManager.MarkSceneDirty(target.scene);
            }
            else
            {
                Undo.DestroyObjectImmediate(t.gameObject);
                EditorSceneManager.MarkSceneDirty(t.gameObject.scene);
            }
        }

        private GameObject EnsureRoot()
        {
            if (buildRoot != null) return buildRoot;
            buildRoot = GameObject.Find(RootName);
            if (buildRoot == null)
            {
                buildRoot = new GameObject(RootName);
                Undo.RegisterCreatedObjectUndo(buildRoot, "Create " + RootName);
            }
            return buildRoot;
        }

        private static void SetLayerRecursively(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform)
                SetLayerRecursively(child.gameObject, layer);
        }

        // ---- 工具窗口 UI ----
        private SerializedObject so;

        void OnGUI()
        {
            EditorGUILayout.HelpBox(
                "开发阶段网格摆放工具（非游戏内功能）。物体在 Scene 视图里直接放成场景物体，随 .unity 保存，退出不丢。\n" +
                "左键=放置  R=旋转90°  X=删除光标下物体  PageUp/PageDown=抬高/降低  (需在 Edit 模式、且 Grid Builder 窗口打开)",
                MessageType.Info);

            EditorGUILayout.LabelField("网格来源 / Grid Source", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            flowField = (FlowFieldManager)EditorGUILayout.ObjectField("FlowFieldManager", flowField, typeof(FlowFieldManager), true);
            if (EditorGUI.EndChangeCheck()) SceneView.RepaintAll();

            if (!HasFlowField)
            {
                manualCellSize = EditorGUILayout.FloatField("格大小 cellSize", manualCellSize);
                manualOrigin = EditorGUILayout.Vector3Field("网格原点 origin", manualOrigin);
                EditorGUILayout.HelpBox("未指定 FlowFieldManager，使用上方手动网格参数。", MessageType.Warning);
            }
            else
            {
                if (GUILayout.Button("重新扫描障碍网格 (供红/绿校验)"))
                {
                    flowField.ForceRescan();
                    SceneView.RepaintAll();
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("目录 / Catalog", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            catalog = (GridBuilderCatalog)EditorGUILayout.ObjectField("Catalog", catalog, typeof(GridBuilderCatalog), false);
            if (EditorGUI.EndChangeCheck())
            {
                so = null;
                SceneView.RepaintAll();
            }
            if (catalog == null)
            {
                if (GUILayout.Button("创建默认 Catalog 资产"))
                    CreateCatalog();
                return;
            }

            if (so == null || so.targetObject != catalog)
                so = new SerializedObject(catalog);
            so.Update();
            SerializedProperty entries = so.FindProperty("entries");
            EditorGUILayout.PropertyField(entries, true);
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(catalog);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("当前选择 / Selected", EditorStyles.boldLabel);
            if (catalog.entries.Count > 0)
            {
                selectedIndex = Mathf.Clamp(selectedIndex, 0, catalog.entries.Count - 1);
                using (new EditorGUILayout.HorizontalScope())
                {
                    for (int i = 0; i < catalog.entries.Count; i++)
                    {
                        GUI.backgroundColor = (i == selectedIndex) ? Color.green : Color.white;
                        if (GUILayout.Button($"{i + 1}: {(string.IsNullOrEmpty(catalog.entries[i].displayName) ? "?" : catalog.entries[i].displayName)}"))
                            selectedIndex = i;
                    }
                }
                GUI.backgroundColor = Color.white;
                EditorGUILayout.LabelField($"旋转: {rotationSteps * 90}°    额外高度: {heightStep:F2}");
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("重置旋转/高度")) { rotationSteps = 0; heightStep = 0f; }
                    if (GUILayout.Button("删除所有已放置物体"))
                    {
                        if (buildRoot != null)
                        {
                            Undo.DestroyObjectImmediate(buildRoot);
                            buildRoot = null;
                        }
                    }
                    if (GUILayout.Button("选中 DevBuildRoot"))
                    {
                        EnsureRoot();
                        Selection.activeGameObject = buildRoot;
                    }
                }
            }
            else
            {
                EditorGUILayout.HelpBox("目录为空：在上方列表点 + 添加预制体。", MessageType.Warning);
            }
        }

        private void CreateCatalog()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Editor"))
                AssetDatabase.CreateFolder("Assets", "Editor");
            GridBuilderCatalog asset = CreateInstance<GridBuilderCatalog>();
            AssetDatabase.CreateAsset(asset, CatalogPath);
            AssetDatabase.SaveAssets();
            catalog = asset;
            so = null;
            Debug.Log($"[GridBuilder] 已创建目录资产: {CatalogPath}");
        }
    }
}
