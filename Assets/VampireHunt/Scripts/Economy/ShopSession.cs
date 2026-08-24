using System;
using System.Collections.Generic;

namespace VampireHunt.Economy
{
    public sealed class ShopOffer
    {
        public ulong OfferId { get; }
        public ShopProductDefinition Product { get; }
        public bool Purchased { get; internal set; }
        public bool PurchasePending { get; internal set; }

        internal ShopOffer(ulong offerId, ShopProductDefinition product)
        {
            OfferId = offerId;
            Product = product ?? throw new ArgumentNullException(nameof(product));
        }
    }

    /// <summary>Client-local state for one player's view of one world shop instance.</summary>
    public sealed class ShopSession
    {
        private readonly List<ShopOffer> m_Offers = new List<ShopOffer>();
        private ulong m_NextOfferId = 1;

        public ulong SessionId { get; }
        public ShopSessionKey Key { get; }
        public ShopDefinition Definition { get; }
        public IReadOnlyList<ShopOffer> Offers => m_Offers;
        public int RefreshCount { get; private set; }
        public bool RefreshPending { get; private set; }

        public ShopSession(
            ulong sessionId,
            ShopSessionKey key,
            ShopDefinition definition,
            ShopProductDefinition[] initialProducts)
        {
            SessionId = sessionId;
            Key = key;
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            ReplaceOffers(initialProducts);
        }

        public bool TryBeginPurchase(int index, out ShopOffer offer)
        {
            offer = index >= 0 && index < m_Offers.Count ? m_Offers[index] : null;
            if (offer == null || offer.Purchased || offer.PurchasePending || RefreshPending) return false;
            offer.PurchasePending = true;
            return true;
        }

        public void ResolvePurchase(ulong offerId, bool success)
        {
            ShopOffer offer = FindOffer(offerId);
            if (offer == null) return;
            offer.PurchasePending = false;
            if (success) offer.Purchased = true;
        }

        public bool TryBeginRefresh()
        {
            if (RefreshPending) return false;
            for (int i = 0; i < m_Offers.Count; i++)
                if (m_Offers[i].PurchasePending) return false;
            RefreshPending = true;
            return true;
        }

        public void ResolveRefresh(bool success, ShopProductDefinition[] products)
        {
            RefreshPending = false;
            if (!success) return;
            RefreshCount++;
            ReplaceOffers(products);
        }

        private ShopOffer FindOffer(ulong offerId)
        {
            for (int i = 0; i < m_Offers.Count; i++)
                if (m_Offers[i].OfferId == offerId) return m_Offers[i];
            return null;
        }

        private void ReplaceOffers(ShopProductDefinition[] products)
        {
            m_Offers.Clear();
            if (products == null) return;
            for (int i = 0; i < products.Length; i++)
                if (products[i] != null) m_Offers.Add(new ShopOffer(m_NextOfferId++, products[i]));
        }
    }
}
