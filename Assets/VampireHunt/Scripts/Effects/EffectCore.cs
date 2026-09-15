using System;
using System.Collections.Generic;
using VampireHunt.Contracts;
using VampireHunt.SharedKernel;

namespace VampireHunt.Effects
{
    [Flags]
    public enum EffectExecutionRealm : byte
    {
        None = 0,
        Server = 1 << 0,
        Owner = 1 << 1,
        Presentation = 1 << 2
    }

    public enum EffectSourceKind : byte
    {
        Pact = 1,
        Status = 2,
        Equipment = 3,
        Ability = 4,
        World = 5,
        EnemyAffix = 6
    }

    [Flags]
    public enum EffectBlockFlags : byte
    {
        None = 0,
        Action = 1 << 0,
        Movement = 1 << 1
    }

    public readonly struct EffectSourceKey : IEquatable<EffectSourceKey>
    {
        public EffectSourceKind Kind { get; }
        public uint DefinitionId { get; }
        public ulong InstanceId { get; }

        public EffectSourceKey(EffectSourceKind kind, uint definitionId, ulong instanceId = 0)
        {
            Kind = kind;
            DefinitionId = definitionId;
            InstanceId = instanceId;
        }

        public bool Equals(EffectSourceKey other) =>
            Kind == other.Kind && DefinitionId == other.DefinitionId && InstanceId == other.InstanceId;
        public override bool Equals(object obj) => obj is EffectSourceKey other && Equals(other);
        public override int GetHashCode() => ((int)Kind, DefinitionId, InstanceId).GetHashCode();
    }

    public readonly struct EffectRuntimeState
    {
        public EffectSourceKey Key { get; }
        public EntityId Source { get; }
        public EntityId Target { get; }
        public float Stacks { get; }
        public float Magnitude { get; }
        public double StartTime { get; }
        public double EndTime { get; }
        /// <summary>属性精通：影响所有元素效果（挂元素层数、闪电连锁传导复制层数）。</summary>
        public float ElementMastery { get; }

        public EffectRuntimeState(
            in EffectSourceKey key,
            EntityId source,
            EntityId target,
            float stacks,
            float magnitude,
            double startTime,
            double endTime,
            float elementMastery = 1f)
        {
            Key = key;
            Source = source;
            Target = target;
            Stacks = Math.Max(0f, stacks);
            Magnitude = magnitude;
            StartTime = startTime;
            EndTime = endTime;
            ElementMastery = elementMastery > 0f ? elementMastery : 1f;
        }
    }

    public interface IEffectModuleDescriptor
    {
        uint ModuleTypeId { get; }
        EffectExecutionRealm Realm { get; }
    }

    public sealed class EffectDefinition
    {
        public EffectSourceKind SourceKind { get; }
        public uint DefinitionId { get; }
        public IEffectModuleDescriptor[] Modules { get; }

        public EffectDefinition(
            EffectSourceKind sourceKind,
            uint definitionId,
            IEffectModuleDescriptor[] modules)
        {
            if (definitionId == 0) throw new ArgumentOutOfRangeException(nameof(definitionId));
            SourceKind = sourceKind;
            DefinitionId = definitionId;
            Modules = modules ?? Array.Empty<IEffectModuleDescriptor>();
        }

        public EffectBlockFlags GetBlockFlags()
        {
            EffectBlockFlags result = EffectBlockFlags.None;
            for (int i = 0; i < Modules.Length; i++)
                if (Modules[i] is ActionBlockEffectDescriptor block) result |= block.Flags;
            return result;
        }

        public uint GetPresentationCueId()
        {
            for (int i = 0; i < Modules.Length; i++)
                if (Modules[i] is PresentationCueEffectDescriptor cue) return cue.CueId;
            return 0;
        }
    }

    public interface IEffectPortResolver
    {
        bool TryGet<TPort>(out TPort port) where TPort : class;
    }

    public sealed class EffectPortCollection : IEffectPortResolver
    {
        private readonly List<object> m_Ports = new List<object>();

        public void Add(object port)
        {
            if (port != null && !m_Ports.Contains(port)) m_Ports.Add(port);
        }

        public bool TryGet<TPort>(out TPort port) where TPort : class => TryGet<TPort>(null, out port);

        /// <summary>
        /// 按谓词取第一个匹配端口。同一 GameObject 上可能挂多个实现同一端口的组件
        /// （如撞击/射击两个使魔控制器都实现 <c>IFamiliarPactTarget</c>），调用方用谓词按身份精确定位。
        /// </summary>
        public bool TryGet<TPort>(Func<TPort, bool> predicate, out TPort port) where TPort : class
        {
            for (int i = 0; i < m_Ports.Count; i++)
            {
                if (!(m_Ports[i] is TPort match)) continue;
                if (predicate != null && !predicate(match)) continue;
                port = match;
                return true;
            }
            port = null;
            return false;
        }
    }

    public enum EffectCommandKind : byte
    {
        PeriodicDamage = 1,
        ApplyStatus = 2,
        RemoveSource = 3
    }

    public readonly struct EffectCommand
    {
        public EffectCommandKind Kind { get; }
        public EffectRuntimeState State { get; }
        public float Amount { get; }
        public DamageTags DamageTags { get; }
        public uint StatusId { get; }
        public float StatusStacks { get; }

        private EffectCommand(
            EffectCommandKind kind,
            in EffectRuntimeState state,
            float amount,
            DamageTags damageTags,
            uint statusId,
            float statusStacks)
        {
            Kind = kind;
            State = state;
            Amount = amount;
            DamageTags = damageTags;
            StatusId = statusId;
            StatusStacks = statusStacks;
        }

        public static EffectCommand PeriodicDamage(in EffectRuntimeState state, float amount, DamageTags tags) =>
            new EffectCommand(EffectCommandKind.PeriodicDamage, state, amount, tags, 0, 0f);

        public static EffectCommand ApplyStatus(in EffectRuntimeState state, uint statusId, float stacks) =>
            new EffectCommand(EffectCommandKind.ApplyStatus, state, 0f, DamageTags.None, statusId, stacks);

        public static EffectCommand RemoveSource(in EffectRuntimeState state) =>
            new EffectCommand(EffectCommandKind.RemoveSource, state, 0f, DamageTags.None, 0, 0f);
    }

    public interface IEffectCommandSink
    {
        void Enqueue(in EffectCommand command);
    }

    public readonly struct EffectRuntimeContext
    {
        public EffectExecutionRealm Realm { get; }
        public IEffectPortResolver Ports { get; }
        public IEffectCommandSink Commands { get; }

        public EffectRuntimeContext(
            EffectExecutionRealm realm,
            IEffectPortResolver ports,
            IEffectCommandSink commands)
        {
            Realm = realm;
            Ports = ports;
            Commands = commands;
        }
    }

    public interface IEffectRuntimeModule : IDisposable
    {
        void Install(in EffectRuntimeState state);
        void Update(in EffectRuntimeState previous, in EffectRuntimeState current);
        void Tick(double time);
    }

    public interface IEffectBlockProvider
    {
        EffectBlockFlags BlockFlags { get; }
    }

    public interface IEffectModuleFactory
    {
        uint ModuleTypeId { get; }
        IEffectRuntimeModule Create(IEffectModuleDescriptor descriptor, in EffectRuntimeContext context);
    }

    public sealed class EffectModuleRegistry
    {
        private readonly Dictionary<uint, IEffectModuleFactory> m_Factories =
            new Dictionary<uint, IEffectModuleFactory>();

        public void Register(IEffectModuleFactory factory)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (m_Factories.ContainsKey(factory.ModuleTypeId))
                throw new InvalidOperationException($"Duplicate effect module type {factory.ModuleTypeId}.");
            m_Factories.Add(factory.ModuleTypeId, factory);
        }

        public bool TryCreate(
            IEffectModuleDescriptor descriptor,
            in EffectRuntimeContext context,
            out IEffectRuntimeModule runtime)
        {
            runtime = null;
            return descriptor != null &&
                   (descriptor.Realm & context.Realm) != 0 &&
                   m_Factories.TryGetValue(descriptor.ModuleTypeId, out IEffectModuleFactory factory) &&
                   (runtime = factory.Create(descriptor, context)) != null;
        }
    }

    public sealed class EffectRuntimeHost : IDisposable
    {
        private sealed class RuntimeInstance
        {
            public EffectDefinition Definition;
            public EffectRuntimeState State;
            public readonly List<IEffectRuntimeModule> Modules = new List<IEffectRuntimeModule>();
        }

        private readonly EffectModuleRegistry m_Registry;
        private readonly Dictionary<EffectSourceKey, RuntimeInstance> m_Instances =
            new Dictionary<EffectSourceKey, RuntimeInstance>();

        public int SourceCount => m_Instances.Count;

        public EffectRuntimeHost(EffectModuleRegistry registry)
        {
            m_Registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public void SetSource(
            EffectDefinition definition,
            in EffectRuntimeState state,
            in EffectRuntimeContext context)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (definition.SourceKind != state.Key.Kind || definition.DefinitionId != state.Key.DefinitionId)
                throw new InvalidOperationException("Effect definition and source key do not match.");

            if (m_Instances.TryGetValue(state.Key, out RuntimeInstance existing))
            {
                EffectRuntimeState previous = existing.State;
                existing.State = state;
                for (int i = 0; i < existing.Modules.Count; i++) existing.Modules[i].Update(previous, state);
                return;
            }

            var instance = new RuntimeInstance { Definition = definition, State = state };
            m_Instances.Add(state.Key, instance);
            for (int i = 0; i < definition.Modules.Length; i++)
            {
                if (!m_Registry.TryCreate(definition.Modules[i], context, out IEffectRuntimeModule module)) continue;
                instance.Modules.Add(module);
                module.Install(state);
            }
        }

        public bool RemoveSource(in EffectSourceKey key)
        {
            if (!m_Instances.TryGetValue(key, out RuntimeInstance instance)) return false;
            for (int i = instance.Modules.Count - 1; i >= 0; i--) instance.Modules[i].Dispose();
            m_Instances.Remove(key);
            return true;
        }

        public void RemoveSourceKind(EffectSourceKind kind)
        {
            var keys = new List<EffectSourceKey>();
            foreach (EffectSourceKey key in m_Instances.Keys) if (key.Kind == kind) keys.Add(key);
            for (int i = 0; i < keys.Count; i++) RemoveSource(keys[i]);
        }

        public bool HasBlock(EffectBlockFlags flags)
        {
            foreach (RuntimeInstance instance in m_Instances.Values)
            {
                for (int i = 0; i < instance.Modules.Count; i++)
                {
                    if (instance.Modules[i] is IEffectBlockProvider block &&
                        (block.BlockFlags & flags) != 0) return true;
                }
            }
            return false;
        }

        public void Tick(double time)
        {
            foreach (RuntimeInstance instance in m_Instances.Values)
                for (int i = 0; i < instance.Modules.Count; i++) instance.Modules[i].Tick(time);
        }

        public void Clear()
        {
            var keys = new List<EffectSourceKey>(m_Instances.Keys);
            for (int i = 0; i < keys.Count; i++) RemoveSource(keys[i]);
        }

        public void Dispose() => Clear();
    }
}
