using System;
using System.Collections.Generic;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;
using VampireHunt.Core.Contracts;

namespace VampireHunt.Infrastructure.Integration
{
    /// <summary>
    /// Resolves capability ports by logical entity lifetime. The directory does
    /// not own the receivers; it only tracks registration and unregistration.
    /// </summary>
    public sealed class CombatEntityDirectoryAdapter : ICombatEntityDirectory
    {
        private readonly Dictionary<EntityId, IDamageReceiver> damageReceivers = new();
        private readonly Dictionary<EntityId, IHealingReceiver> healingReceivers = new();
        private readonly Dictionary<EntityId, IKnockbackReceiver> knockbackReceivers = new();
        private readonly HashSet<EntityId> registeredIds = new();
        private readonly IEntityLifecycleEventSink lifecycleSink;

        public CombatEntityDirectoryAdapter(IEntityLifecycleEventSink lifecycleSink = null)
        {
            this.lifecycleSink = lifecycleSink;
        }

        public int Count => registeredIds.Count;

        public void Register(
            EntityId id,
            IDamageReceiver damageReceiver = null,
            IHealingReceiver healingReceiver = null,
            IKnockbackReceiver knockbackReceiver = null)
        {
            ValidateId(id);
            if (damageReceiver == null && healingReceiver == null && knockbackReceiver == null)
            {
                throw new ArgumentException("At least one combat capability is required.", nameof(damageReceiver));
            }

            bool wasRegistered = registeredIds.Contains(id);
            RemoveCapabilities(id);
            if (damageReceiver != null) damageReceivers[id] = damageReceiver;
            if (healingReceiver != null) healingReceivers[id] = healingReceiver;
            if (knockbackReceiver != null) knockbackReceivers[id] = knockbackReceiver;
            registeredIds.Add(id);

            if (!wasRegistered) lifecycleSink?.EntitySpawned(id);
        }

        /// <summary>Registers all capability interfaces implemented by a single object.</summary>
        public void Register(EntityId id, object capabilities)
        {
            if (capabilities == null) throw new ArgumentNullException(nameof(capabilities));
            Register(
                id,
                capabilities as IDamageReceiver,
                capabilities as IHealingReceiver,
                capabilities as IKnockbackReceiver);
        }

        public bool Unregister(EntityId id)
        {
            if (!id.IsValid || !registeredIds.Remove(id)) return false;
            RemoveCapabilities(id);
            lifecycleSink?.EntityDespawned(id);
            return true;
        }

        public void Clear()
        {
            if (registeredIds.Count == 0) return;
            EntityId[] ids = new EntityId[registeredIds.Count];
            registeredIds.CopyTo(ids);
            for (int i = 0; i < ids.Length; i++) Unregister(ids[i]);
        }

        public IDamageReceiver TryGetDamageReceiver(EntityId id)
        {
            return id.IsValid && damageReceivers.TryGetValue(id, out IDamageReceiver receiver)
                ? receiver
                : null;
        }

        public IHealingReceiver TryGetHealingReceiver(EntityId id)
        {
            return id.IsValid && healingReceivers.TryGetValue(id, out IHealingReceiver receiver)
                ? receiver
                : null;
        }

        public IKnockbackReceiver TryGetKnockbackReceiver(EntityId id)
        {
            return id.IsValid && knockbackReceivers.TryGetValue(id, out IKnockbackReceiver receiver)
                ? receiver
                : null;
        }

        private void RemoveCapabilities(EntityId id)
        {
            damageReceivers.Remove(id);
            healingReceivers.Remove(id);
            knockbackReceivers.Remove(id);
        }

        private static void ValidateId(EntityId id)
        {
            if (!id.IsValid) throw new ArgumentException("A valid entity id is required.", nameof(id));
        }
    }
}
