using Blocks.Gameplay.Core;
using UnityEngine;

namespace VampireHunt.Infrastructure.Unity
{
    [CreateAssetMenu(fileName = "ItemSlotUseEvent", menuName = "Vampire Hunt/Items/Item Slot Use Event")]
    public sealed class ItemSlotUseEvent : GameEvent<int> { }
}
