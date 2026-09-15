using UnityEngine;
using VampireHunt.Economy;

namespace VampireHunt.Infrastructure.Unity
{
    [CreateAssetMenu(fileName = "ShopProduct", menuName = "Vampire Hunt/Shop/Product")]
    public sealed class ShopProductDefinitionAsset : ScriptableObject
    {
        [SerializeField, Min(1)] private uint productId = 1;
        [SerializeField] private string displayName;
        [SerializeField] private ItemDefinitionAsset item;
        [SerializeField, Min(1)] private int quantity = 1;
        [SerializeField, Min(0)] private int basePrice = 10;
        [SerializeField, Min(0.0001f)] private float rollWeight = 1f;

        public uint ProductId => productId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName)
            ? item != null ? item.DisplayName : string.Empty
            : displayName;
        public ItemDefinitionAsset Item => item;
        public int Quantity => quantity;
        public int BasePrice => basePrice;
        public float RollWeight => rollWeight;

        public ShopProductDefinition ToDomain()
        {
            return item == null
                ? null
                : new ShopProductDefinition(
                    productId,
                    DisplayName,
                    item.ItemId,
                    item.Kind,
                    quantity,
                    basePrice,
                    rollWeight);
        }

        private void OnValidate()
        {
            if (productId == 0) productId = 1;
            quantity = Mathf.Max(1, quantity);
            basePrice = Mathf.Max(0, basePrice);
            rollWeight = Mathf.Max(0.0001f, rollWeight);
        }
    }
}
