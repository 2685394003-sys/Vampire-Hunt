using VampireHunt.Core;
using UnityEngine;
using EntityId = VampireHunt.Core.EntityId;

/// <summary>
/// Transitional adapter identity source for legacy NetworkSpawnUtility pools.
/// The authoritative NetworkObjectPool uses its injected allocator; this
/// source only keeps old prefab callbacks from reusing an EntityId while those
/// assets are being migrated.
/// </summary>
internal static class EnemyLegacyEntityIds
{
    public static EntityIdAllocator Source { get; } = new EntityIdAllocator();

    public static EntityId Allocate() => Source.Allocate();

    public static EntityId Resolve(GameObject value)
    {
        if (value == null) return Allocate();
        PlayerNetworkState player = value.GetComponentInParent<PlayerNetworkState>();
        if (player != null && player.LogicalPlayerId.IsValid) return player.LogicalPlayerId;
        EnemyHealth enemy = value.GetComponentInParent<EnemyHealth>();
        if (enemy != null && enemy.Runtime != null && enemy.Runtime.Id.IsValid)
            return enemy.Runtime.Id;
        BossController boss = value.GetComponentInParent<BossController>();
        if (boss != null && boss.RuntimeEntityId.IsValid) return boss.RuntimeEntityId;
        return Allocate();
    }
}
