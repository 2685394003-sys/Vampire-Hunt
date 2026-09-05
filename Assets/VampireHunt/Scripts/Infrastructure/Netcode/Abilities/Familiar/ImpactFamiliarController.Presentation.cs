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

        private void SyncVisuals()
        {
            if (m_PresentationPublisher == null) return;
            for (int i = 0; i < m_Brains.Count; i++)
            {
                ImpactFamiliarBrain brain = m_Brains[i];
                // 冲刺与掉头滑行时沿飞行方向拉长，做出水滴被甩出去的形状。
                float stretch = brain.IsEngaged ? m_Definition.StretchFactor : 1f;
                float scale = m_Definition.VisualScale;
                m_PresentationPublisher.PublishPose(new FamiliarVisualPose(
                    i,
                    ToFloat3(brain.Position),
                    ToFloat3(brain.Facing),
                    new Float3(scale, scale, scale * stretch)));
            }
        }

        private static Float3 ToFloat3(Vector3 value) => new Float3(value.x, value.y, value.z);
    }
}
