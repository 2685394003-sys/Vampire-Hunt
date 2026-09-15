using UnityEngine;
using VampireHunt.Economy;

namespace VampireHunt.Infrastructure.Unity
{
    public abstract class ItemDefinitionAsset : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField, Min(1)] private uint itemId = 1;
        [SerializeField] private string displayName = "New Item";
        [SerializeField, TextArea(2, 5)] private string description;
        [SerializeField] private Sprite icon;

        public uint ItemId => itemId;
        public string DisplayName => displayName;
        public string Description => description;
        public Sprite Icon => icon;
        public abstract ItemKind Kind { get; }

        public abstract ItemDefinition ToDomain();

        protected virtual void OnValidate()
        {
            if (itemId == 0) itemId = 1;
        }
    }
}
