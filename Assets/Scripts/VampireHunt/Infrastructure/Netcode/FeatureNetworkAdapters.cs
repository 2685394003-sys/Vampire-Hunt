using System;
using VampireHunt.Boss.Contracts;
using VampireHunt.Core;
using VampireHunt.Enemies.Contracts;
using VampireHunt.Infrastructure.Netcode.Contracts;
using VampireHunt.Player.Contracts;

namespace VampireHunt.Infrastructure.Netcode
{
    /// <summary>Network-facing Player command adapter.</summary>
    public sealed class PlayerNetworkAdapter : IPlayerCommandGateway
    {
        private readonly NetworkCommandRouter router;
        public PlayerNetworkAdapter(NetworkCommandRouter router, ulong clientId = 0UL)
        {
            this.router = router ?? throw new ArgumentNullException(nameof(router));
            ClientId = clientId;
        }

        public ulong ClientId { get; }

        public CommandResult SubmitDash(DashCommand command) => router.Route(ClientId, command);
        public CommandResult SubmitAttack(AttackCommand command) => router.Route(ClientId, command);
        public CommandResult SelectBloodPact(SelectBloodPactCommand command) => router.Route(ClientId, command);

    }

    /// <summary>Network adapter for server enemy ticks; snapshots are handled separately.</summary>
    public sealed class EnemyNetworkAdapter
    {
        private readonly IEnemySimulationEndpoint simulation;

        public EnemyNetworkAdapter(IEnemySimulationEndpoint simulation)
        {
            this.simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
        }

        public bool Tick(EntityId enemyId, float deltaTime) => simulation.Tick(enemyId, Math.Max(0f, deltaTime));
    }

    /// <summary>Network adapter for the boss simulation facade.</summary>
    public sealed class BossNetworkAdapter
    {
        private readonly IBossSimulationEndpoint simulation;

        public BossNetworkAdapter(IBossSimulationEndpoint simulation)
        {
            this.simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
        }

        public void Tick(EntityId bossId, float deltaTime) => simulation.Tick(bossId, Math.Max(0f, deltaTime));
    }

    /// <summary>Offline adapter that still enters the same command handler.</summary>
    public sealed class LocalRuntimeAdapter : IPlayerCommandGateway
    {
        private readonly IPlayerCommandHandler handler;

        public LocalRuntimeAdapter(IPlayerCommandHandler handler)
        {
            this.handler = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        public CommandResult SubmitDash(DashCommand command) => handler.Handle(command);
        public CommandResult SubmitAttack(AttackCommand command) => handler.Handle(command);
        public CommandResult SelectBloodPact(SelectBloodPactCommand command) => handler.Handle(command);
    }
}
