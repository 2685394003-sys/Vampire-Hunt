using System;
using VampireHunt.Player.Contracts;
using VampireHunt.UI.Contracts;

namespace VampireHunt.UI
{
    /// <summary>Renders a read-only projection; it has no path to domain state.</summary>
    public sealed class PlayerHudPresenter
    {
        private readonly Func<IPlayerReadModel> modelProvider;
        private readonly IPlayerHudView view;

        public PlayerHudPresenter(IPlayerReadModel model, IPlayerHudView view)
            : this(() => model, view)
        {
        }

        public PlayerHudPresenter(Func<IPlayerReadModel> modelProvider, IPlayerHudView view)
        {
            this.modelProvider = modelProvider ?? throw new ArgumentNullException(nameof(modelProvider));
            this.view = view ?? throw new ArgumentNullException(nameof(view));
        }

        public void Render()
        {
            IPlayerReadModel model = modelProvider();
            if (model != null) view.Render(model);
        }
    }
}
