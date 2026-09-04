using UnityEngine;

namespace VampireHunt.Presentation.Combat
{
    /// <summary>
    /// 闪电传导的<b>占位</b>视觉：用 LineRenderer 在源怪与目标怪之间画一条锯齿状闪电折线，
    /// 短暂停留后自毁。仅表现层，不参与玩法结算；正式美术资源（粒子/模型）到位后替换即可。
    /// </summary>
    public static class LightningChainVisual
    {
        private const float DefaultDuration = 0.22f;
        private static Material s_LineMaterial;

        /// <summary>
        /// 在 <paramref name="from"/> 与 <paramref name="to"/> 之间画一条闪电折线。
        /// <paramref name="intensity"/> 为传导强度（1 = 第 1 跳，随逐跳衰减递减），
        /// 用于缩放线宽与亮度，直观体现「越传越弱」。
        /// </summary>
        public static void Play(Vector3 from, Vector3 to, float intensity, float duration = DefaultDuration)
        {
            if (s_LineMaterial == null) s_LineMaterial = CreateLineMaterial();

            var go = new GameObject("LightningChain");
            LineRenderer line = go.AddComponent<LineRenderer>();
            line.material = s_LineMaterial;
            line.useWorldSpace = true;
            line.alignment = LineAlignment.View; // 截面始终面向相机，让闪电看起来是「带状」。
            line.numCapVertices = 4;
            line.numCornerVertices = 4;

            float width = Mathf.Max(0.04f, 0.1f * intensity);
            line.startWidth = width;
            line.endWidth = Mathf.Max(0.015f, width * 0.35f);

            Color baseColor = new Color(1f, 0.9f, 0.35f);
            line.startColor = baseColor;
            line.endColor = new Color(baseColor.r, baseColor.g, baseColor.b, 0.25f);

            int segments = 7;
            line.positionCount = segments + 1;
            Vector3 delta = to - from;
            Vector3 dir = delta.sqrMagnitude > 0.0001f ? delta.normalized : Vector3.forward;
            Vector3 axis = Vector3.Cross(dir, Vector3.up);
            if (axis.sqrMagnitude < 0.01f) axis = Vector3.Cross(dir, Vector3.right);
            axis.Normalize();
            float jitter = Mathf.Max(0.1f, delta.magnitude * 0.14f * intensity);

            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                Vector3 point = Vector3.Lerp(from, to, t);
                if (i > 0 && i < segments)
                {
                    point += axis * Random.Range(-jitter, jitter);
                    point += dir * Random.Range(-jitter * 0.4f, jitter * 0.4f);
                }
                line.SetPosition(i, point);
            }

            Object.Destroy(go, duration);
        }

        private static Material CreateLineMaterial()
        {
            Shader shader = Shader.Find("Sprites/Default")
                ?? Shader.Find("Universal Render Pipeline/Unlit")
                ?? Shader.Find("Unlit/Color")
                ?? Shader.Find("Standard");
            return new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
        }
    }
}
