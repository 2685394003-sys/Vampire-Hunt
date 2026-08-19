using System;
using System.Collections.Generic;
using UnityEngine;
using VampireHunt.Core;
using VampireHunt.Core.Contracts;
using CoreEntityId = VampireHunt.Core.EntityId;

namespace VampireHunt.Bootstrap
{
    /// <summary>Unity simulation clock injected into all authoritative services.</summary>
    public sealed class UnityGameClock : IGameClock
    {
        public double Now => Time.timeAsDouble;
        public float DeltaTime => Time.deltaTime;
    }

    /// <summary>One seeded random stream shared by a composed authoritative runtime.</summary>
    public sealed class SeededRandomSource : IRandomSource
    {
        private readonly System.Random random;

        public SeededRandomSource(int seed)
        {
            random = new System.Random(seed);
        }

        public float NextFloat() => (float)random.NextDouble();

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            return random.Next(minInclusive, maxExclusive);
        }
    }

    /// <summary>
    /// Separates authoritative event publication from client event ingress.
    /// Incoming network events are delivered locally and are never echoed back
    /// to the transport; authoritative events may fan out to both transport and
    /// a host/offline presentation dispatcher.
    /// </summary>
    public sealed class RuntimeGameplayEventHub :
        IGameplayEventSink,
        IGameplayEventIngress,
        IDisposable
    {
        private readonly List<IGameplayEventSink> outbound = new();
        private readonly List<IGameplayEventIngress> localIngress = new();
        private bool disposed;

        public IDisposable AddOutbound(IGameplayEventSink sink)
        {
            if (sink == null) throw new ArgumentNullException(nameof(sink));
            ThrowIfDisposed();
            if (!outbound.Contains(sink)) outbound.Add(sink);
            return new Registration(() => outbound.Remove(sink));
        }

        public IDisposable AddLocalIngress(IGameplayEventIngress ingress)
        {
            if (ingress == null) throw new ArgumentNullException(nameof(ingress));
            ThrowIfDisposed();
            if (!localIngress.Contains(ingress)) localIngress.Add(ingress);
            return new Registration(() => localIngress.Remove(ingress));
        }

        public void Publish(IGameplayEvent @event)
        {
            if (@event == null) throw new ArgumentNullException(nameof(@event));
            ThrowIfDisposed();

            IGameplayEventSink[] outboundSnapshot = outbound.ToArray();
            for (int i = 0; i < outboundSnapshot.Length; i++)
                outboundSnapshot[i].Publish(@event);

            PushLocal(@event);
        }

        public void Push(IGameplayEvent @event)
        {
            if (@event == null) return;
            ThrowIfDisposed();
            PushLocal(@event);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            outbound.Clear();
            localIngress.Clear();
        }

        private void PushLocal(IGameplayEvent @event)
        {
            IGameplayEventIngress[] ingressSnapshot = localIngress.ToArray();
            for (int i = 0; i < ingressSnapshot.Length; i++)
                ingressSnapshot[i].Push(@event);
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(RuntimeGameplayEventHub));
        }

        private sealed class Registration : IDisposable
        {
            private Action unregister;

            public Registration(Action unregister)
            {
                this.unregister = unregister;
            }

            public void Dispose()
            {
                Action current = unregister;
                unregister = null;
                current?.Invoke();
            }
        }
    }

    /// <summary>Fans logical spawn/despawn notifications out to registered adapters.</summary>
    public sealed class RuntimeEntityLifecycleHub : IEntityLifecycleEventSink, IDisposable
    {
        private readonly List<IEntityLifecycleEventSink> sinks = new();
        private bool disposed;

        public IDisposable Add(IEntityLifecycleEventSink sink)
        {
            if (sink == null) throw new ArgumentNullException(nameof(sink));
            ThrowIfDisposed();
            if (!sinks.Contains(sink)) sinks.Add(sink);
            return new Registration(() => sinks.Remove(sink));
        }

        public void EntitySpawned(CoreEntityId entityId)
        {
            ThrowIfDisposed();
            IEntityLifecycleEventSink[] snapshot = sinks.ToArray();
            for (int i = 0; i < snapshot.Length; i++) snapshot[i].EntitySpawned(entityId);
        }

        public void EntityDespawned(CoreEntityId entityId)
        {
            ThrowIfDisposed();
            IEntityLifecycleEventSink[] snapshot = sinks.ToArray();
            for (int i = 0; i < snapshot.Length; i++) snapshot[i].EntityDespawned(entityId);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            sinks.Clear();
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(RuntimeEntityLifecycleHub));
        }

        private sealed class Registration : IDisposable
        {
            private Action unregister;

            public Registration(Action unregister)
            {
                this.unregister = unregister;
            }

            public void Dispose()
            {
                Action current = unregister;
                unregister = null;
                current?.Invoke();
            }
        }
    }

    /// <summary>Authoritative simulation order adopted by ARCHITECTURE.md §8.1.1.</summary>
    public enum RuntimeSimulationPhase
    {
        CommandCollection = 1,
        MovementValidation = 2,
        PlayerCommands = 3,
        EnemySpawning = 4,
        EnemySimulation = 5,
        BossSimulation = 6,
        Abilities = 7,
        DeathAndRewards = 8,
        StateSnapshots = 9,
        EventFlush = 10
    }

    /// <summary>Explicit, phase-ordered callbacks owned by the composition provider.</summary>
    public sealed class RuntimeUpdateLoop : IDisposable
    {
        private readonly List<Entry> callbacks = new();
        private long nextSequence;
        private bool disposed;

        public IDisposable Add(Action<float> callback)
        {
            return Add(RuntimeSimulationPhase.Abilities, callback);
        }

        public IDisposable Add(RuntimeSimulationPhase phase, Action<float> callback)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            if (!Enum.IsDefined(typeof(RuntimeSimulationPhase), phase))
                throw new ArgumentOutOfRangeException(nameof(phase));
            ThrowIfDisposed();
            Entry entry = new(phase, nextSequence++, callback);
            callbacks.Add(entry);
            callbacks.Sort(Entry.Compare);
            return new Registration(() => callbacks.Remove(entry));
        }

        public void Tick(float deltaTime)
        {
            ThrowIfDisposed();
            float safeDelta = float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)
                ? 0f
                : Math.Max(0f, deltaTime);
            Entry[] snapshot = callbacks.ToArray();
            for (int i = 0; i < snapshot.Length; i++) snapshot[i].Callback(safeDelta);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            callbacks.Clear();
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(RuntimeUpdateLoop));
        }

        private sealed class Entry
        {
            public Entry(RuntimeSimulationPhase phase, long sequence, Action<float> callback)
            {
                Phase = phase;
                Sequence = sequence;
                Callback = callback;
            }

            public RuntimeSimulationPhase Phase { get; }
            public long Sequence { get; }
            public Action<float> Callback { get; }

            public static int Compare(Entry left, Entry right)
            {
                int phase = left.Phase.CompareTo(right.Phase);
                return phase != 0 ? phase : left.Sequence.CompareTo(right.Sequence);
            }
        }

        private sealed class Registration : IDisposable
        {
            private Action unregister;

            public Registration(Action unregister)
            {
                this.unregister = unregister;
            }

            public void Dispose()
            {
                Action current = unregister;
                unregister = null;
                current?.Invoke();
            }
        }
    }

    internal sealed class CompositionInstallation : IDisposable
    {
        private readonly List<IDisposable> disposables = new();
        private Action dispose;

        public void Add(IDisposable disposable)
        {
            if (disposable != null) disposables.Add(disposable);
        }

        public void OnDispose(Action action)
        {
            dispose += action;
        }

        public void Dispose()
        {
            for (int i = disposables.Count - 1; i >= 0; i--)
                disposables[i]?.Dispose();
            disposables.Clear();

            Action current = dispose;
            dispose = null;
            current?.Invoke();
        }
    }
}
