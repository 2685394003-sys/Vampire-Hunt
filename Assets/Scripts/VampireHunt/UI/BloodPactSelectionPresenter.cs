using System;
using VampireHunt.Player.Contracts;
using VampireHunt.UI.Contracts;

namespace VampireHunt.UI
{
    /// <summary>
    /// Shows a server-provided offer and submits only the selected id/version.
    /// Cost, ownership, and legality remain server Application rules.
    /// </summary>
    public sealed class BloodPactSelectionPresenter
    {
        private readonly IBloodPactSelectionView view;
        private readonly IPlayerCommandGateway commands;
        private BloodPactOffer currentOffer;
        private bool hasOffer;
        private uint nextSequence;

        public BloodPactSelectionPresenter(
            IBloodPactSelectionView view,
            IPlayerCommandGateway commands)
        {
            this.view = view ?? throw new ArgumentNullException(nameof(view));
            this.commands = commands ?? throw new ArgumentNullException(nameof(commands));
        }

        public bool HasOffer => hasOffer;
        public BloodPactOffer CurrentOffer => currentOffer;

        public void ShowOffer(BloodPactOffer offer)
        {
            if (!offer.IsValid)
            {
                Hide();
                return;
            }

            currentOffer = offer;
            hasOffer = true;
            view.ShowOffer(offer);
        }

        public CommandResult Select(BloodPactId selection)
        {
            if (!hasOffer)
            {
                return new CommandResult(CommandResultStatus.StaleOffer, "No active offer", nextSequence);
            }

            nextSequence = nextSequence == uint.MaxValue ? 1u : nextSequence + 1u;
            CommandResult result = commands.SelectBloodPact(new SelectBloodPactCommand(
                currentOffer.PlayerId,
                selection,
                currentOffer.OfferVersion,
                nextSequence));
            if (result.Accepted) Hide();
            return result;
        }

        public void Hide()
        {
            hasOffer = false;
            currentOffer = default(BloodPactOffer);
            view.Hide();
        }
    }
}
