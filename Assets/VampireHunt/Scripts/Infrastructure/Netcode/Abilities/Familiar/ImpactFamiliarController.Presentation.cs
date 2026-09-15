using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Player.Abilities.Familiar;

namespace VampireHunt.Infrastructure.Netcode.Abilities.Familiar
{
    public sealed partial class ImpactFamiliarController
    {
        // ── 视觉 ──────────────────────────────────────────────

        private void SpawnVisuals()
        {
            m_PresentationPublisher?.SetVisualCount(m_Brains.Count);
            SyncVisuals();
        }

        private void DestroyVisuals()
        {
            m_PresentationPublisher?.SetVisualCount(0);
        }

        /// <summary>
        /// 待机 / 返回途中发布的「剑尖垂下」方向（世界空间）。
        /// <para>
        /// 有朝向的模型（剑 / 矛 / 枪）挂在环绕轨道上时，若沿用水平移动方向会横着飘 ——
        /// 玩家看到的是「一把横着的剑在绕圈」。所以待机一律让剑尖垂下，交给表现层做平滑插值。
        /// </para>
        /// </summary>
        private static readonly Vector3 IdleTipDirection = Vector3.down;

        private void SyncVisuals()
        {
            if (m_PresentationPublisher == null) return;
            for (int i = 0; i < m_Brains.Count; i++)
            {
                ImpactFamiliarBrain brain = m_Brains[i];
                // 冲刺与掉头滑行时沿飞行方向拉长，做出水滴被甩出去的形状。
                float stretch = brain.IsEngaged ? m_Definition.StretchFactor : 1f;
                float scale = m_Definition.VisualScale;
                // ⚠️ 这里发布的是「视觉朝向」，不是移动方向 —— 表现层直接拿它做 LookRotation。
                //    交战（冲刺 / 掉头）= 追着目标（水平）；待机（轨道环绕）/ 返回途中 = 剑尖垂下。
                //    玩法侧的击退方向与命中法线读的是 brain.Facing 本身，不受此处影响。
                Vector3 visualFacing = brain.IsEngaged ? brain.Facing : IdleTipDirection;
                m_PresentationPublisher.PublishPose(new FamiliarVisualPose(
                    i,
                    ToFloat3(brain.Position),
                    ToFloat3(visualFacing),
                    new Float3(scale, scale, scale * stretch)));
            }
        }

        private static Float3 ToFloat3(Vector3 value) => new Float3(value.x, value.y, value.z);
    }
}
