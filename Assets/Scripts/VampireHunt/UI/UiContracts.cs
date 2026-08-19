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
}
