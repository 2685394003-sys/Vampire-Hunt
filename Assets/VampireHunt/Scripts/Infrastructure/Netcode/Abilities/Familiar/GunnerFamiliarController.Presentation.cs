using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Player.Abilities.Familiar;

namespace VampireHunt.Infrastructure.Netcode.Abilities.Familiar
{
    public sealed partial class GunnerFamiliarController
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
            float scale = m_Definition.VisualScale;
            for (int i = 0; i < m_Brains.Count; i++)
            {
                GunnerFamiliarBrain brain = m_Brains[i];
                m_PresentationPublisher.PublishPose(new FamiliarVisualPose(
                    i,
                    ToFloat3(brain.Position),
                    ToFloat3(brain.Facing),
                    new Float3(scale, scale, scale)));
            }
        }

        private void PresentShot(
            Vector3 origin,
            Vector3 end,
            Vector3 direction,
            GunnerFamiliarWeaponDefinition weapon,
            bool showImpactOnArrival)
        {
            if (m_PresentationPublisher == null || weapon == null) return;
            m_PresentationPublisher.PublishShot(new GunnerFamiliarShotPresentationCue(
                (byte)m_CurrentWeapon,
                ToFloat3(origin),
                ToFloat3(end),
                ToFloat3(direction),
                weapon.projectileSpeed,
                weapon.tracerScale,
                showImpactOnArrival));
        }

        private static Float3 ToFloat3(Vector3 value) => new Float3(value.x, value.y, value.z);
    }
}
