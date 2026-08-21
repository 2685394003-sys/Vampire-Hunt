using System.IO;
using NUnit.Framework;

namespace VampireHunt.Tests.Architecture
{
    public sealed class PresentationBootstrapContractTests
    {
        [Test]
        public void PresentationCoordinator_UsesExplicitBindings_AndReleasesAllAdapters()
        {
            string source = File.ReadAllText(
                "Assets/Scripts/VampireHunt/Bootstrap/DefaultCompositionFactoryProvider.cs");

            Assert.That(source, Does.Contain("IRuntimeGameplayEventStreamBinding"));
            Assert.That(source, Does.Contain("IRuntimeCameraBinding"));
            Assert.That(source, Does.Contain("AudioCuePresenter presenter = new(events, audioDriver)"));
            Assert.That(source, Does.Contain("streamBinding.Bind(null)"));
            Assert.That(source, Does.Contain("cameraBinding.BindCamera(null)"));
            Assert.That(source, Does.Contain("SceneManager.sceneLoaded += HandleSceneChanged"));
            Assert.That(source, Does.Contain("SceneManager.sceneUnloaded += HandleSceneUnloaded"));
            Assert.That(source, Does.Contain("ClearBindings()"));
        }

        [Test]
        public void PresentationAdapters_ExposeCompositionOwnedEventAndCameraSeams()
        {
            string combatText = File.ReadAllText("Assets/Scripts/UI/CombatTextService.cs");
            string spriteSorting = File.ReadAllText("Assets/Scripts/PerspectiveSpriteSorting.cs");
            string cameraLock = File.ReadAllText("Assets/Scripts/Camera/CameraSpriteViewLock.cs");

            Assert.That(combatText, Does.Contain("IRuntimeGameplayEventStreamBinding"));
            Assert.That(combatText, Does.Contain("IRuntimeCameraBinding"));
            Assert.That(spriteSorting, Does.Contain("IRuntimeCameraBinding"));
            Assert.That(cameraLock, Does.Contain("IRuntimeCameraBinding"));
        }
    }
}
