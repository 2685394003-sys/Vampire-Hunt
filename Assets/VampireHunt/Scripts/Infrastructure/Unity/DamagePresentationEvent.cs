using Blocks.Gameplay.Core;
using UnityEngine;

namespace VampireHunt.Infrastructure.Unity
{
    [CreateAssetMenu(fileName = "DamagePresentationEvent", menuName = "Vampire Hunt/Events/Damage Presentation Event")]
    public sealed class DamagePresentationEvent : GameEvent<DamagePresentationPayload> { }
}
