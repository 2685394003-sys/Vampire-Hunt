using Unity.Netcode;
using VampireHunt.Boss.Contracts;
using VampireHunt.Enemies.Contracts;
using VampireHunt.Infrastructure.Netcode.Contracts;
using VampireHunt.Player.Contracts;

namespace VampireHunt.Infrastructure.Netcode
{
    /// <summary>
    /// Server-to-client state transport for the three immutable state DTOs.
    /// Each wire value is version/discriminator checked before the existing
    /// per-feature replicator applies sequence ordering and reset semantics.
    /// </summary>
    public sealed class NetworkStateRpcAdapter : NetworkBehaviour
    {
        private PlayerStateReplicator playerReplicator;
        private EnemyStateReplicator enemyReplicator;
        private BossStateReplicator bossReplicator;
        private IPlayerStateSnapshotSink playerSink;
        private IEnemyStateSnapshotSink enemySink;
        private IBossStateSnapshotSink bossSink;

        public bool IsConfigured =>
            playerReplicator != null && playerSink != null &&
            enemyReplicator != null && enemySink != null &&
            bossReplicator != null && bossSink != null;

        public void Configure(
            PlayerStateReplicator playerStateReplicator,
            IPlayerStateSnapshotSink playerSnapshotSink,
            EnemyStateReplicator enemyStateReplicator,
            IEnemyStateSnapshotSink enemySnapshotSink,
            BossStateReplicator bossStateReplicator,
            IBossStateSnapshotSink bossSnapshotSink)
        {
            playerReplicator = playerStateReplicator ?? throw new System.ArgumentNullException(nameof(playerStateReplicator));
            playerSink = playerSnapshotSink ?? throw new System.ArgumentNullException(nameof(playerSnapshotSink));
            enemyReplicator = enemyStateReplicator ?? throw new System.ArgumentNullException(nameof(enemyStateReplicator));
            enemySink = enemySnapshotSink ?? throw new System.ArgumentNullException(nameof(enemySnapshotSink));
            bossReplicator = bossStateReplicator ?? throw new System.ArgumentNullException(nameof(bossStateReplicator));
            bossSink = bossSnapshotSink ?? throw new System.ArgumentNullException(nameof(bossSnapshotSink));
        }

        public bool BroadcastPlayer(PlayerStateDto dto)
        {
            if (!IsServer || !IsConfigured || !PlayerStateWire.TryFrom(dto, out PlayerStateWire wire)) return false;
            PublishPlayerRpc(wire);
            return true;
        }

        public bool BroadcastEnemy(EnemyStateDto dto)
        {
            if (!IsServer || !IsConfigured || !EnemyStateWire.TryFrom(dto, out EnemyStateWire wire)) return false;
            PublishEnemyRpc(wire);
            return true;
        }

        public bool BroadcastBoss(BossStateDto dto)
        {
            if (!IsServer || !IsConfigured || !BossStateWire.TryFrom(dto, out BossStateWire wire)) return false;
            PublishBossRpc(wire);
            return true;
        }

        [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Unreliable)]
        private void PublishPlayerRpc(PlayerStateWire wire)
        {
            if (!wire.TryToDto(out PlayerStateDto dto) || playerReplicator == null || playerSink == null) return;
            playerReplicator.ApplyNetworkState(dto, playerSink);
        }

        [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Unreliable)]
        private void PublishEnemyRpc(EnemyStateWire wire)
        {
            if (!wire.TryToDto(out EnemyStateDto dto) || enemyReplicator == null || enemySink == null) return;
            enemyReplicator.ApplyNetworkState(dto, enemySink);
        }

        [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Unreliable)]
        private void PublishBossRpc(BossStateWire wire)
        {
            if (!wire.TryToDto(out BossStateDto dto) || bossReplicator == null || bossSink == null) return;
            bossReplicator.ApplyNetworkState(dto, bossSink);
        }
    }
}
