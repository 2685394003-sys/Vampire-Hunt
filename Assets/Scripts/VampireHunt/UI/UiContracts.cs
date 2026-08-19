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
}
