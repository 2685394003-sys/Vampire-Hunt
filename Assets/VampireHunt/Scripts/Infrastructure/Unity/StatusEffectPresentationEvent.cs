using Blocks.Gameplay.Core;
using UnityEngine;

namespace VampireHunt.Infrastructure.Unity
{
    [CreateAssetMenu(fileName = "StatusEffectPresentationEvent", menuName = "Vampire Hunt/Events/Status Effect Presentation Event")]
    public sealed class StatusEffectPresentationEvent : GameEvent<StatusEffectPresentationPayload> { }
}
