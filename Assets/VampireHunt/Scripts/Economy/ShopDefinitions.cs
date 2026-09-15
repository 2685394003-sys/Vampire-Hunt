using System;

namespace VampireHunt.Economy
{
    public enum ShopTransactionKind : byte
    {
        Purchase = 1,
        Refresh = 2
    }

    public enum ShopTransactionFailureReason : byte
    {
        None = 0,
        InvalidRequest = 1,
        ProductUnavailable = 2,
        InsufficientCoin = 3,
        InventoryRejected = 4,
        DuplicateTransaction = 5
    }

    public sealed class ShopProductDefinition
    {
        public uint ProductId { get; }
        public string DisplayName { get; }
        public uint ItemId { get; }
        public ItemKind ItemKind { get; }
        public int Quantity { get; }
        public int BasePrice { get; }
        public float RollWeight { get; }

        public ShopProductDefinition(
            uint productId,
            string displayName,
            uint itemId,
            ItemKind itemKind,
            int quantity,
            int basePrice,
            float rollWeight)
        {
            if (productId == 0) throw new ArgumentOutOfRangeException(nameof(productId));
            if (itemId == 0) throw new ArgumentOutOfRangeException(nameof(itemId));
            ProductId = productId;
            DisplayName = displayName ?? string.Empty;
            ItemId = itemId;
            ItemKind = itemKind;
            Quantity = Math.Max(1, quantity);
            BasePrice = Math.Max(0, basePrice);
            RollWeight = Math.Max(0.0001f, rollWeight);
        }
    }

    public sealed class ShopDefinition
    {
        public uint ShopId { get; }
        public string DisplayName { get; }
        public int OfferCount { get; }
        public int RefreshPrice { get; }
        public bool AllowDuplicateProducts { get; }
        public ShopProductDefinition[] Products { get; }

        public ShopDefinition(
            uint shopId,
            string displayName,
            int offerCount,
            int refreshPrice,
            bool allowDuplicateProducts,
            ShopProductDefinition[] products)
        {
            if (shopId == 0) throw new ArgumentOutOfRangeException(nameof(shopId));
            ShopId = shopId;
            DisplayName = displayName ?? string.Empty;
            OfferCount = Math.Max(1, offerCount);
            RefreshPrice = Math.Max(0, refreshPrice);
            AllowDuplicateProducts = allowDuplicateProducts;
            Products = products ?? Array.Empty<ShopProductDefinition>();
        }
    }

    public readonly struct ShopSessionKey : IEquatable<ShopSessionKey>
    {
        public uint ShopId { get; }
        public ulong ShopInstanceId { get; }

        public ShopSessionKey(uint shopId, ulong shopInstanceId)
        {
            ShopId = shopId;
            ShopInstanceId = shopInstanceId;
        }

        public bool Equals(ShopSessionKey other) =>
            ShopId == other.ShopId && ShopInstanceId == other.ShopInstanceId;

        public override bool Equals(object obj) => obj is ShopSessionKey other && Equals(other);
        public override int GetHashCode() => unchecked((int)ShopId * 397 ^ ShopInstanceId.GetHashCode());
    }

    public readonly struct ShopTransactionResult
    {
        public ulong TransactionId { get; }
        public ShopTransactionKind Kind { get; }
        public uint ProductId { get; }
        public ShopTransactionFailureReason FailureReason { get; }
        public int RemainingCoin { get; }
        public bool Success => FailureReason == ShopTransactionFailureReason.None;

        public ShopTransactionResult(
            ulong transactionId,
            ShopTransactionKind kind,
            uint productId,
            ShopTransactionFailureReason failureReason,
            int remainingCoin)
        {
            TransactionId = transactionId;
            Kind = kind;
            ProductId = productId;
            FailureReason = failureReason;
            RemainingCoin = Math.Max(0, remainingCoin);
        }
    }
}
