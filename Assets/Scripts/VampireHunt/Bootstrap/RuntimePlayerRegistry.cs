using System;
using System.Collections.Generic;
using UnityEngine;
using VampireHunt.Core;
using VampireHunt.Infrastructure.Netcode.Contracts;
using VampireHunt.Player.Application;
using VampireHunt.Player.Contracts;
using EntityId = VampireHunt.Core.EntityId;

namespace VampireHunt.Bootstrap
{
    /// <summary>
    /// Composition-owned directory for the logical lifetime of players.  It is
    /// both the offline command endpoint and the sender-aware NGO endpoint, so
    /// both modes enter the same Player application facade.
    /// </summary>
    public sealed class RuntimePlayerRegistry :
        IPlayerCommandHandler,
        IOwnedPlayerCommandEndpoint,
        INetworkCommandOwnership,
        IPlayerPositionQuery,
        IPlayerProgressionCommands,
        IDisposable
    {
        private readonly Dictionary<EntityId, Entry> entries = new();
        private bool disposed;

        public int Count => entries.Count;
        public event Action<IPlayerRuntimePort, ulong> Registered;
        public event Action<IPlayerRuntimePort> Unregistered;

        public void Register(
            IPlayerRuntimePort runtime,
            Transform transform,
            ulong ownerClientId = 0UL)
        {
            ThrowIfDisposed();
            if (runtime == null) throw new ArgumentNullException(nameof(runtime));
            if (!runtime.PlayerId.IsValid)
                throw new ArgumentException("A registered player requires a valid EntityId.", nameof(runtime));
            if (transform == null) throw new ArgumentNullException(nameof(transform));
            if (entries.ContainsKey(runtime.PlayerId))
                throw new InvalidOperationException($"Player {runtime.PlayerId} is already registered.");
            entries.Add(runtime.PlayerId, new Entry(runtime, transform, ownerClientId));
            Registered?.Invoke(runtime, ownerClientId);
        }

        public bool Unregister(EntityId playerId, out IPlayerRuntimePort runtime)
        {
            if (!entries.TryGetValue(playerId, out Entry entry))
            {
                runtime = null;
                return false;
            }

            entries.Remove(playerId);
            runtime = entry.Runtime;
            Unregistered?.Invoke(runtime);
            return true;
        }

        public bool TryGet(EntityId playerId, out IPlayerRuntimePort runtime)
        {
            if (entries.TryGetValue(playerId, out Entry entry))
            {
                runtime = entry.Runtime;
                return true;
            }
            runtime = null;
            return false;
        }

        public bool TryGetFirst(out IPlayerRuntimePort runtime)
        {
            foreach (Entry entry in entries.Values)
            {
                runtime = entry.Runtime;
                return true;
            }
            runtime = null;
            return false;
        }

        public bool Owns(ulong senderId, EntityId playerId) =>
            entries.TryGetValue(playerId, out Entry entry) && entry.OwnerClientId == senderId;

        public bool TryGetPosition(EntityId playerId, out WorldPosition position)
        {
            if (entries.TryGetValue(playerId, out Entry entry) && entry.Transform != null)
            {
                Vector3 value = entry.Transform.position;
                position = new WorldPosition(value.x, value.y, value.z);
                return true;
            }
            position = WorldPosition.Origin;
            return false;
        }

        public int CopyAlivePositions(ICollection<WorldPosition> buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            foreach (Entry entry in entries.Values)
            {
                if (!entry.Runtime.IsAlive || entry.Transform == null) continue;
                Vector3 value = entry.Transform.position;
                buffer.Add(new WorldPosition(value.x, value.y, value.z));
            }
            return buffer.Count;
        }

        public CommandResult Handle(DashCommand command) => RouteUnchecked(command.PlayerId, r => r.SubmitDash(command), command.Sequence);
        public CommandResult Handle(AttackCommand command) => RouteUnchecked(command.PlayerId, r => r.SubmitAttack(command), command.Sequence);
        public CommandResult Handle(SelectBloodPactCommand command) => RouteUnchecked(command.PlayerId, r => r.SelectBloodPact(command), command.Sequence);

        public bool GrantReward(EntityId recipientId, RewardGrant reward) =>
            entries.TryGetValue(recipientId, out Entry entry) && entry.Runtime.GrantReward(reward);

        public int GrantSharedScarlet(int amount)
        {
            if (amount <= 0) return 0;
            List<IPlayerRuntimePort> alive = new();
            foreach (Entry entry in entries.Values)
                if (entry.Runtime.IsAlive) alive.Add(entry.Runtime);
            if (alive.Count == 0) return 0;

            int share = amount / alive.Count;
            int remainder = amount % alive.Count;
            for (int i = 0; i < alive.Count; i++)
                alive[i].AddScarlet(share + (i < remainder ? 1 : 0));
            return amount;
        }

        public CommandResult Route(ulong senderId, DashCommand command) =>
            RouteOwned(senderId, command.PlayerId, r => r.SubmitDash(command), command.Sequence);

        public CommandResult Route(ulong senderId, AttackCommand command) =>
            RouteOwned(senderId, command.PlayerId, r => r.SubmitAttack(command), command.Sequence);

        public CommandResult Route(ulong senderId, SelectBloodPactCommand command) =>
            RouteOwned(senderId, command.PlayerId, r => r.SelectBloodPact(command), command.Sequence);

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            entries.Clear();
            Registered = null;
            Unregistered = null;
        }

        private CommandResult RouteOwned(
            ulong senderId,
            EntityId playerId,
            Func<IPlayerRuntimePort, CommandResult> route,
            uint sequence)
        {
            if (!entries.TryGetValue(playerId, out Entry entry))
                return new CommandResult(CommandResultStatus.NotFound, "Player was not found", sequence);
            if (entry.OwnerClientId != senderId)
                return new CommandResult(CommandResultStatus.Unauthorized, "Sender does not own player", sequence);
            return route(entry.Runtime);
        }

        private CommandResult RouteUnchecked(
            EntityId playerId,
            Func<IPlayerRuntimePort, CommandResult> route,
            uint sequence)
        {
            ThrowIfDisposed();
            return entries.TryGetValue(playerId, out Entry entry)
                ? route(entry.Runtime)
                : new CommandResult(CommandResultStatus.NotFound, "Player was not found", sequence);
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(RuntimePlayerRegistry));
        }

        private readonly struct Entry
        {
            public Entry(IPlayerRuntimePort runtime, Transform transform, ulong ownerClientId)
            {
                Runtime = runtime;
                Transform = transform;
                OwnerClientId = ownerClientId;
            }

            public IPlayerRuntimePort Runtime { get; }
            public Transform Transform { get; }
            public ulong OwnerClientId { get; }
        }
    }

    internal sealed class RuntimeBloodPactCatalog : IBloodPactCatalog
    {
        private readonly Dictionary<BloodPactId, BloodPactOption> options = new();

        public RuntimeBloodPactCatalog(IReadOnlyList<BloodPactOption> source)
        {
            if (source == null) return;
            for (int i = 0; i < source.Count; i++)
            {
                BloodPactOption option = source[i];
                if (!option.Id.IsValid)
                    throw new ArgumentException("Blood Pact option ids must be valid.", nameof(source));
                if (!options.TryAdd(option.Id, option))
                    throw new ArgumentException($"Duplicate Blood Pact option '{option.Id}'.", nameof(source));
            }
        }

        public int Count => options.Count;

        public bool TryGet(BloodPactId id, out BloodPactOption option) => options.TryGetValue(id, out option);

        public void CopyOptions(ICollection<BloodPactOption> buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            foreach (BloodPactOption option in options.Values) buffer.Add(option);
        }
    }

    internal sealed class PlayerRandomAdapter : IPlayerRandom
    {
        private readonly IRandomSource random;

        public PlayerRandomAdapter(IRandomSource random)
        {
            this.random = random ?? throw new ArgumentNullException(nameof(random));
        }

        public int NextInt(int minimumInclusive, int maximumExclusive) =>
            random.NextInt(minimumInclusive, maximumExclusive);
    }
}
