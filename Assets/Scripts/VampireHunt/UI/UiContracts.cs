using System;
using VampireHunt.Player.Contracts;

namespace VampireHunt.UI.Contracts
{
    public interface IPlayerHudView
    {
        void Render(IPlayerReadModel model);
    }

    public interface IBloodPactSelectionView
    {
        void ShowOffer(BloodPactOffer offer);
        void Hide();
    }

    /// <summary>Optional view seam used by Bootstrap to attach the presenter.</summary>
    public interface IBloodPactSelectionPresenterBinding
    {
        void Bind(VampireHunt.UI.BloodPactSelectionPresenter presenter);
    }

    /// <summary>
    /// Optional view seam for composition-owned runtime mode. UI uses this
    /// only to decide whether opening a modal may pause the local simulation.
    /// </summary>
    public interface INetworkActivityProviderBinding
    {
        void SetNetworkActiveProvider(Func<bool> provider);
    }
}
