#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VampireHunt.Tools
{
    [CustomEditor(typeof(RoadSpline))]
    public class RoadSplineEditor : Editor
    {
        private RoadSpline R => (RoadSpline)target;

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            if (GUILayout.Button("在末端添加路径点（拉长/延伸）"))
            {
                R.AddPointAtEnd();
                SceneView.RepaintAll();
            }
            if (GUILayout.Button("清空路径点"))
            {
                if (EditorUtility.DisplayDialog("清空路径点", "确定清空所有路径点？", "清空", "取消"))
                    R.ClearPoints();
            }

            EditorGUILayout.Space(6);
            if (GUILayout.Button("生成 / 重新生成道路", GUILayout.Height(30)))
                R.Regenerate();

            EditorGUILayout.HelpBox(
                "用法：\n" +
                "1) Scene 视图拖动黄色手柄编辑路径；点“在末端添加路径点”可拉长/延伸。\n" +
                "2) 勾 closed 让道路转一圈闭合（无缝）。\n" +
                "3) 把道路片段 Prefab 拖到 prefab 字段，调 spacing / forwardAxis 让片段对齐平铺。\n" +
                "4) 点“生成 / 重新生成道路”。结果挂在 GeneratedRoad 子物体下，随时可重生成。\n" +
                "贴地支持 Terrain 与 Raycast（网格地形/石头/桥都行，不只能 Terrain）。",
                MessageType.Info);
        }

        private void OnSceneGUI()
        {
            if (R.points == null) return;
            int n = R.points.Count;

            // 曲线预览
            Handles.color = Color.yellow;
            Vector3 prev = R.GetPoint(0f);
            int steps = Mathf.Clamp(n * 24, 24, 600);
            for (int i = 1; i <= steps; i++)
            {
                Vector3 p = R.GetPoint(i / (float)steps);
                Handles.DrawLine(prev, p);
                prev = p;
            }

            // 路径点手柄（拖动编辑）
            for (int i = 0; i < n; i++)
            {
                EditorGUI.BeginChangeCheck();
                Vector3 world = R.transform.TransformPoint(R.points[i]);
                float size = HandleUtility.GetHandleSize(world) * 0.15f;
                var fmh_66_28_639219943154119026 = Quaternion.identity; Vector3 moved = Handles.FreeMoveHandle(
                    world, size, Vector3.zero, Handles.SphereHandleCap);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(R, "Move road point");
                    R.points[i] = R.transform.InverseTransformPoint(moved);
                    EditorSceneManager.MarkSceneDirty(R.gameObject.scene);
                }
            }
        }
    }
}
#endif
