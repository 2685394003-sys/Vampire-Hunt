using System;
using System.Collections.Generic;
using UnityEngine;
using VampireHunt.Economy;

namespace VampireHunt.Infrastructure.Unity
{
    [CreateAssetMenu(fileName = "ShopCatalog", menuName = "Vampire Hunt/Shop/Shop Catalog")]
    public sealed class ShopCatalogAsset : ScriptableObject
    {
        [SerializeField] private ShopDefinitionAsset[] shops = Array.Empty<ShopDefinitionAsset>();

        private readonly Dictionary<uint, ShopDefinitionAsset> m_Shops =
            new Dictionary<uint, ShopDefinitionAsset>();
        private readonly Dictionary<uint, ShopProductDefinitionAsset> m_Products =
            new Dictionary<uint, ShopProductDefinitionAsset>();
        private bool m_Initialized;

        public bool TryGetShopAsset(uint shopId, out ShopDefinitionAsset shop)
        {
            Initialize();
            return m_Shops.TryGetValue(shopId, out shop);
        }

        public bool TryGetShop(uint shopId, out ShopDefinition shop)
        {
            shop = null;
            if (!TryGetShopAsset(shopId, out ShopDefinitionAsset asset)) return false;
            shop = asset.ToDomain();
            return true;
        }

        public bool TryGetProductAsset(uint productId, out ShopProductDefinitionAsset product)
        {
            Initialize();
            return m_Products.TryGetValue(productId, out product);
        }

        public bool TryGetProduct(uint productId, out ShopProductDefinition product)
        {
            product = null;
            if (!TryGetProductAsset(productId, out ShopProductDefinitionAsset asset)) return false;
            product = asset.ToDomain();
            return product != null;
        }

        private void Initialize()
        {
            if (m_Initialized) return;
            m_Initialized = true;
            m_Shops.Clear();
            m_Products.Clear();
            if (shops == null) return;
            for (int i = 0; i < shops.Length; i++)
            {
                ShopDefinitionAsset shop = shops[i];
                if (shop == null) continue;
                if (!m_Shops.TryAdd(shop.ShopId, shop))
                    Debug.LogError($"[ShopCatalogAsset] Duplicate ShopId {shop.ShopId}.", shop);
                ShopProductDefinitionAsset[] products = shop.Products;
                for (int j = 0; j < products.Length; j++)
                {
                    ShopProductDefinitionAsset product = products[j];
                    if (product == null) continue;
                    if (m_Products.TryGetValue(product.ProductId, out ShopProductDefinitionAsset existing) &&
                        existing != product)
                        Debug.LogError($"[ShopCatalogAsset] Duplicate ProductId {product.ProductId}.", product);
                    else m_Products[product.ProductId] = product;
                }
            }
        }

        private void OnValidate() => m_Initialized = false;
    }
}
