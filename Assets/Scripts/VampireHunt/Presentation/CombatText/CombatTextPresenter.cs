using System;
using System.Collections.Generic;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;
using VampireHunt.Presentation.Contracts;
using VampireHunt.Presentation.Runtime;

namespace VampireHunt.Presentation.CombatText
{
    /// <summary>
    /// Presents server-confirmed damage/healing only. It keeps source/target
    /// EntityIds in the command and never requires a pooled view to still be
    /// registered when an event arrives.
    /// </summary>
    public sealed class CombatTextPresenter : IDisposable
    {
        private readonly ICombatTextPool pool;
        private readonly Queue<ICombatTextHandle> active = new Queue<ICombatTextHandle>();
        private readonly PresentationEventTracker eventTracker = new PresentationEventTracker();
        private readonly IDisposable subscription;
        private bool disposed;

        public CombatTextPresenter(IGameplayEventStream events, ICombatTextPool pool)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            this.pool = pool ?? throw new ArgumentNullException(nameof(pool));
            subscription = events.Subscribe(HandleEvent);
        }

        public int DroppedCount { get; private set; }
        public int ActiveCount => pool.ActiveCount;
        public int Capacity => pool.Capacity;

        public void Show(in CombatTextCommand command)
        {
            if (disposed) return;
            CleanupInactive();
            if (!pool.TryRent(out ICombatTextHandle handle))
            {
                RecycleOldest();
                if (!pool.TryRent(out handle))
                {
                    DroppedCount++;
                    return;
                }
            }

            handle.Show(command);
            active.Enqueue(handle);
        }

        public void Release(ICombatTextHandle handle)
        {
            if (handle == null) return;
            pool.Return(handle);
            CleanupInactive();
        }

        public void RecycleAll()
        {
            while (active.Count > 0) pool.Return(active.Dequeue());
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            subscription.Dispose();
            RecycleAll();
        }

        private void HandleEvent(IGameplayEvent @event)
        {
            if (!eventTracker.Accept(@event)) return;
            if (@event is DamageConfirmedEvent damage)
            {
                if (damage.Result.AppliedDamage <= 0) return;
                Show(new CombatTextCommand(
                    damage.SourceId,
                    damage.TargetId,
                    damage.Result.AppliedDamage,
                    damage.Result.WasCritical,
                    false,
                    damage.Result.HitPosition,
                    damage.Flags,
                    damage.EventId,
                    damage.OccurredAt));
                return;
            }

            if (@event is HealingConfirmedEvent healing)
            {
                if (healing.Result.AppliedHealing <= 0) return;
                Show(new CombatTextCommand(
                    healing.SourceId,
                    healing.TargetId,
                    healing.Result.AppliedHealing,
                    false,
                    true,
                    VampireHunt.Core.WorldPosition.Origin,
                    DamageFlags.None,
                    healing.EventId,
                    healing.OccurredAt));
            }
        }

        private void RecycleOldest()
        {
            CleanupInactive();
            if (active.Count == 0) return;
            pool.Return(active.Dequeue());
        }

        private void CleanupInactive()
        {
            int count = active.Count;
            for (int i = 0; i < count; i++)
            {
                ICombatTextHandle handle = active.Dequeue();
                if (handle != null && handle.IsActive) active.Enqueue(handle);
            }
        }
    }

    /// <summary>
    /// Engine-neutral local pool useful for tests and for a Unity adapter that
    /// wraps concrete TMP views. It enforces a hard active cap.
    /// </summary>
    public sealed class InMemoryCombatTextPool : ICombatTextPool
    {
        private readonly HashSet<InMemoryCombatTextHandle> active = new HashSet<InMemoryCombatTextHandle>();
        private readonly Queue<InMemoryCombatTextHandle> available = new Queue<InMemoryCombatTextHandle>();
        private readonly int capacity;

        public InMemoryCombatTextPool(int capacity, int prewarm = 0)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            this.capacity = capacity;
            for (int i = 0; i < Math.Min(capacity, Math.Max(0, prewarm)); i++)
                available.Enqueue(new InMemoryCombatTextHandle());
        }

        public int Capacity => capacity;
        public int ActiveCount => active.Count;

        public bool TryRent(out ICombatTextHandle handle)
        {
            if (active.Count >= capacity)
            {
                handle = null;
                return false;
            }

            InMemoryCombatTextHandle concrete = available.Count > 0
                ? available.Dequeue()
                : new InMemoryCombatTextHandle();
            concrete.SetActive(true);
            active.Add(concrete);
            handle = concrete;
            return true;
        }

        public void Return(ICombatTextHandle handle)
        {
            if (!(handle is InMemoryCombatTextHandle concrete)) return;
            if (!active.Remove(concrete)) return;
            concrete.Reset();
            available.Enqueue(concrete);
        }
    }

    public sealed class InMemoryCombatTextHandle : ICombatTextHandle
    {
        public bool IsActive { get; private set; }
        public CombatTextCommand LastCommand { get; private set; }

        public void Show(CombatTextCommand command)
        {
            LastCommand = command;
            IsActive = true;
        }

        public void Reset()
        {
            IsActive = false;
            LastCommand = default(CombatTextCommand);
        }

        internal void SetActive(bool active) => IsActive = active;
    }
}
