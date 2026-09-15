using System;
using System.Collections.Generic;
using UnityEngine;
using VampireHunt.Economy;

namespace VampireHunt.Infrastructure.Unity
{
    [CreateAssetMenu(fileName = "ShopDefinition", menuName = "Vampire Hunt/Shop/Shop Definition")]
    public sealed class ShopDefinitionAsset : ScriptableObject
    {
        [SerializeField, Min(1)] private uint shopId = 1;
        [SerializeField] private string displayName = "Shop";
        [SerializeField, Range(1, 3)] private int offerCount = 3;
        [SerializeField, Min(0)] private int refreshPrice = 5;
        [SerializeField] private bool allowDuplicateProducts;
        [SerializeField] private ShopProductDefinitionAsset[] products =
            Array.Empty<ShopProductDefinitionAsset>();

        public uint ShopId => shopId;
        public string DisplayName => displayName;
        public int OfferCount => offerCount;
        public int RefreshPrice => refreshPrice;
        public bool AllowDuplicateProducts => allowDuplicateProducts;
        public ShopProductDefinitionAsset[] Products => products ?? Array.Empty<ShopProductDefinitionAsset>();

        public ShopDefinition ToDomain()
        {
            var definitions = new List<ShopProductDefinition>();
            ShopProductDefinitionAsset[] source = Products;
            for (int i = 0; i < source.Length; i++)
            {
                ShopProductDefinition definition = source[i] != null ? source[i].ToDomain() : null;
                if (definition != null) definitions.Add(definition);
            }
            return new ShopDefinition(
                shopId,
                displayName,
                offerCount,
                refreshPrice,
                allowDuplicateProducts,
                definitions.ToArray());
        }

        private void OnValidate()
        {
            if (shopId == 0) shopId = 1;
            offerCount = Mathf.Clamp(offerCount, 1, 3);
            refreshPrice = Mathf.Max(0, refreshPrice);
        }
    }
}
