using NUnit.Framework;
using VampireHunt.Core;
using VampireHunt.Player.Contracts;
using VampireHunt.UI;
using VampireHunt.UI.Contracts;

namespace VampireHunt.Tests.UI
{
    public sealed class UiPresenterTests
    {
        [Test]
        public void PlayerHudPresenter_RendersReadModelWithoutReachingDomainState()
        {
            RecordingHudView view = new RecordingHudView();
            FakePlayerReadModel model = new FakePlayerReadModel
            {
                Health = 42,
                MaxHealth = 100,
                Stamina = 3f,
                MaxStamina = 10f,
                Scarlet = 8,
                Coins = 4,
                Level = 2,
                Experience = 12,
                IsAlive = true
            };
            PlayerHudPresenter presenter = new PlayerHudPresenter(model, view);

            presenter.Render();

            Assert.That(view.RenderCount, Is.EqualTo(1));
            Assert.That(view.LastModel, Is.SameAs(model));
            Assert.That(view.LastModel.Health, Is.EqualTo(42));
        }

        [Test]
        public void BloodPactSelectionPresenter_SubmitsOnlyIdAndVersion_AndHidesOnlyWhenAccepted()
        {
            RecordingBloodPactView view = new RecordingBloodPactView();
            RecordingCommandGateway commands = new RecordingCommandGateway();
            BloodPactSelectionPresenter presenter = new BloodPactSelectionPresenter(view, commands);
            EntityId playerId = new EntityId(6);
            BloodPactId selection = new BloodPactId("swift");
            BloodPactOffer offer = new BloodPactOffer(
                playerId,
                17u,
                10,
                new[] { selection, new BloodPactId("guard") });

            presenter.ShowOffer(offer);
            commands.NextResult = new CommandResult(CommandResultStatus.StaleOffer, "stale");
            CommandResult rejected = presenter.Select(selection);

            Assert.That(rejected.Accepted, Is.False);
            Assert.That(presenter.HasOffer, Is.True);
            Assert.That(commands.LastCommand.PlayerId, Is.EqualTo(playerId));
            Assert.That(commands.LastCommand.Selection, Is.EqualTo(selection));
            Assert.That(commands.LastCommand.OfferVersion, Is.EqualTo(17u));
            Assert.That(commands.LastCommand.Sequence, Is.EqualTo(1u));
            Assert.That(commands.LastCommand.Selection, Is.EqualTo(selection));
            Assert.That(view.HideCount, Is.Zero);

            commands.NextResult = CommandResult.Accept();
            CommandResult accepted = presenter.Select(selection);

            Assert.That(accepted.Accepted, Is.True);
            Assert.That(presenter.HasOffer, Is.False);
            Assert.That(view.HideCount, Is.EqualTo(1));
        }

        [Test]
        public void BloodPactSelectionPresenter_DoesNotInventSelectionWhenOfferIsMissing()
        {
            RecordingBloodPactView view = new RecordingBloodPactView();
            RecordingCommandGateway commands = new RecordingCommandGateway();
            BloodPactSelectionPresenter presenter = new BloodPactSelectionPresenter(view, commands);

            CommandResult result = presenter.Select(new BloodPactId("swift"));

            Assert.That(result.Status, Is.EqualTo(CommandResultStatus.StaleOffer));
            Assert.That(commands.CallCount, Is.Zero);
        }

        private sealed class RecordingHudView : IPlayerHudView
        {
            public int RenderCount { get; private set; }
            public IPlayerReadModel LastModel { get; private set; }
            public void Render(IPlayerReadModel model)
            {
                RenderCount++;
                LastModel = model;
            }
        }

        private sealed class RecordingBloodPactView : IBloodPactSelectionView
        {
            public int ShowCount { get; private set; }
            public int HideCount { get; private set; }
            public BloodPactOffer LastOffer { get; private set; }
            public void ShowOffer(BloodPactOffer offer)
            {
                ShowCount++;
                LastOffer = offer;
            }

            public void Hide() => HideCount++;
        }

        private sealed class RecordingCommandGateway : IPlayerCommandGateway
        {
            public int CallCount { get; private set; }
            public SelectBloodPactCommand LastCommand { get; private set; }
            public CommandResult NextResult { get; set; } = CommandResult.Reject(CommandResultStatus.Rejected);

            public CommandResult SubmitDash(DashCommand command) => CommandResult.Reject(CommandResultStatus.Rejected);
            public CommandResult SubmitAttack(AttackCommand command) => CommandResult.Reject(CommandResultStatus.Rejected);
            public CommandResult SelectBloodPact(SelectBloodPactCommand command)
            {
                CallCount++;
                LastCommand = command;
                return NextResult;
            }
        }

        private sealed class FakePlayerReadModel : IPlayerReadModel
        {
            public int Health { get; set; }
            public int MaxHealth { get; set; }
            public float Stamina { get; set; }
            public float MaxStamina { get; set; }
            public int Scarlet { get; set; }
            public int Coins { get; set; }
            public int Level { get; set; }
            public int Experience { get; set; }
            public bool IsAlive { get; set; }
        }
    }
}
