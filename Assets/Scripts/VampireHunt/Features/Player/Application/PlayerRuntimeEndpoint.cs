using System;
using System.Collections.Generic;
using VampireHunt.Combat.Contracts;
using VampireHunt.Core;
using VampireHunt.Player.Contracts;
using VampireHunt.Player.Domain;

namespace VampireHunt.Player.Application
{
    /// <summary>
    /// The Unity-facing Player application endpoint.  It owns the aggregate and
    /// delegates all mutations to Domain/Application services.  The type is
    /// intentionally internal: Unity adapters receive only IPlayerRuntimePort.
    /// </summary>
    internal sealed class PlayerRuntimeEndpoint :
        IPlayerRuntimePort,
        IPlayerAttackResultPort,
        IAuthoritativeDamageReceiver,
        IHealingReceiver,
        IDisposable
    {
        private readonly SinglePlayerRepository repository;
        private readonly PlayerAggregate aggregate;
        private readonly PlayerCommandService commands;
        private readonly PlayerProgressionService progression;
        private readonly BloodPactOfferService offers;
        private readonly PlayerCombatService combat;
        private readonly IPlayerDamageResolver damageResolver;
        private readonly IMovementClock clock;
        private readonly PlayerRuntimeValues values;
        private PlayerSnapshot snapshot;
        private bool disposed;

        internal PlayerRuntimeEndpoint(
            EntityId playerId,
            PlayerSpec spec,
            PlayerRuntimeValues values,
            IPlayerDamageResolver damageResolver = null,
            PlayerCombatService combat = null,
            BloodPactOfferService offers = null,
            IGameClock clock = null,
            IMeleeHitQuery hitQuery = null,
            IPlayerPositionQuery positions = null,
            IBloodPactCatalog bloodPactCatalog = null,
            IPlayerRandom random = null)
        {
            if (!playerId.IsValid) throw new ArgumentException("A valid player id is required.", nameof(playerId));
            this.values = values;
            this.damageResolver = damageResolver;
            this.combat = ResolveCombatService(
                combat,
                damageResolver,
                hitQuery,
                positions,
                values);
            this.clock = clock == null ? null : new GameClockAdapter(clock);
            aggregate = spec.CreateAggregate(playerId);
            repository = new SinglePlayerRepository(aggregate);
            progression = new PlayerProgressionService(repository);
            this.offers = ResolveBloodPactService(offers, repository, bloodPactCatalog, random);
            commands = new PlayerCommandService(
                repository,
                ownership: null,
                this.combat,
                progression,
                this.offers,
                this.clock);
            snapshot = aggregate.CreateSnapshot(Now);
        }

        public EntityId PlayerId => aggregate.Id;
        public PlayerSnapshot Snapshot => snapshot;
        public PlayerRuntimeValues Values => values;
        public int Health => snapshot.Health;
        public int MaxHealth => snapshot.MaxHealth;
        public float Stamina => snapshot.Stamina;
        public float MaxStamina => snapshot.MaxStamina;
        public int Scarlet => snapshot.Scarlet;
        public int BloodPactScarlet => snapshot.Scarlet;
        public int Coins => snapshot.Coins;
        public int Level => snapshot.Level;
        public int Experience => snapshot.Experience;
        public bool IsAlive => snapshot.IsAlive;
        public bool IsBloodPactPlayerAlive => snapshot.IsAlive;

        public event Action<PlayerSnapshot> SnapshotChanged;
        public event Action<IGameplayEvent> GameplayEventProduced;

        public CommandResult SubmitDash(DashCommand command)
        {
            ThrowIfDisposed();
            CommandResult result = commands.Handle(command);
            PublishSnapshot();
            return result;
        }

        public CommandResult SubmitAttack(AttackCommand command)
        {
            ThrowIfDisposed();
            CommandResult result = commands.Handle(command);
            PublishSnapshot();
            return result;
        }

        public CommandResult SelectBloodPact(SelectBloodPactCommand command)
        {
            ThrowIfDisposed();
            CommandResult result = commands.Handle(command);
            PublishSnapshot();
            return result;
        }

        public AttackResult ExecuteAttack(AttackCommand command)
        {
            ThrowIfDisposed();
            if (combat == null)
                return new AttackResult(false, "Combat service is unavailable", 0, null);

            // AttackResult is exposed only as an application result.  The
            // command gateway remains the normal intent path for input/RPC.
            AttackResult result = combat.Attack(aggregate, command, Now);
            PublishSnapshot();
            return result;
        }

        public bool TryCreateBloodPactOffer(out BloodPactOffer offer)
        {
            ThrowIfDisposed();
            if (offers == null)
            {
                offer = default;
                return false;
            }

            offer = offers.CreateOffer(PlayerId);
            return offer.IsValid;
        }

        public bool TryGetBloodPactOffer(out BloodPactOffer offer)
        {
            ThrowIfDisposed();
            offer = default;
            return offers != null && offers.TryGetOffer(PlayerId, out offer) && offer.IsValid;
        }

        public bool TryGetBloodPactStacks(BloodPactId id, out int stacks)
        {
            ThrowIfDisposed();
            stacks = aggregate.BloodPacts.GetStacks(id);
            return id.IsValid;
        }

        public PlayerDamageResult ApplyDamage(PlayerDamageCommand command)
        {
            ThrowIfDisposed();
            if (command.TargetId != PlayerId || !aggregate.IsAlive || command.Amount <= 0 || damageResolver == null)
                return PlayerDamageResult.Rejected(command);

            bool wasInvincible = aggregate.Vitals.IsInvincibleAt(Now);
            if (wasInvincible)
                return PlayerDamageResult.Invincible(command);

            DamageResult result = damageResolver.Apply(new PlayerAttackRequest(
                command.SourceId,
                command.TargetId,
                command.Amount,
                command.HitPosition,
                command.HitPosition));
            PublishSnapshot();
            return new PlayerDamageResult(result, true, false);
        }

        /// <summary>
        /// Combat directory receiver. CombatApplicationService calls this
        /// capability after resolving an attack; it must write the aggregate
        /// directly and never call CombatApplicationService again.
        /// </summary>
        public DamageResult ApplyDamage(in ResolvedDamage damage)
        {
            ThrowIfDisposed();
            return ApplyResolvedDamage(in damage, Now);
        }

        public DamageResult ApplyDamage(in ResolvedDamage damage, double authoritativeNow)
        {
            ThrowIfDisposed();
            return ApplyResolvedDamage(in damage, authoritativeNow);
        }

        private DamageResult ApplyResolvedDamage(in ResolvedDamage damage, double authoritativeNow)
        {
            if (damage.TargetId != PlayerId || !aggregate.IsAlive)
                return DamageResult.NoDamage(damage.FinalDamage, damage.Hit.Position);

            DamageResult result = aggregate.Vitals.ApplyDamage(in damage, authoritativeNow);
            PublishSnapshot();
            return result;
        }

        public int ApplyHealing(int amount)
        {
            ThrowIfDisposed();
            if (!aggregate.IsAlive || amount <= 0) return 0;
            int applied = aggregate.Vitals.ApplyHealing(amount);
            PublishSnapshot();
            return Math.Max(0, applied);
        }

        public bool ForceDeath(EntityId killerId = default)
        {
            ThrowIfDisposed();
            if (!aggregate.IsAlive) return false;
            if (aggregate.Vitals.CurrentHealth <= 0) return false;

            // Encounter/admin death is an explicit domain transition.  It is
            // not a synthetic hit and does not emit a damage result.
            aggregate.Vitals.ForceDeath();
            PublishSnapshot();
            return !aggregate.IsAlive;
        }

        public bool RestoreToFull()
        {
            ThrowIfDisposed();
            if (!aggregate.IsAlive) return false;
            aggregate.Vitals.RestoreToFull();
            aggregate.MobilityState.Reset();
            PublishSnapshot();
            return true;
        }

        public bool TryConsumeStamina(float amount)
        {
            ThrowIfDisposed();
            if (amount < 0f || float.IsNaN(amount) || float.IsInfinity(amount) ||
                aggregate.MobilityState.Stamina + 0.0001f < amount)
                return false;

            // Dash legality is owned by PlayerMobilityState.  This helper is
            // retained for old input adapters but does not decide cooldowns.
            if (!aggregate.MobilityState.TrySpendStamina(amount)) return false;
            PublishSnapshot();
            return true;
        }

        public bool RestoreStamina(float amount)
        {
            ThrowIfDisposed();
            if (amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return false;
            // Recovery is a domain operation; the adapter only forwards the
            // request and never changes a cooldown or invincibility rule.
            if (!aggregate.MobilityState.RestoreStamina(amount)) return false;
            PublishSnapshot();
            return true;
        }

        public bool AddScarlet(int amount)
        {
            ThrowIfDisposed();
            if (amount <= 0) return false;
            aggregate.Progression.AddScarlet(amount);
            PublishSnapshot();
            return true;
        }

        public bool TrySpendScarlet(int amount)
        {
            ThrowIfDisposed();
            bool spent = amount >= 0 && aggregate.Progression.SpendScarlet(amount);
            if (spent) PublishSnapshot();
            return spent;
        }

        public bool AddCoins(int amount)
        {
            ThrowIfDisposed();
            if (amount <= 0) return false;
            aggregate.Progression.AddCoins(amount);
            PublishSnapshot();
            return true;
        }

        public bool TrySpendCoins(int amount)
        {
            ThrowIfDisposed();
            if (amount < 0 || aggregate.Progression.Coins < amount) return false;
            bool spent = aggregate.Progression.SpendCoins(amount);
            if (spent) PublishSnapshot();
            return spent;
        }

        public bool GrantReward(RewardGrant reward)
        {
            ThrowIfDisposed();
            bool granted = progression.GrantReward(PlayerId, reward);
            if (granted) PublishSnapshot();
            return granted;
        }

        public bool TryApplyModifier(PlayerModifierCommand modifier)
        {
            ThrowIfDisposed();
            if (!Enum.IsDefined(typeof(PlayerStat), (int)modifier.Stat) ||
                !Enum.IsDefined(typeof(PlayerStatModifierOperation), (int)modifier.Operation))
                return false;

            bool applied = aggregate.RunStats.AddModifier(new PlayerStatModifier(
                modifier.ModifierId,
                modifier.SourceId,
                (PlayerStat)modifier.Stat,
                (PlayerStatModifierOperation)modifier.Operation,
                modifier.Magnitude,
                modifier.Stacks,
                modifier.MaxStacks));
            if (applied) PublishSnapshot();
            return applied;
        }

        public bool RemoveModifier(string modifierId)
        {
            ThrowIfDisposed();
            bool removed = aggregate.RunStats.RemoveModifier(modifierId);
            if (removed) PublishSnapshot();
            return removed;
        }

        public int RemoveModifiersFromSource(string sourceId)
        {
            ThrowIfDisposed();
            int removed = aggregate.RunStats.RemoveBySource(sourceId);
            if (removed > 0) PublishSnapshot();
            return removed;
        }

        public bool ResetForNewRun()
        {
            ThrowIfDisposed();
            aggregate.ResetForRespawn();
            aggregate.Progression.Reset();
            aggregate.BloodPacts.Clear();
            aggregate.RunStats.ResetModifiers();
            PublishSnapshot();
            return true;
        }

        public bool IsInvincibleAt(double now)
        {
            ThrowIfDisposed();
            return aggregate.Vitals.IsInvincibleAt(now);
        }

        public void Tick(float deltaTime)
        {
            ThrowIfDisposed();
            if (deltaTime <= 0f || !aggregate.IsAlive) return;
            aggregate.MobilityState.Recover(deltaTime);
            PublishSnapshot();
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            SnapshotChanged = null;
            GameplayEventProduced = null;
        }

        private double Now => clock?.Now ?? 0d;

        private static PlayerCombatService ResolveCombatService(
            PlayerCombatService supplied,
            IPlayerDamageResolver damageResolver,
            IMeleeHitQuery hitQuery,
            IPlayerPositionQuery positions,
            PlayerRuntimeValues values)
        {
            if (supplied != null) return supplied;
            if (damageResolver == null && hitQuery == null && positions == null) return null;
            if (damageResolver == null || hitQuery == null || positions == null)
            {
                throw new ArgumentException(
                    "Player combat requires a damage resolver, melee hit query and player position query.");
            }

            // The endpoint owns the repository/aggregate. Constructing this
            // service here prevents Bootstrap from accidentally wiring a
            // PlayerCombatService against a different repository instance.
            return new PlayerCombatService(
                hitQuery,
                damageResolver,
                positions,
                values.AttackRange,
                values.AttackConeAngle);
        }

        private static BloodPactOfferService ResolveBloodPactService(
            BloodPactOfferService supplied,
            IPlayerRepository repository,
            IBloodPactCatalog catalog,
            IPlayerRandom random)
        {
            if (supplied != null) return supplied;
            if (catalog == null && random == null) return null;
            if (catalog == null || random == null)
            {
                throw new ArgumentException(
                    "Blood Pact offers require a catalog and random source.");
            }

            // Keep offer state attached to this endpoint's aggregate. A
            // composition root supplies only authoring/catalog ports and does
            // not need to construct a service around a hidden repository.
            return new BloodPactOfferService(repository, catalog, random);
        }

        private void PublishSnapshot()
        {
            PlayerSnapshot next = aggregate.CreateSnapshot(Now);
            if (SnapshotsMatch(snapshot, next)) return;
            snapshot = next;
            SnapshotChanged?.Invoke(snapshot);
        }

        private static bool SnapshotsMatch(PlayerSnapshot left, PlayerSnapshot right)
        {
            if (left.Id != right.Id || left.CurrentHealth != right.CurrentHealth ||
                left.MaxHealth != right.MaxHealth || left.IsAlive != right.IsAlive ||
                left.IsInvincibleWindow != right.IsInvincibleWindow ||
                !left.Stamina.Equals(right.Stamina) || !left.MaxStamina.Equals(right.MaxStamina) ||
                left.Scarlet != right.Scarlet || left.Coins != right.Coins ||
                left.Level != right.Level || left.Experience != right.Experience ||
                left.LastCommandSequence != right.LastCommandSequence ||
                left.IsAttacking != right.IsAttacking ||
                !left.AttackWindowEndsAt.Equals(right.AttackWindowEndsAt) ||
                left.BloodPacts.Count != right.BloodPacts.Count)
                return false;

            for (int i = 0; i < left.BloodPacts.Count; i++)
                if (!left.BloodPacts[i].Equals(right.BloodPacts[i])) return false;
            return true;
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(PlayerRuntimeEndpoint));
        }

        private sealed class SinglePlayerRepository : IPlayerRepository
        {
            private readonly PlayerAggregate player;

            public SinglePlayerRepository(PlayerAggregate player) => this.player = player;

            public PlayerAggregate Get(EntityId playerId) => TryGet(playerId, out PlayerAggregate value) ? value : null;

            public bool TryGet(EntityId playerId, out PlayerAggregate value)
            {
                value = playerId == player.Id ? player : null;
                return value != null;
            }

            public void GetAllAlive(ICollection<PlayerAggregate> buffer)
            {
                if (buffer != null && player.IsAlive) buffer.Add(player);
            }
        }

        private sealed class GameClockAdapter : IMovementClock
        {
            private readonly IGameClock clock;
            public GameClockAdapter(IGameClock clock) => this.clock = clock;
            public double Now => clock.Now;
        }
    }

    /// <summary>
    /// Internal factory used by Bootstrap/Integration composition.  It keeps
    /// aggregate construction out of Assembly-CSharp while exposing only the
    /// Contracts port to legacy components.
    /// </summary>
    internal static class PlayerRuntimeEndpointFactory
    {
        /// <summary>
        /// Bootstrap-friendly overload. Authoring has already been reduced to
        /// PlayerSpec, so the composition root never rereads a ScriptableObject
        /// to reconstruct adapter-only values.
        /// </summary>
        internal static IPlayerRuntimePort Create(
            EntityId playerId,
            PlayerSpec spec,
            IPlayerDamageResolver damageResolver = null,
            PlayerCombatService combat = null,
            BloodPactOfferService offers = null,
            IGameClock clock = null,
            IMeleeHitQuery hitQuery = null,
            IPlayerPositionQuery positions = null,
            IBloodPactCatalog bloodPactCatalog = null,
            IPlayerRandom random = null)
        {
            return Create(
                playerId,
                spec,
                PlayerRuntimeValuesFactory.Create(spec),
                damageResolver,
                combat,
                offers,
                clock,
                hitQuery,
                positions,
                bloodPactCatalog,
                random);
        }

        internal static IPlayerRuntimePort Create(
            EntityId playerId,
            PlayerSpec spec,
            PlayerRuntimeValues values,
            IPlayerDamageResolver damageResolver = null,
            PlayerCombatService combat = null,
            BloodPactOfferService offers = null,
            IGameClock clock = null,
            IMeleeHitQuery hitQuery = null,
            IPlayerPositionQuery positions = null,
            IBloodPactCatalog bloodPactCatalog = null,
            IPlayerRandom random = null)
        {
            return new PlayerRuntimeEndpoint(
                playerId,
                spec,
                values,
                damageResolver,
                combat,
                offers,
                clock,
                hitQuery,
                positions,
                bloodPactCatalog,
                random);
        }
    }

    internal static class PlayerRuntimeValuesFactory
    {
        internal static PlayerRuntimeValues Create(PlayerSpec spec) =>
            new(
                (float)spec.DashStaminaCost,
                spec.StaminaRecoveryPerSecond,
                spec.GetBaseValue(PlayerStat.MoveSpeed),
                (float)spec.DashSpeedMultiplier,
                (float)spec.DashDuration,
                spec.GetBaseValue(PlayerStat.AttackRange),
                spec.AttackConeAngle,
                spec.GetBaseValue(PlayerStat.KnockbackForce),
                spec.KnockbackDuration,
                spec.StunDuration);
    }
}
