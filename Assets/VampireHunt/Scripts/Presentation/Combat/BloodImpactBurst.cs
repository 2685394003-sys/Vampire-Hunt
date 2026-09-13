using System.Collections.Generic;
using UnityEngine;

namespace VampireHunt.Presentation.Combat
{
    /// <summary>
    /// 一次撞击血爆的生命周期承载组件：由 <see cref="BloodImpactVfx.Play"/> 动态创建，
    /// 逐层生成视觉件、逐帧驱动动画，到期自毁。
    /// </summary>
    /// <remarks>
    /// <para>四层时序（时间轴都从命中那一帧算起）：</para>
    /// <list type="number">
    /// <item><b>脉冲球</b>：亮红→暗红、瞬间涨开再归零 —— 这一段就是「顿」的那一下，爽感核心。</item>
    /// <item><b>冲击波</b>：贴水平面的扁平球，向外扩散，给撞击点一个可读的锚。</item>
    /// <item><b>血刺</b>：3~5 根随机方向的细长血锥，0.06 s 刺出后回缩。</item>
    /// <item><b>血雾</b>：十几颗血滴受重力 + 阻尼飞散；命中点正下方若探到地面，留一片贴地血渍。</item>
    /// </list>
    /// <para>
    /// 占位实现，不依赖任何美术资源。层与层之间互不依赖，可以在
    /// <see cref="BloodImpactParams"/> 里把任意一层关掉（数量填 0 / 时长填 0）。
    /// </para>
    /// <para>
    /// ⚠️ 正式实现应换成对象池 + VFX Graph：当前每次命中都新建 GameObject / Renderer，
    /// 在怪群密集、多把使魔同时冲刺时会有 GC（垃圾回收）与 draw call 压力。
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class BloodImpactBurst : MonoBehaviour
    {
        private const float PulseEmission = 3.2f;
        private const float SpikeEmission = 2.0f;
        private const float DropEmission = 0.9f;
        private const float WaveEmission = 0f;   // 无光照材质，不写自发光

        private float m_Radius = 0.9f;
        private Color m_Color = Color.white;
        private BloodImpactParams m_Params;

        private Part m_Pulse;
        private Part m_Wave;
        private readonly List<Part> m_Spikes = new List<Part>();
        private readonly List<Vector3> m_SpikeDirs = new List<Vector3>();
        private float m_SpikeLength;
        private readonly List<Part> m_Drops = new List<Part>();
        private readonly List<Vector3> m_DropVelocity = new List<Vector3>();
        private Part m_Stain;

        private float m_GroundY;
        private bool m_HasGround;

        private float m_Elapsed;
        private float m_Lifetime = 0.5f;
        private bool m_Built;
        private bool m_PulseDead;
        private bool m_WaveDead;
        private bool m_SpikesDead;
        private bool m_DropsDead;

        /// <summary>
        /// 按判定半径与参数装配一次血爆。由 <see cref="BloodImpactVfx.Play"/> 调用。
        /// </summary>
        /// <param name="radius">撞击判定半径（米）。所有尺寸 = radius × 参数里的倍率。</param>
        /// <param name="color">血的颜色。</param>
        /// <param name="parameters">分层参数。</param>
        public void Build(float radius, Color color, in BloodImpactParams parameters)
        {
            m_Radius = Mathf.Max(0.01f, radius);
            m_Color = color;
            m_Params = parameters;
            m_Elapsed = 0f;

            float longest = Mathf.Max(
                m_Params.pulseDuration,
                Mathf.Max(m_Params.waveDuration, Mathf.Max(m_Params.spikeDuration, m_Params.dropLifetime)));
            m_Lifetime = Mathf.Max(0.05f, longest);

            bool wantStain = m_Params.groundStain && m_Params.stainLifetime > 0f && m_Params.stainDiameter > 0f;
            if (wantStain || m_Params.dropCount > 0) ProbeGround();
            if (wantStain) m_Lifetime = Mathf.Max(m_Lifetime, m_Params.stainLifetime);

            BuildPulse();
            BuildWave();
            BuildSpikes();
            BuildDrops();
            BuildStain();

            m_Built = true;
        }

        private void Update()
        {
            if (!m_Built) return;

            m_Elapsed += Time.deltaTime;
            float t = m_Elapsed;

            UpdatePulse(t);
            UpdateWave(t);
            UpdateSpikes(t);
            UpdateDrops(t);
            UpdateStain(t);

            if (t >= m_Lifetime) BloodImpactVfx.DestroyObject(gameObject);
        }

        // ── 各层构建 ────────────────────────────────────────────

        private void BuildPulse()
        {
            if (m_Params.pulseDuration <= 0f || m_Params.pulseEndDiameter <= 0f) return;
            m_Pulse = CreatePart("Pulse", BloodImpactVfx.SphereMesh, BloodImpactVfx.SolidMaterial);
        }

        private void BuildWave()
        {
            if (m_Params.waveDuration <= 0f || m_Params.waveEndDiameter <= 0f) return;
            m_Wave = CreatePart("Wave", BloodImpactVfx.SphereMesh, BloodImpactVfx.LineMaterial);
        }

        private void BuildSpikes()
        {
            if (m_Params.spikeCount <= 0 || m_Params.spikeDuration <= 0f || m_Params.spikeLength <= 0f) return;

            m_SpikeLength = m_Params.spikeLength * m_Radius;
            for (int i = 0; i < m_Params.spikeCount; i++)
            {
                // 各向同性：均匀取球面上的随机方向（避免出现明显的上下堆叠感）。
                Vector3 dir = Random.onUnitSphere;

                Part part = CreatePart("Spike", BloodImpactVfx.SphereMesh, BloodImpactVfx.SolidMaterial);
                float thickness = m_Params.spikeThickness * m_Radius;
                // 球体本身对称，用 Z 当刺的轴：LookRotation 让局部 +Z 指向 dir。
                part.Transform.localRotation = Quaternion.LookRotation(dir);
                part.Transform.localPosition = dir * (m_SpikeLength * 0.5f);
                part.Transform.localScale = new Vector3(thickness, thickness, m_SpikeLength);

                m_Spikes.Add(part);
                m_SpikeDirs.Add(dir);
            }
        }

        private void BuildDrops()
        {
            if (m_Params.dropCount <= 0 || m_Params.dropLifetime <= 0f || m_Params.dropDiameter <= 0f) return;

            float diameter = m_Params.dropDiameter * m_Radius;
            float speedMin = Mathf.Max(0f, Mathf.Min(m_Params.dropSpeedMin, m_Params.dropSpeedMax));
            float speedMax = Mathf.Max(speedMin, m_Params.dropSpeedMax);

            for (int i = 0; i < m_Params.dropCount; i++)
            {
                // 上半球偏置：血滴先上扬再受重力落回，读作「喷出来」而不是「摊开」。
                Vector3 dir = Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) * 0.9f + 0.1f;
                dir.Normalize();

                Part part = CreatePart("Drop", BloodImpactVfx.SphereMesh, BloodImpactVfx.SolidMaterial);
                part.Transform.localPosition = dir * (m_Radius * 0.15f);
                part.Transform.localScale = Vector3.one * diameter;

                m_Drops.Add(part);
                m_DropVelocity.Add(dir * Random.Range(speedMin, speedMax));
            }
        }

        private void BuildStain()
        {
            if (!m_Params.groundStain || !m_HasGround) return;
            if (m_Params.stainLifetime <= 0f || m_Params.stainDiameter <= 0f) return;

            m_Stain = CreatePart("Stain", BloodImpactVfx.PlaneMesh, BloodImpactVfx.LineMaterial);
            m_Stain.Transform.localPosition = new Vector3(0f, m_GroundY - transform.position.y, 0f);
            m_Stain.Transform.localRotation = Quaternion.identity;
            m_Stain.Transform.localScale = Vector3.one * 0.6f;
        }

        // ── 各层驱动 ────────────────────────────────────────────

        private void UpdatePulse(float t)
        {
            if (m_Pulse == null || m_PulseDead) return;
            if (t >= m_Params.pulseDuration)
            {
                BloodImpactVfx.DestroyObject(m_Pulse.Go);
                m_Pulse = null;
                m_PulseDead = true;
                return;
            }

            float u = Mathf.Clamp01(t / m_Params.pulseDuration);
            float grow = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(u / 0.35f));   // 0~35% 涨开，之后只淡出
            float diameter = Mathf.Lerp(m_Params.pulseStartDiameter, m_Params.pulseEndDiameter, grow) * m_Radius;
            float alpha = Mathf.Pow(1f - u, 1.6f);

            m_Pulse.Transform.localScale = Vector3.one * diameter;
            // 起始是接近白亮的过曝红（闪光），随扩散退回血的本色。
            Color c = Color.Lerp(BloodImpactVfx.BloodHot, m_Color, Mathf.SmoothStep(0f, 1f, u * 1.2f));
            c.a = alpha;
            ApplyColor(m_Pulse, c, PulseEmission);
        }

        private void UpdateWave(float t)
        {
            if (m_Wave == null || m_WaveDead) return;
            if (t >= m_Params.waveDuration)
            {
                BloodImpactVfx.DestroyObject(m_Wave.Go);
                m_Wave = null;
                m_WaveDead = true;
                return;
            }

            float u = Mathf.Clamp01(t / m_Params.waveDuration);
            float grow = Mathf.SmoothStep(0f, 1f, u);
            float diameter = Mathf.Lerp(m_Params.waveStartDiameter, m_Params.waveEndDiameter, grow) * m_Radius;
            float thickness = Mathf.Max(0.005f, m_Params.waveThickness * m_Radius);

            m_Wave.Transform.localScale = new Vector3(diameter, thickness, diameter);
            Color c = m_Color * 1.25f;
            c.a = Mathf.Pow(1f - u, 1.2f) * 0.85f;
            ApplyColor(m_Wave, c, WaveEmission);
        }

        private void UpdateSpikes(float t)
        {
            if (m_SpikesDead || m_Spikes.Count == 0) return;
            if (t >= m_Params.spikeDuration)
            {
                for (int i = 0; i < m_Spikes.Count; i++) BloodImpactVfx.DestroyObject(m_Spikes[i].Go);
                m_Spikes.Clear();
                m_SpikesDead = true;
                return;
            }

            float grow = Mathf.Clamp01(t / Mathf.Max(0.001f, m_Params.spikeGrowTime));
            grow = 1f - (1f - grow) * (1f - grow);                      // easeOut
            float retract = Mathf.Clamp01(
                (t - m_Params.spikeGrowTime) / Mathf.Max(0.001f, m_Params.spikeDuration - m_Params.spikeGrowTime));
            float length = m_SpikeLength * grow * (1f - retract * 0.85f);
            float thickness = m_Params.spikeThickness * m_Radius * (1f - retract * 0.5f);

            Color c = m_Color;
            c.a = 1f - retract * retract;

            for (int i = 0; i < m_Spikes.Count; i++)
            {
                Part part = m_Spikes[i];
                if (part.Go == null) continue;
                Vector3 dir = m_SpikeDirs[i];
                part.Transform.localPosition = dir * (length * 0.5f);
                part.Transform.localScale = new Vector3(thickness, thickness, Mathf.Max(0.001f, length));
                ApplyColor(part, c, SpikeEmission);
            }
        }

        private void UpdateDrops(float t)
        {
            if (m_DropsDead || m_Drops.Count == 0) return;

            float life = Mathf.Max(0.01f, m_Params.dropLifetime);
            if (t >= life)
            {
                for (int i = 0; i < m_Drops.Count; i++) BloodImpactVfx.DestroyObject(m_Drops[i].Go);
                m_Drops.Clear();
                m_DropsDead = true;
                return;
            }

            float dt = Time.deltaTime;
            float drag = Mathf.Max(0f, m_Params.dropDrag);
            float damp = 1f - Mathf.Clamp01(drag * dt);
            float groundLimit = m_HasGround ? m_GroundY + m_Params.dropDiameter * m_Radius * 0.5f : float.NegativeInfinity;

            Color c = m_Color;
            c.a = t < life * 0.7f ? 1f : Mathf.InverseLerp(life, life * 0.7f, t);

            for (int i = 0; i < m_Drops.Count; i++)
            {
                Part part = m_Drops[i];
                if (part.Go == null) continue;
                if (!part.Go.activeSelf) continue;                       // 已落地，停更

                Vector3 v = m_DropVelocity[i];
                v *= damp;
                v.y -= Mathf.Max(0f, m_Params.dropGravity) * dt;
                m_DropVelocity[i] = v;

                Vector3 p = part.Transform.localPosition + v * dt;
                if (p.y + transform.position.y <= groundLimit)           // 触地：立刻消失（血渍那一层负责「留下痕迹」）
                {
                    part.Go.SetActive(false);
                    continue;
                }

                part.Transform.localPosition = p;
                ApplyColor(part, c, DropEmission);
            }
        }

        private void UpdateStain(float t)
        {
            if (m_Stain == null) return;

            float u = Mathf.Clamp01(t / Mathf.Max(0.01f, m_Params.stainLifetime));
            float spread = Mathf.SmoothStep(0.6f, 1f, Mathf.Clamp01(t / 0.12f));
            m_Stain.Transform.localScale = Vector3.one * (m_Params.stainDiameter * m_Radius * spread);

            Color c = BloodImpactVfx.StainColor;
            c.a = m_Params.stainOpacity * Mathf.Pow(1f - u, 1.3f);
            ApplyColor(m_Stain, c, 0f);
        }

        // ── 工具 ────────────────────────────────────────────────

        private void ProbeGround()
        {
            float probe = Mathf.Max(0.5f, m_Params.groundProbeScale * m_Radius);
            Vector3 origin = transform.position + Vector3.up * 0.2f;
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, probe,
                    m_Params.groundMask, QueryTriggerInteraction.Ignore))
            {
                m_GroundY = hit.point.y + 0.01f;                          // 抬高 1 cm 避免 z-fighting（深度冲突）
                m_HasGround = true;
            }
            else
            {
                m_HasGround = false;
            }
        }

        private Part CreatePart(string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            BloodImpactVfx.DisableShadows(renderer);

            return new Part(go, renderer);
        }

        private void ApplyColor(Part part, Color color, float emissionBoost)
        {
            if (part.Renderer == null) return;
            // 自发光随不透明度一起衰减，否则暗下去之后还会「发着光的血」。
            float boost = emissionBoost * Mathf.Clamp01(color.a);
            BloodImpactVfx.SetRendererColor(part.Renderer, color, boost);
        }

        private sealed class Part
        {
            public readonly GameObject Go;
            public readonly Transform Transform;
            public readonly Renderer Renderer;

            public Part(GameObject go, Renderer renderer)
            {
                Go = go;
                Transform = go.transform;
                Renderer = renderer;
            }
        }
    }
}
