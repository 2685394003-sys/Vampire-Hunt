using System.Collections.Generic;
using UnityEngine;
using VampireHunt.Contracts;

namespace VampireHunt.Presentation.Combat
{
    /// <summary>撞击使魔的纯表现组件。它只消费服务器控制器给出的姿态快照。</summary>
    [DisallowMultipleComponent]
    public sealed class ImpactFamiliarPresenter : MonoBehaviour, IImpactFamiliarPresentationSink
    {
        [SerializeField] private GameObject familiarVisualPrefab;
        [Tooltip("网络姿态之间的视觉追赶速度；只影响平滑，不影响服务器判定位置。")]
        [SerializeField, Min(0.1f)] private float visualFollowLerp = 20f;

        private readonly List<GameObject> m_Visuals = new List<GameObject>();
        private readonly List<PoseTarget> m_Targets = new List<PoseTarget>();

        public void Rebuild(int count)
        {
            Clear();
            if (familiarVisualPrefab == null) return;
            for (int i = 0; i < count; i++)
            {
                m_Visuals.Add(Instantiate(familiarVisualPrefab, transform.position, Quaternion.identity));
                m_Targets.Add(new PoseTarget(transform.position, Quaternion.identity, Vector3.one));
            }
        }

        public void ApplyPose(in FamiliarVisualPose pose)
        {
            if ((uint)pose.Index >= (uint)m_Visuals.Count) return;
            GameObject visual = m_Visuals[pose.Index];
            if (visual == null) return;

            PoseTarget target = m_Targets[pose.Index];
            target.Position = ToVector3(pose.Position);
            Vector3 facing = ToVector3(pose.Facing);
            if (facing.sqrMagnitude > 0.0001f)
                target.Rotation = Quaternion.LookRotation(facing);
            target.Scale = ToVector3(pose.Scale);

            if (!target.Initialized)
            {
                visual.transform.SetPositionAndRotation(target.Position, target.Rotation);
                visual.transform.localScale = target.Scale;
                target.Initialized = true;
            }
        }

        private void LateUpdate()
        {
            float t = 1f - Mathf.Exp(-Mathf.Max(0.1f, visualFollowLerp) * Time.deltaTime);
            for (int i = 0; i < m_Visuals.Count; i++)
            {
                GameObject visual = m_Visuals[i];
                PoseTarget target = m_Targets[i];
                if (visual == null || !target.Initialized) continue;
                visual.transform.position = Vector3.Lerp(visual.transform.position, target.Position, t);
                visual.transform.rotation = Quaternion.Slerp(visual.transform.rotation, target.Rotation, t);
                visual.transform.localScale = Vector3.Lerp(visual.transform.localScale, target.Scale, t);
            }
        }

        public void Clear()
        {
            for (int i = 0; i < m_Visuals.Count; i++)
                if (m_Visuals[i] != null) Destroy(m_Visuals[i]);
            m_Visuals.Clear();
            m_Targets.Clear();
        }

        private void OnDestroy() => Clear();

        private static Vector3 ToVector3(in Float3 value) => new Vector3(value.X, value.Y, value.Z);

        private sealed class PoseTarget
        {
            public Vector3 Position;
            public Quaternion Rotation;
            public Vector3 Scale;
            public bool Initialized;

            public PoseTarget(Vector3 position, Quaternion rotation, Vector3 scale)
            {
                Position = position;
                Rotation = rotation;
                Scale = scale;
            }
        }
    }
}
