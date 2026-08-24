using Blocks.Gameplay.Core;
using UnityEngine;
using VampireHunt.Economy;

namespace VampireHunt.Infrastructure.Unity
{
    [System.Serializable]
    public struct ItemUsePresentationPayload
    {
        public int slotIndex;
        public uint itemId;
        public bool success;
        public ItemUseFailureReason failureReason;
        public double nextReadyServerTime;
    }

    [CreateAssetMenu(fileName = "ItemUsePresentationEvent", menuName = "Vampire Hunt/Items/Item Use Result Event")]
    public sealed class ItemUsePresentationEvent : GameEvent<ItemUsePresentationPayload> { }
}
