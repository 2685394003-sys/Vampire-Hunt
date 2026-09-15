using System;
using System.Collections.Generic;
using VampireHunt.Contracts;
using VampireHunt.Effects;
using VampireHunt.SharedKernel;

namespace VampireHunt.Combat
{
    public readonly struct StatusEffectSnapshot
    {
        public uint StatusId { get; }
        public EntityId Source { get; }
        public float Stacks { get; }
        public float Magnitude { get; }
        public double StartTime { get; }
        public double EndTime { get; }
        public EffectBlockFlags BlockFlags { get; }
        public uint PresentationCueId { get; }
        public ElementId Element { get; }
        public float ElementMastery { get; }

        public StatusEffectSnapshot(
            uint statusId,
            EntityId source,
            float stacks,
            float magnitude,
            double startTime,
            double endTime,
            EffectBlockFlags blockFlags,
            uint presentationCueId,
            ElementId element,
            float elementMastery)
        {
            StatusId = statusId;
            Source = source;
            Stacks = stacks;
            Magnitude = magnitude;
            StartTime = startTime;
            EndTime = endTime;
            BlockFlags = blockFlags;
            PresentationCueId = presentationCueId;
            Element = element;
            ElementMastery = elementMastery;
        }
    }

    public readonly struct StatusEffectMutation
    {
        public bool Applied { get; }
        public bool WasAdded { get; }
        public StatusEffectSnapshot Previous { get; }
        public StatusEffectSnapshot Current { get; }

        public StatusEffectMutation(
            bool applied,
            bool wasAdded,
            in StatusEffectSnapshot previous,
            in StatusEffectSnapshot current)
        {
            Applied = applied;
            WasAdded = wasAdded;
            Previous = previous;
            Current = current;
        }
    }

    /// <summary>Status lifetime and stacking only. Gameplay behavior lives in effect modules.</summary>
    public sealed class StatusEffectCollection
    {
        private sealed class RuntimeStatus
        {
            public StatusEffectDefinition Definition;
            public EntityId Source;
            public float Stacks;
            public float Magnitude;
            public double StartTime;
            public double EndTime;
            public float ElementMastery;
        }

        private readonly Dictionary<uint, RuntimeStatus> m_Statuses = new Dictionary<uint, RuntimeStatus>();
        private readonly List<uint> m_RemoveBuffer = new List<uint>();

        public uint Revision { get; private set; }
        public int Count => m_Statuses.Count;
        public bool Has(uint statusId) => statusId != 0 && m_Statuses.ContainsKey(statusId);

        public StatusEffectMutation Apply(
            StatusEffectDefinition definition,
            in StatusApplicationRequest request,
            double time)
        {
            if (definition == null || request.Spec.StatusId != definition.StatusId) return default;
            double duration = request.Spec.Duration > 0f ? request.Spec.Duration : definition.DefaultDuration;
            float magnitude = request.Spec.Magnitude > 0f ? request.Spec.Magnitude : 1f;

            if (!m_Statuses.TryGetValue(definition.StatusId, out RuntimeStatus runtime))
            {
                runtime = new RuntimeStatus
                {
                    Definition = definition,
                    Source = request.Source,
                    Stacks = Math.Min(definition.MaxStacks, request.Spec.Stacks),
                    Magnitude = magnitude,
                    StartTime = time,
                    EndTime = time + duration,
                    ElementMastery = request.Spec.ElementMastery
                };
                m_Statuses.Add(definition.StatusId, runtime);
                Revision++;
                StatusEffectSnapshot current = ToSnapshot(runtime);
                return new StatusEffectMutation(true, true, default, current);
            }

            StatusEffectSnapshot previous = ToSnapshot(runtime);
            runtime.Source = request.Source;
            runtime.ElementMastery = request.Spec.ElementMastery;
            switch (definition.StackPolicy)
            {
                case StatusStackPolicy.AddStacksAndRefresh:
                    runtime.Stacks = Math.Min(definition.MaxStacks, runtime.Stacks + request.Spec.Stacks);
                    runtime.Magnitude = Math.Max(runtime.Magnitude, magnitude);
                    runtime.EndTime = time + duration;
                    break;
                case StatusStackPolicy.ReplaceIfStronger:
                    if (magnitude < runtime.Magnitude) return new StatusEffectMutation(true, false, previous, previous);
                    runtime.Stacks = Math.Min(definition.MaxStacks, request.Spec.Stacks);
                    runtime.Magnitude = magnitude;
                    runtime.EndTime = time + duration;
                    break;
                case StatusStackPolicy.AddStacksKeepDuration:
                    runtime.Stacks = Math.Min(definition.MaxStacks, runtime.Stacks + request.Spec.Stacks);
                    runtime.Magnitude = Math.Max(runtime.Magnitude, magnitude);
                    // 不刷新 EndTime：整组层数在同一时刻到期（闪电"时间结束后需重新叠层"）。
                    break;
                default:
                    runtime.Stacks = Math.Max(runtime.Stacks, Math.Min(definition.MaxStacks, request.Spec.Stacks));
                    runtime.Magnitude = Math.Max(runtime.Magnitude, magnitude);
                    runtime.EndTime = time + duration;
                    break;
            }

            Revision++;
            return new StatusEffectMutation(true, false, previous, ToSnapshot(runtime));
        }

        public bool TryRemove(uint statusId, out StatusEffectSnapshot removed)
        {
            if (!m_Statuses.TryGetValue(statusId, out RuntimeStatus runtime))
            {
                removed = default;
                return false;
            }
            removed = ToSnapshot(runtime);
            m_Statuses.Remove(statusId);
            Revision++;
            return true;
        }

        /// <summary>消耗指定层数（元素反应"等比合成"用）。层数扣到 ≤0 时整个状态移除。</summary>
        public bool TryConsumeStacks(uint statusId, float amount)
        {
            if (!m_Statuses.TryGetValue(statusId, out RuntimeStatus runtime) || amount <= 0f) return false;
            runtime.Stacks -= amount;
            Revision++;
            if (runtime.Stacks <= 0f) m_Statuses.Remove(statusId);
            return true;
        }

        public void RemoveExpired(double time, List<StatusEffectSnapshot> output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            output.Clear();
            m_RemoveBuffer.Clear();
            foreach (KeyValuePair<uint, RuntimeStatus> pair in m_Statuses)
            {
                if (time < pair.Value.EndTime) continue;
                output.Add(ToSnapshot(pair.Value));
                m_RemoveBuffer.Add(pair.Key);
            }
            for (int i = 0; i < m_RemoveBuffer.Count; i++)
            {
                m_Statuses.Remove(m_RemoveBuffer[i]);
                Revision++;
            }
        }

        public void Capture(List<StatusEffectSnapshot> output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            output.Clear();
            foreach (RuntimeStatus status in m_Statuses.Values) output.Add(ToSnapshot(status));
            output.Sort((left, right) => left.StatusId.CompareTo(right.StatusId));
        }

        public void Clear()
        {
            if (m_Statuses.Count == 0) return;
            m_Statuses.Clear();
            Revision++;
        }

        private static StatusEffectSnapshot ToSnapshot(RuntimeStatus status) =>
            new StatusEffectSnapshot(
                status.Definition.StatusId,
                status.Source,
                status.Stacks,
                status.Magnitude,
                status.StartTime,
                status.EndTime,
                status.Definition.BlockFlags,
                status.Definition.PresentationCueId,
                status.Definition.Element,
                status.ElementMastery);
    }
}
