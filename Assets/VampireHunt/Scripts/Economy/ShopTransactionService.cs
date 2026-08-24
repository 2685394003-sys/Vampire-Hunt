using System;
using VampireHunt.Contracts;

namespace VampireHunt.Economy
{
    /// <summary>Server-side transaction boundary for purchases and paid shop refreshes.</summary>
    public sealed class ShopTransactionService
    {
        private readonly ICurrencyWallet m_Wallet;
        private readonly IPlayerItemInventory m_Inventory;

        public ShopTransactionService(ICurrencyWallet wallet, IPlayerItemInventory inventory)
        {
            m_Wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            m_Inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
        }

        public ShopTransactionFailureReason TryPurchase(ShopProductDefinition product)
        {
            if (product == null) return ShopTransactionFailureReason.ProductUnavailable;
            bool canGrant = product.ItemKind switch
            {
                ItemKind.Usable => m_Inventory.CanGrantUsable(product.ItemId, product.Quantity),
                ItemKind.Accessory => m_Inventory.CanGrantAccessory(product.ItemId, product.Quantity),
                _ => false
            };
            if (!canGrant) return ShopTransactionFailureReason.InventoryRejected;
            if (!m_Wallet.TrySpend(product.BasePrice))
                return ShopTransactionFailureReason.InsufficientCoin;

            bool granted = product.ItemKind switch
            {
                ItemKind.Usable => m_Inventory.TryGrantUsable(product.ItemId, product.Quantity),
                ItemKind.Accessory => m_Inventory.TryGrantAccessory(product.ItemId, product.Quantity),
                _ => false
            };
            if (granted) return ShopTransactionFailureReason.None;

            // The inventory preflight and commit execute on the same server thread. Keep a refund as
            // a safety net for configuration drift or a future mutation introduced between them.
            m_Wallet.Credit(product.BasePrice);
            return ShopTransactionFailureReason.InventoryRejected;
        }

        public ShopTransactionFailureReason TryRefresh(int price)
        {
            if (price < 0) return ShopTransactionFailureReason.InvalidRequest;
            return m_Wallet.TrySpend(price)
                ? ShopTransactionFailureReason.None
                : ShopTransactionFailureReason.InsufficientCoin;
        }

        public int Balance => m_Wallet.Balance;
    }
}
