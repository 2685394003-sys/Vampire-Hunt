using UnityEngine;
using VampireHunt.Contracts;

namespace VampireHunt.Presentation.Combat
{
    /// <summary>
    /// Client-only visual adapter for accepted player ability casts. Gameplay executors never
    /// instantiate these objects; they only execute damage, while the network bridge delivers cues.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CombatAbilityExecutionPresenter : MonoBehaviour, ICombatAbilityPresentationSink
    {
        [Header("Circle Field")]
        [SerializeField] private uint circleFieldAbilityId = 160;
        [SerializeField] private GameObject circleFieldVfxPrefab;
        [SerializeField, Min(0.01f)] private float circleFieldBaseDiameter = 1f;
        [SerializeField, Min(0.01f)] private float circleFieldVfxLifetime = 0.6f;

        [Header("Flame Thrower")]
        [SerializeField] private uint flameThrowerAbilityId = 150;
        [SerializeField] private GameObject flameThrowerVfxPrefab;
        [SerializeField, Min(0.01f)] private float flameThrowerFallbackLifetime = 0.15f;

        public bool HandlesAbility(uint abilityId) =>
            abilityId == circleFieldAbilityId || abilityId == flameThrowerAbilityId;

        public void PresentAbility(in CombatAbilityPresentationCue cue)
        {
            if (cue.AbilityId == circleFieldAbilityId)
            {
                PresentCircleField(cue);
                return;
            }

            if (cue.AbilityId == flameThrowerAbilityId)
                PresentFlameThrower(cue);
        }

        private void PresentCircleField(in CombatAbilityPresentationCue cue)
        {
            if (circleFieldVfxPrefab == null) return;
            GameObject field = Instantiate(circleFieldVfxPrefab, ToVector3(cue.Origin), Quaternion.identity);
            float scale = Mathf.Max(0.1f, cue.Range) * 2f / Mathf.Max(0.01f, circleFieldBaseDiameter);
            field.transform.localScale = new Vector3(scale, field.transform.localScale.y, scale);
            Destroy(field, Mathf.Max(0.01f, circleFieldVfxLifetime));
        }

        private void PresentFlameThrower(in CombatAbilityPresentationCue cue)
        {
            if (flameThrowerVfxPrefab == null) return;
            Vector3 direction = ToVector3(cue.Direction);
            if (direction.sqrMagnitude < 0.0001f) direction = transform.forward;
            else direction.Normalize();

            GameObject flame = Instantiate(flameThrowerVfxPrefab, ToVector3(cue.Origin),
                Quaternion.LookRotation(direction, Vector3.up));
            ParticleSystem particles = flame.GetComponentInChildren<ParticleSystem>();
            if (particles == null)
            {
                Destroy(flame, Mathf.Max(0.01f, flameThrowerFallbackLifetime));
                return;
            }

            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = particles.main;
            float speed = main.startSpeed.constant;
            float lifetime = speed > 0.01f
                ? Mathf.Max(0.01f, cue.Range) / speed
                : Mathf.Max(0.01f, flameThrowerFallbackLifetime);
            main.startLifetime = lifetime;
            particles.Play(true);
            Destroy(flame, lifetime + 0.05f);
        }

        private static Vector3 ToVector3(in Float3 value) => new Vector3(value.X, value.Y, value.Z);
    }
}
