using UnityEngine;

namespace VampireHunt.Infrastructure.Netcode
{
    /// <summary>
    /// Short-lived laser beam visual: a stretched cube positioned between two world points.
    /// Spawned per laser tick and self-destroys after <see cref="lifetime"/>.
    /// </summary>
    public sealed class LaserBeamVisual : MonoBehaviour
    {
        [SerializeField] private float lifetime = 0.1f;

        /// <summary>Positions the beam between <paramref name="start"/> and <paramref name="end"/> with a square cross-section.</summary>
        public void Configure(Vector3 start, Vector3 end, float width)
        {
            Vector3 delta = end - start;
            float length = Mathf.Max(0.05f, delta.magnitude);
            Vector3 forward = delta.sqrMagnitude > 0.0001f ? delta / length : Vector3.forward;

            transform.position = start + forward * (length * 0.5f);
            transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            transform.localScale = new Vector3(width, width, length);

            Destroy(gameObject, lifetime);
        }
    }
}
