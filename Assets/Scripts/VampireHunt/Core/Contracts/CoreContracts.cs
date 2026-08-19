using VampireHunt.Core;

namespace VampireHunt.Core.Contracts
{
    /// <summary>
    /// Covariance-free state ingress: replication owns transport while each
    /// feature owns its immutable snapshot type and specialized sink name.
    /// </summary>
    public interface IStateSnapshotSink<in TSnapshot>
    {
        void Apply(TSnapshot snapshot);
    }

    /// <summary>Ingress used by a replication adapter to push an authoritative event.</summary>
    public interface IGameplayEventIngress
    {
        void Push(IGameplayEvent @event);
    }

    /// <summary>Lifecycle notification used by a registry/view adapter.</summary>
    public interface IEntityLifecycleEventSink
    {
        void EntitySpawned(EntityId id);
        void EntityDespawned(EntityId id);
    }
}
