using System.Collections.Generic;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Presentation.Audio;

namespace VampireHunt.Presentation.Combat
{
    /// <summary>撞击使魔的纯表现组件。它只消费服务器控制器给出的姿态快照。</summary>
    [DisallowMultipleComponent]
    public sealed class ImpactFamiliarPresenter : MonoBehaviour, IImpactFamiliarPresentationSink
    {
        [SerializeField] private GameObject familiarVisualPrefab;
        [Tooltip("网络姿态之间的视觉追赶速度；只影响平滑，不影响服务器判定位置。")]
        [SerializeField, Min(0.1f)] private float visualFollowLerp = 20f;

        [Header("音效（留空则不发声）")]
        [Tooltip("使魔数量增加（首次召唤 / 血契叠加）时播放一次。例如「Play_Familiar_Summon」")]
        [SerializeField] private string summonEventName = "Play_Familiar_Summon";
        [Tooltip("撞击使魔进入冲刺（交战）时播放。例如「Play_Familiar_Attack」")]
        [SerializeField] private string attackEventName = "Play_Familiar_Attack";

        private readonly List<GameObject> m_Visuals = new List<GameObject>();
        private readonly List<PoseTarget> m_Targets = new List<PoseTarget>();
        private readonly List<bool> m_Engaged = new List<bool>();
        private int m_LastCount;
        private bool m_HasLastCount;

        public void Rebuild(int count)
        {
            Clear();
            // 数量增加 = 新召唤一只使魔；首次召唤与血契叠加都会走到这里。
            if (count > 0 && (!m_HasLastCount || count > m_LastCount))
                AudioCue.Post(summonEventName, gameObject);
            m_LastCount = count;
            m_HasLastCount = true;

            if (familiarVisualPrefab == null) return;
            for (int i = 0; i < count; i++)
            {
                m_Visuals.Add(Instantiate(familiarVisualPrefab, transform.position, Quaternion.identity));
                m_Targets.Add(new PoseTarget(transform.position, Quaternion.identity, Vector3.one));
                m_Engaged.Add(false);
            }
        }

        public void ApplyPose(in FamiliarVisualPose pose)
        {
            if ((uint)pose.Index >= (uint)m_Visuals.Count) return;
            GameObject visual = m_Visuals[pose.Index];
            if (visual == null) return;

            // 服务器用「冲刺时沿飞行方向拉长」表达交战状态（见 ImpactFamiliarController.Presentation），
            // 所以客户端从 Z/X 比例就能反解出「是否正在冲刺」，据此触发撞击音。
            bool engaged = pose.Scale.Z > pose.Scale.X * 1.01f;
            if (engaged && !m_Engaged[pose.Index]) AudioCue.Post(attackEventName, gameObject);
            m_Engaged[pose.Index] = engaged;

            PoseTarget target = m_Targets[pose.Index];
            target.Position = ToVector3(pose.Position);
            Vector3 facing = ToVector3(pose.Facing);
            if (facing.sqrMagnitude > 0.0001f)
                target.Rotation = ResolveRotation(facing);
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
            m_Engaged.Clear();
        }

        private void OnDestroy() => Clear();

        /// <summary>
        /// 由期望朝向构造旋转，处理「朝向与世界上方共线」的退化情况。
        /// <para>
        /// 待机时剑尖垂直向下（<c>Vector3.down</c>），此时
        /// <see cref="Quaternion.LookRotation(UnityEngine.Vector3)"/> 的 forward 与默认 up 共线会退化。
        /// 换用世界前方当 up，剑面朝玩家前后方向，剑不会随机翻滚。
        /// </para>
        /// </summary>
        private static Quaternion ResolveRotation(Vector3 facing)
        {
            Vector3 direction = facing.normalized;
            Vector3 up = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.999f ? Vector3.forward : Vector3.up;
            return Quaternion.LookRotation(direction, up);
        }

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
