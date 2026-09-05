using System.Collections.Generic;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Unity;
using VampireHunt.Player.Abilities.Familiar;

namespace VampireHunt.Presentation.Combat
{
    /// <summary>射击使魔的纯表现组件。状态机和命中均不在这里执行。</summary>
    [DisallowMultipleComponent]
    public sealed class GunnerFamiliarPresenter : MonoBehaviour, IGunnerFamiliarPresentationSink
    {
        private const float MuzzleVfxLifetime = 1f;

        [SerializeField] private GunnerFamiliarAsset definitionAsset;
        [SerializeField] private GameObject familiarVisualPrefab;
        [SerializeField] private GameObject fallbackTracerPrefab;
        [SerializeField] private GameObject fallbackImpactVfxPrefab;
        [SerializeField] private GameObject fallbackMuzzleVfxPrefab;
        [SerializeField, Min(0.02f)] private float impactVfxLifetime = 0.14f;
        [SerializeField, Min(0.05f)] private float tracerScaleMultiplier = 1f;
        [Tooltip("网络姿态之间的视觉追赶速度；只影响平滑，不影响服务器判定位置。")]
        [SerializeField, Min(0.1f)] private float visualFollowLerp = 20f;

        private readonly List<GameObject> m_Visuals = new List<GameObject>();
        private readonly List<PoseTarget> m_Targets = new List<PoseTarget>();
        private readonly List<Tracer> m_Tracers = new List<Tracer>();
        private bool m_WarnedMissingTracer;
        private Material m_FallbackMaterial;

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

        public void PlayShot(in GunnerFamiliarShotPresentationCue cue)
        {
            Vector3 origin = ToVector3(cue.Origin);
            Vector3 end = ToVector3(cue.End);
            Vector3 direction = ToVector3(cue.Direction);
            GunnerFamiliarWeaponProfile profile = definitionAsset != null
                ? definitionAsset.FindWeapon((FamiliarWeaponId)cue.WeaponId)
                : null;

            SpawnMuzzle(profile != null ? profile.muzzleVfxPrefab : null, origin, direction);
            SpawnTracer(profile != null ? profile.tracerPrefab : null, origin, end, direction,
                cue.TracerSpeed, cue.TracerScale, cue.ShowImpactOnArrival);
        }

        public void Clear()
        {
            for (int i = 0; i < m_Visuals.Count; i++)
                if (m_Visuals[i] != null) Destroy(m_Visuals[i]);
            m_Visuals.Clear();
            m_Targets.Clear();

            for (int i = 0; i < m_Tracers.Count; i++)
                if (m_Tracers[i].Visual != null) Destroy(m_Tracers[i].Visual);
            m_Tracers.Clear();
        }

        private void Update()
        {
            for (int i = m_Tracers.Count - 1; i >= 0; i--)
            {
                Tracer tracer = m_Tracers[i];
                if (tracer.Visual == null)
                {
                    m_Tracers.RemoveAt(i);
                    continue;
                }

                tracer.Elapsed += Time.deltaTime;
                float total = tracer.TotalDistance / tracer.Speed;
                float t = total <= 0.0001f ? 1f : Mathf.Clamp01(tracer.Elapsed / total);
                tracer.Visual.transform.position = Vector3.Lerp(tracer.From, tracer.To, t);
                if (t < 1f) continue;

                if (tracer.ShowImpactOnArrival) SpawnImpact(tracer.To);
                Destroy(tracer.Visual);
                m_Tracers.RemoveAt(i);
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

        private void OnDestroy()
        {
            Clear();
            if (m_FallbackMaterial == null) return;
            Destroy(m_FallbackMaterial);
            m_FallbackMaterial = null;
        }

        private void SpawnMuzzle(GameObject weaponPrefab, Vector3 origin, Vector3 direction)
        {
            GameObject prefab = weaponPrefab != null ? weaponPrefab : fallbackMuzzleVfxPrefab;
            if (prefab == null) return;
            GameObject vfx = Instantiate(prefab, origin,
                direction.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(direction) : Quaternion.identity);
            Destroy(vfx, MuzzleVfxLifetime);
        }

        private void SpawnImpact(Vector3 point)
        {
            if (fallbackImpactVfxPrefab == null) return;
            GameObject vfx = Instantiate(fallbackImpactVfxPrefab, point, Quaternion.identity);
            Destroy(vfx, impactVfxLifetime);
        }

        private void SpawnTracer(
            GameObject weaponPrefab,
            Vector3 from,
            Vector3 to,
            Vector3 direction,
            float speed,
            float scale,
            bool showImpactOnArrival)
        {
            GameObject prefab = weaponPrefab != null ? weaponPrefab : fallbackTracerPrefab;
            GameObject visual = prefab != null
                ? Instantiate(prefab, from,
                    direction.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(direction) : Quaternion.identity)
                : CreateFallbackTracer(from, direction);

            if (prefab == null && !m_WarnedMissingTracer)
            {
                m_WarnedMissingTracer = true;
                Debug.LogWarning("[GunnerFamiliarPresenter] 未配置曳光弹预制体，使用临时发光球。", this);
            }

            visual.transform.localScale *= Mathf.Max(0.05f, tracerScaleMultiplier) * Mathf.Max(0.05f, scale);
            m_Tracers.Add(new Tracer(visual, from, to, speed, showImpactOnArrival));
        }

        private GameObject CreateFallbackTracer(Vector3 position, Vector3 direction)
        {
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Collider collider = visual.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            visual.transform.position = position;
            if (direction.sqrMagnitude > 0.0001f) visual.transform.rotation = Quaternion.LookRotation(direction);
            visual.transform.localScale = new Vector3(0.13f, 0.13f, 0.6f);

            if (m_FallbackMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                m_FallbackMaterial = new Material(shader);
                m_FallbackMaterial.SetColor("_BaseColor", Color.white);
                m_FallbackMaterial.SetColor("_EmissionColor", new Color(3.2f, 2.4f, 0.6f, 1f));
                m_FallbackMaterial.EnableKeyword("_EMISSION");
            }

            MeshRenderer renderer = visual.GetComponent<MeshRenderer>();
            if (renderer != null) renderer.sharedMaterial = m_FallbackMaterial;
            return visual;
        }

        private static Vector3 ToVector3(in Float3 value) => new Vector3(value.X, value.Y, value.Z);

        private sealed class Tracer
        {
            public readonly GameObject Visual;
            public readonly Vector3 From;
            public readonly Vector3 To;
            public readonly float Speed;
            public readonly float TotalDistance;
            public readonly bool ShowImpactOnArrival;
            public float Elapsed;

            public Tracer(GameObject visual, Vector3 from, Vector3 to, float speed, bool showImpactOnArrival)
            {
                Visual = visual;
                From = from;
                To = to;
                Speed = Mathf.Max(1f, speed);
                TotalDistance = Vector3.Distance(from, to);
                ShowImpactOnArrival = showImpactOnArrival;
            }
        }

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
