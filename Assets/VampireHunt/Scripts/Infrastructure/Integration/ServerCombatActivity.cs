using Unity.Netcode;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Netcode.Player;
using VampireHunt.SharedKernel;

namespace VampireHunt.Infrastructure.Integration
{
    /// <summary>Called only from accepted server actions/contacts, before damage mitigation.</summary>
    public static class ServerCombatActivity
    {
        public static PlayerCombatStateHost Resolve(NetworkManager manager, EntityId player)
        {
            if (manager == null || !manager.IsServer || player.IsNone ||
                !manager.ConnectedClients.TryGetValue(player.Value - 1, out var client) || client.PlayerObject == null)
                return null;
            return client.PlayerObject.GetComponent<PlayerCombatStateHost>();
        }

        public static void Action(NetworkManager manager, EntityId source, uint action, ulong sequence)
        {
            Resolve(manager, source)?.RecordActivityServer(CombatActivityKind.Action, source.Value, 0, action, sequence);
        }

        public static void Interaction(NetworkManager manager, EntityId source, EntityId target,
            uint action, ulong sequence, CombatActivityKind kind = CombatActivityKind.Interaction)
        {
            if (source.IsNone || target.IsNone || source == target) return;
            var attacker = Resolve(manager, source);
            var victim = Resolve(manager, target);
            attacker?.RecordActivityServer(kind, source.Value, target.Value, action, sequence);
            if (victim != attacker) victim?.RecordActivityServer(kind, source.Value, target.Value, action, sequence);
        }

        public static void Support(NetworkManager manager, EntityId source, EntityId target, uint action, ulong sequence)
        {
            var recipient = Resolve(manager, target);
            if (recipient != null && recipient.State == PlayerCombatState.Combat)
                Resolve(manager, source)?.RecordActivityServer(CombatActivityKind.Support, source.Value, target.Value, action, sequence);
        }
    }
}
