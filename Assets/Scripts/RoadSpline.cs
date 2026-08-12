using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

namespace VampireHunt.Tools
{
    /// <summary>
    /// 沿自定义样条（Catmull-Rom，零依赖）铺设道路/围墙片段。
    /// 支持：拖动路径点拉长/转弯、闭合转一圈、弧长均匀采样无缝平铺、
    /// Terrain 与 Raycast 两种贴地（网格地形/石头/桥都行）、编辑器 Undo + Prefab 实例连接。
    /// </summary>
    public class RoadSpline : MonoBehaviour
    {
        public enum Axis { Z, X }
        public enum HeightMode { None, Terrain, Raycast }

        [Header("路径点（本地坐标，相对本物体）")]
        [Tooltip("在 Scene 视图拖动黄色手柄编辑；用 Inspector 按钮增删点")]
        public List<Vector3> points = new List<Vector3>();

        [Tooltip("是否闭合（转一圈连回起点，无缝）")]
        public bool closed = false;

        [Header("片段")]
        [Tooltip("道路/围墙片段预制体（Project 窗口里的 Prefab）")]
        public GameObject prefab;
        [Tooltip("单段模型实际长度（参考/默认间距）")]
        public float segmentLength = 2.65f;
        [Tooltip("相邻片段中心距离；等于 segmentLength 时无缝平铺，更大则留缝")]
        public float spacing = 2.65f;
        [Tooltip("从起点空出的距离")]
        public float startOffset = 0f;

        [Header("朝向")]
        [Tooltip("模型前进轴：多数道路沿 +Z，部分沿 +X")]
        public Axis forwardAxis = Axis.Z;
        [Tooltip("额外旋转微调（度）")]
        public Vector3 rotationOffset = Vector3.zero;

        [Header("贴地")]
        [Tooltip("高度贴合方式")]
        public HeightMode heightMode = HeightMode.Raycast;
        [Tooltip("Raycast 模式：从多高往下打 + 最长探测距离")]
        public float raycastHeight = 50f;
        public float raycastMaxDist = 100f;
        [Tooltip("Raycast 只打这些层（默认 Everything）")]
        public LayerMask snapLayers = -1;
        [Tooltip("贴地后再抬高的量")]
        public float heightOffset = 0f;

        [Header("生成结果")]
        [Tooltip("生成的片段挂在哪个子物体下（自动创建 GeneratedRoad）")]
        public Transform generatedRoot;

        // ============================================================
        // Catmull-Rom：穿过错所有控制点的平滑曲线
        // ============================================================
        public Vector3 GetPoint(float globalT)
        {
            int n = points.Count;
            if (n == 0) return transform.position;
            if (n == 1) return transform.TransformPoint(points[0]);

            int segCount = closed ? n : n - 1;
            float f = Mathf.Clamp01(globalT) * segCount;
            int i = Mathf.FloorToInt(f);
            if (i >= segCount) i = segCount - 1;
            float lt = f - i;

            Vector3 p0 = GetLocalPoint(i - 1);
            Vector3 p1 = GetLocalPoint(i);
            Vector3 p2 = GetLocalPoint(i + 1);
            Vector3 p3 = GetLocalPoint(i + 2);
            return transform.TransformPoint(CatmullRom(p0, p1, p2, p3, lt));
        }

        public Vector3 GetTangent(float globalT)
        {
            int n = points.Count;
            if (n < 2) return transform.forward;
            int segCount = closed ? n : n - 1;
            float f = Mathf.Clamp01(globalT) * segCount;
            int i = Mathf.FloorToInt(f);
            if (i >= segCount) i = segCount - 1;
            float lt = f - i;
            Vector3 p0 = GetLocalPoint(i - 1);
            Vector3 p1 = GetLocalPoint(i);
            Vector3 p2 = GetLocalPoint(i + 1);
            Vector3 p3 = GetLocalPoint(i + 2);
            return (transform.rotation * CatmullRomTangent(p0, p1, p2, p3, lt)).normalized;
        }

        Vector3 GetLocalPoint(int index)
        {
            int n = points.Count;
            if (n == 0) return Vector3.zero;
            if (closed)
            {
                index = ((index % n) + n) % n;
                return points[index];
            }
            index = Mathf.Clamp(index, 0, n - 1);
            return points[index];
        }

        static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t, t3 = t2 * t;
            return 0.5f * (
                2f * p1 +
                (-p0 + p2) * t +
                (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        static Vector3 CatmullRomTangent(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float t2 = t * t;
            return 0.5f * (
                (-p0 + p2) +
                2f * (2f * p0 - 5f * p1 + 4f * p2 - p3) * t +
                3f * (-p0 + 3f * p1 - 3f * p2 + p3) * t2);
        }

        // ============================================================
        // 生成
        // ============================================================
        [ContextMenu("生成 / 重新生成道路")]
        public void Regenerate()
        {
            if (prefab == null) { Debug.LogError("[RoadSpline] 未指定 prefab"); return; }
            if (points.Count < 2) { Debug.LogError("[RoadSpline] 至少需要 2 个路径点"); return; }

            BuildLUT(out float total, out float[] cum);
            EnsureRoot();
            ClearGenerated();

            float dist = startOffset;
            int safety = 0;
            while (dist <= total + 1e-4f && safety++ < 100000)
            {
                float d = Mathf.Min(dist, total);
                // 闭合样条：跳过与起点重合的缝，避免重复片段
                if (closed && d >= total - 1e-4f) break;
                SpawnAt(d, cum, total);
                dist += spacing;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
        }

        void SpawnAt(float distance, float[] cum, float total)
        {
            float t = DistanceToT(distance, cum, total);
            Vector3 worldPos = GetPoint(t);
            Vector3 worldTan = GetTangent(t);
            Vector3 worldUp = transform.up; // 道路默认朝上，可用 rotationOffset 微调

            if (heightMode == HeightMode.Terrain && Terrain.activeTerrain != null)
                worldPos.y = Terrain.activeTerrain.SampleHeight(worldPos) + heightOffset;
            else if (heightMode == HeightMode.Raycast)
            {
                Ray ray = new Ray(worldPos + Vector3.up * raycastHeight, Vector3.down);
                if (Physics.Raycast(ray, out RaycastHit hit, raycastHeight + raycastMaxDist, snapLayers))
                    worldPos.y = hit.point.y + heightOffset;
            }

            GameObject go = CreateInstance();
            if (go == null) return; // prefab 为空或非法时 CreateInstance 会打日志，这里直接跳过
            Quaternion rot = Quaternion.LookRotation(worldTan, worldUp);
            if (forwardAxis == Axis.X) rot *= Quaternion.Euler(0f, 90f, 0f);
            rot *= Quaternion.Euler(rotationOffset);
            go.transform.SetPositionAndRotation(worldPos, rot);
        }

        GameObject CreateInstance()
        {
            if (prefab == null)
            {
                Debug.LogError("[RoadSpline] CreateInstance 被调用时 prefab 为 null，请把 Project 窗口里的 Prefab 拖到 prefab 字段。");
                return null;
            }
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                // 优先用 PrefabUtility 保留预制体连接；如果失败则回退到 Instantiate
                if (PrefabUtility.GetPrefabAssetType(prefab) != PrefabAssetType.NotAPrefab)
                {
                    GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, generatedRoot);
                    if (go != null)
                    {
                        Undo.RegisterCreatedObjectUndo(go, "Spawn road segment");
                        return go;
                    }
                    Debug.LogWarning("[RoadSpline] PrefabUtility.InstantiatePrefab 返回 null，改用 Instantiate 作为回退。请确认 prefab 字段拖的是 Project 窗口里的 Prefab 资源。");
                }
                GameObject inst = Instantiate(prefab, generatedRoot);
                Undo.RegisterCreatedObjectUndo(inst, "Spawn road segment");
                return inst;
            }
#endif
            return Instantiate(prefab, generatedRoot);
        }

        void EnsureRoot()
        {
            if (generatedRoot != null) return;
            Transform existing = transform.Find("GeneratedRoad");
            if (existing != null) { generatedRoot = existing; return; }
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                GameObject go = new GameObject("GeneratedRoad");
                go.transform.SetParent(transform, false);
                Undo.RegisterCreatedObjectUndo(go, "Create road root");
                generatedRoot = go.transform;
                return;
            }
#endif
            GameObject g = new GameObject("GeneratedRoad");
            g.transform.SetParent(transform, false);
            generatedRoot = g.transform;
        }

        void ClearGenerated()
        {
            if (generatedRoot == null) return;
            for (int i = generatedRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = generatedRoot.GetChild(i);
#if UNITY_EDITOR
                if (!Application.isPlaying) Undo.DestroyObjectImmediate(child.gameObject);
                else Destroy(child.gameObject);
#else
                Destroy(child.gameObject);
#endif
            }
        }

        // ============================================================
        // 弧长均匀采样：先按参数均匀采样建累积长度 LUT，再按距离反查参数 t
        // （豆包直接把 dist/total 当 t，弯处会忽密忽疏）
        // ============================================================
        void BuildLUT(out float total, out float[] cum)
        {
            int segCount = closed ? points.Count : points.Count - 1;
            int div = Mathf.Clamp(segCount * 32, 64, 6000);
            cum = new float[div + 1];
            Vector3 prev = GetPoint(0f);
            cum[0] = 0f;
            for (int i = 1; i <= div; i++)
            {
                Vector3 p = GetPoint(i / (float)div);
                cum[i] = cum[i - 1] + Vector3.Distance(prev, p);
                prev = p;
            }
            total = cum[div];
        }

        float DistanceToT(float distance, float[] cum, float total)
        {
            int n = cum.Length - 1;
            distance = Mathf.Clamp(distance, 0f, total);
            for (int i = 1; i <= n; i++)
            {
                if (cum[i] >= distance)
                {
                    float seg = cum[i] - cum[i - 1];
                    float f = seg > 1e-6f ? (distance - cum[i - 1]) / seg : 0f;
                    return (i - 1 + f) / n;
                }
            }
            return 1f;
        }

        // ============================================================
        // 编辑器辅助
        // ============================================================
        private void Reset()
        {
            points = new List<Vector3>
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(0f, 0f, 5f),
            };
        }

        public void AddPointAtEnd()
        {
#if UNITY_EDITOR
            Undo.RecordObject(this, "Add road point");
#endif
            Vector3 last = points.Count > 0 ? points[points.Count - 1] : Vector3.zero;
            float step = spacing > 0f ? spacing : segmentLength;
            points.Add(last + new Vector3(0f, 0f, step));
#if UNITY_EDITOR
            if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(gameObject.scene);
#endif
        }

        public void ClearPoints()
        {
#if UNITY_EDITOR
            Undo.RecordObject(this, "Clear road points");
#endif
            points.Clear();
        }
    }
}
