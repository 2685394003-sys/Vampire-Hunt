using UnityEngine;

namespace VampireHunt.Presentation.Boss
{
    /// <summary>Presentation-only flourish used by editable Boss VFX prefabs.</summary>
    [DisallowMultipleComponent]
    public sealed class BossVfxPulsePresenter : MonoBehaviour
    {
        [SerializeField] private float rotationDegreesPerSecond = 40f;
        [SerializeField] private float pulseAmount = 0.12f;
        [SerializeField] private float pulseFrequency = 6f;
        [SerializeField] private bool faceGround = true;
        private Vector3 m_BaseScale;

        private void Awake()
        {
            m_BaseScale = transform.localScale;
            if (faceGround) transform.Rotate(90f, 0f, 0f, Space.Self);
        }

        private void Update()
        {
            transform.Rotate(0f, 0f, rotationDegreesPerSecond * Time.deltaTime, Space.Self);
            float pulse = 1f + Mathf.Sin(Time.unscaledTime * pulseFrequency) * pulseAmount;
            transform.localScale = m_BaseScale * pulse;
        }
    }
}
