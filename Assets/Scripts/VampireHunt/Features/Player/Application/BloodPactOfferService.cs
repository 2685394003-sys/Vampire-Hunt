using System;
using System.Collections.Generic;
using VampireHunt.Core;
using VampireHunt.Player.Contracts;
using VampireHunt.Player.Domain;

namespace VampireHunt.Player.Application
{
    /// <summary>Server-owned Blood Pact offer/version and selection workflow.</summary>
    public sealed class BloodPactOfferService
    {
        private readonly IPlayerRepository repository;
        private readonly IBloodPactCatalog catalog;
        private readonly IPlayerRandom random;
        private readonly Dictionary<EntityId, BloodPactOffer> activeOffers = new();
        private readonly int choicesPerOffer;
        private uint nextOfferVersion;

        internal BloodPactOfferService(
            IPlayerRepository repository,
            IBloodPactCatalog catalog,
            IPlayerRandom random,
            int choicesPerOffer = 3,
            uint initialOfferVersion = 1)
        {
            this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            this.random = random ?? throw new ArgumentNullException(nameof(random));
            this.choicesPerOffer = Math.Max(1, choicesPerOffer);
            nextOfferVersion = initialOfferVersion == 0 ? 1u : initialOfferVersion;
        }

        public BloodPactOffer CreateOffer(EntityId playerId)
        {
            if (!repository.TryGet(playerId, out PlayerAggregate player) || !player.IsAlive)
                return default;

            List<BloodPactOption> options = new();
            catalog.CopyOptions(options);
            List<BloodPactOption> available = new();
            for (int i = 0; i < options.Count; i++)
            {
                BloodPactOption option = options[i];
                if (!option.Id.IsValid || player.BloodPacts.GetStacks(option.Id) >= option.MaximumStacks)
                    continue;
                available.Add(option);
            }

            int count = Math.Min(choicesPerOffer, available.Count);
            if (count == 0)
            {
                activeOffers.Remove(playerId);
                return default;
            }

            List<BloodPactId> choices = new(count);
            int cost = 0;
            for (int i = 0; i < count; i++)
            {
                int index = NextIndex(available.Count);
                BloodPactOption option = available[index];
                available.RemoveAt(index);
                choices.Add(option.Id);
                if (i == 0) cost = option.Cost;
            }

            uint version = NextVersion();
            BloodPactOffer offer = new(playerId, version, cost, choices);
            activeOffers[playerId] = offer;
            return offer;
        }

        public bool TryGetOffer(EntityId playerId, out BloodPactOffer offer) =>
            activeOffers.TryGetValue(playerId, out offer);

        public SelectionResult Select(EntityId playerId, BloodPactId selection, uint offerVersion)
        {
            if (!repository.TryGet(playerId, out PlayerAggregate player))
                return new SelectionResult(BloodPactSelectionCode.InvalidPlayer, selection, 0, 0, offerVersion);
            if (!player.IsAlive)
                return new SelectionResult(BloodPactSelectionCode.Dead, selection, player.Progression.Scarlet, 0, offerVersion);
            if (!activeOffers.TryGetValue(playerId, out BloodPactOffer offer) ||
                offer.OfferVersion != offerVersion)
            {
                return new SelectionResult(BloodPactSelectionCode.StaleOffer, selection, player.Progression.Scarlet, 0, offerVersion);
            }
            if (!offer.Contains(selection))
                return new SelectionResult(BloodPactSelectionCode.NotOffered, selection, player.Progression.Scarlet, 0, offerVersion);
            if (!catalog.TryGet(selection, out BloodPactOption option) || !option.Id.IsValid)
                return new SelectionResult(BloodPactSelectionCode.InvalidSelection, selection, player.Progression.Scarlet, 0, offerVersion);
            // BloodPactOffer exposes one authoritative price for the whole
            // offer. Do not silently switch to a catalog option's price here:
            // that would let the server charge a value different from the UI.
            int offerCost = offer.Cost;
            if (player.Progression.Scarlet < offerCost)
                return new SelectionResult(BloodPactSelectionCode.InsufficientScarlet, selection, player.Progression.Scarlet, 0, offerVersion);
            if (player.BloodPacts.GetStacks(selection) > 0 && !option.Repeatable)
                return new SelectionResult(BloodPactSelectionCode.AlreadyOwned, selection, player.Progression.Scarlet, player.BloodPacts.GetStacks(selection), offerVersion);
            if (player.BloodPacts.GetStacks(selection) >= option.MaximumStacks)
                return new SelectionResult(BloodPactSelectionCode.AlreadyOwned, selection, player.Progression.Scarlet, player.BloodPacts.GetStacks(selection), offerVersion);

            if (!player.Progression.SpendScarlet(offerCost) ||
                !player.BloodPacts.AddOrStack(selection, option.Repeatable, option.MaximumStacks))
            {
                // SpendScarlet is checked first, and this refund is only reached
                // if a concurrent/domain invariant failure prevents the add.
                player.Progression.AddScarlet(offerCost);
                return new SelectionResult(BloodPactSelectionCode.InvalidSelection, selection, player.Progression.Scarlet, 0, offerVersion);
            }

            int stacks = player.BloodPacts.GetStacks(selection);
            activeOffers.Remove(playerId); // A version is single-use.
            return new SelectionResult(BloodPactSelectionCode.Accepted, selection, player.Progression.Scarlet, stacks, offerVersion);
        }

        public SelectionResult Select(EntityId playerId, BloodPactId selection)
        {
            return activeOffers.TryGetValue(playerId, out BloodPactOffer offer)
                ? Select(playerId, selection, offer.OfferVersion)
                : new SelectionResult(BloodPactSelectionCode.StaleOffer, selection, 0, 0, 0);
        }

        private int NextIndex(int count)
        {
            if (count <= 1) return 0;
            int value = random.NextInt(0, count);
            return value < 0 ? 0 : value >= count ? count - 1 : value;
        }

        private uint NextVersion()
        {
            uint version = nextOfferVersion++;
            if (version == 0) version = nextOfferVersion++;
            if (nextOfferVersion == 0) nextOfferVersion = 1;
            return version;
        }
    }
}
