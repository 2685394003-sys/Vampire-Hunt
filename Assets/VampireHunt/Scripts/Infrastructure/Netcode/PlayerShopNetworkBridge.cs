using System;
using System.Collections.Generic;
using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Economy;
using VampireHunt.Infrastructure.Integration;
using VampireHunt.Infrastructure.Unity;

namespace VampireHunt.Infrastructure.Netcode
{
    /// <summary>
    /// Keeps each player's offers local while committing Coin and inventory mutations on the server.
    /// The server trusts that the requested catalog product was offered, but resolves product contents
    /// and prices from the shared catalog and deduplicates transaction IDs.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(PlayerInventoryNetworkState))]
    [RequireComponent(typeof(CurrencyWalletAdapter))]
    public sealed class PlayerShopNetworkBridge : NetworkBehaviour, IShopInteractionGateway
    {
        [SerializeField] private ShopCatalogAsset catalog;
        [SerializeField] private PlayerInventoryNetworkState inventory;
        [SerializeField] private CurrencyWalletAdapter wallet;
        [SerializeField] private int runSeed = 1337;

        private readonly Dictionary<ShopSessionKey, ShopSession> m_Sessions =
            new Dictionary<ShopSessionKey, ShopSession>();
        private readonly Dictionary<ulong, PendingTransaction> m_Pending =
            new Dictionary<ulong, PendingTransaction>();
        private readonly HashSet<ulong> m_ServerProcessedTransactions = new HashSet<ulong>();
        private readonly ShopRollService m_RollService = new ShopRollService();
        private ShopTransactionService m_TransactionService;
        private ulong m_NextSessionId = 1;
        private ulong m_NextTransactionId = 1;

        public event Action<ShopSession> ShopOpened;
        public event Action<ShopSession> ShopChanged;
        public event Action ShopClosed;
        public event Action<ShopTransactionResult> TransactionResolved;

        public ShopSession CurrentSession { get; private set; }
        public int CoinBalance => wallet != null ? wallet.Balance : 0;

        private sealed class PendingTransaction
        {
            public ShopSession Session;
            public ShopTransactionKind Kind;
            public ulong OfferId;
            public uint ProductId;
        }

        private void Awake()
        {
            if (inventory == null) inventory = GetComponent<PlayerInventoryNetworkState>();
            if (wallet == null) wallet = GetComponent<CurrencyWalletAdapter>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer)
            {
                m_ServerProcessedTransactions.Clear();
                m_TransactionService = new ShopTransactionService(wallet, inventory);
            }
        }

        public override void OnNetworkDespawn()
        {
            CurrentSession = null;
            m_Sessions.Clear();
            m_Pending.Clear();
            m_ServerProcessedTransactions.Clear();
            m_TransactionService = null;
            base.OnNetworkDespawn();
        }

        public bool OpenShop(uint shopId, ulong shopInstanceId)
        {
            if (!IsSpawned || !IsOwner || catalog == null ||
                !catalog.TryGetShop(shopId, out ShopDefinition definition)) return false;

            var key = new ShopSessionKey(shopId, shopInstanceId);
            if (!m_Sessions.TryGetValue(key, out ShopSession session))
            {
                ShopProductDefinition[] products = m_RollService.Roll(
                    definition,
                    BuildRollSeed(key, 0));
                session = new ShopSession(m_NextSessionId++, key, definition, products);
                m_Sessions.Add(key, session);
            }

            CurrentSession = session;
            ShopOpened?.Invoke(session);
            ShopChanged?.Invoke(session);
            return true;
        }

        public void CloseShop()
        {
            if (!IsOwner || CurrentSession == null) return;
            CurrentSession = null;
            ShopClosed?.Invoke();
        }

        public bool Purchase(int offerIndex)
        {
            ShopSession session = CurrentSession;
            if (!IsOwner || session == null || !session.TryBeginPurchase(offerIndex, out ShopOffer offer))
                return false;

            ulong transactionId = m_NextTransactionId++;
            m_Pending.Add(transactionId, new PendingTransaction
            {
                Session = session,
                Kind = ShopTransactionKind.Purchase,
                OfferId = offer.OfferId,
                ProductId = offer.Product.ProductId
            });
            ShopChanged?.Invoke(session);
            RequestPurchaseRpc(transactionId, offer.Product.ProductId);
            return true;
        }

        public bool Refresh()
        {
            ShopSession session = CurrentSession;
            if (!IsOwner || session == null || !session.TryBeginRefresh()) return false;

            ulong transactionId = m_NextTransactionId++;
            m_Pending.Add(transactionId, new PendingTransaction
            {
                Session = session,
                Kind = ShopTransactionKind.Refresh
            });
            ShopChanged?.Invoke(session);
            RequestRefreshRpc(transactionId, session.Definition.ShopId);
            return true;
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestPurchaseRpc(ulong transactionId, uint productId)
        {
            if (!TryBeginServerTransaction(transactionId, ShopTransactionKind.Purchase, productId)) return;
            ShopTransactionFailureReason failure = catalog != null &&
                catalog.TryGetProduct(productId, out ShopProductDefinition product) &&
                m_TransactionService != null
                    ? m_TransactionService.TryPurchase(product)
                    : ShopTransactionFailureReason.ProductUnavailable;
            ResolveTransactionRpc(
                transactionId,
                (byte)ShopTransactionKind.Purchase,
                productId,
                (byte)failure,
                m_TransactionService != null ? m_TransactionService.Balance : 0);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void RequestRefreshRpc(ulong transactionId, uint shopId)
        {
            if (!TryBeginServerTransaction(transactionId, ShopTransactionKind.Refresh, 0)) return;
            ShopTransactionFailureReason failure = catalog != null &&
                catalog.TryGetShop(shopId, out ShopDefinition shop) &&
                m_TransactionService != null
                    ? m_TransactionService.TryRefresh(shop.RefreshPrice)
                    : ShopTransactionFailureReason.ProductUnavailable;
            ResolveTransactionRpc(
                transactionId,
                (byte)ShopTransactionKind.Refresh,
                0,
                (byte)failure,
                m_TransactionService != null ? m_TransactionService.Balance : 0);
        }

        private bool TryBeginServerTransaction(
            ulong transactionId,
            ShopTransactionKind kind,
            uint productId)
        {
            if (transactionId != 0 && m_ServerProcessedTransactions.Add(transactionId)) return true;
            ResolveTransactionRpc(
                transactionId,
                (byte)kind,
                productId,
                (byte)ShopTransactionFailureReason.DuplicateTransaction,
                m_TransactionService != null ? m_TransactionService.Balance : 0);
            return false;
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void ResolveTransactionRpc(
            ulong transactionId,
            byte kindValue,
            uint productId,
            byte failureValue,
            int remainingCoin)
        {
            var kind = (ShopTransactionKind)kindValue;
            var failure = (ShopTransactionFailureReason)failureValue;
            if (m_Pending.TryGetValue(transactionId, out PendingTransaction pending))
            {
                m_Pending.Remove(transactionId);
                if (pending.Kind == ShopTransactionKind.Purchase)
                {
                    pending.Session.ResolvePurchase(pending.OfferId, failure == ShopTransactionFailureReason.None);
                }
                else
                {
                    ShopProductDefinition[] products = failure == ShopTransactionFailureReason.None
                        ? m_RollService.Roll(
                            pending.Session.Definition,
                            BuildRollSeed(pending.Session.Key, pending.Session.RefreshCount + 1))
                        : null;
                    pending.Session.ResolveRefresh(failure == ShopTransactionFailureReason.None, products);
                }
                if (CurrentSession == pending.Session) ShopChanged?.Invoke(pending.Session);
            }

            TransactionResolved?.Invoke(new ShopTransactionResult(
                transactionId,
                kind,
                productId,
                failure,
                remainingCoin));
        }

        private int BuildRollSeed(ShopSessionKey key, int refreshCount)
        {
            return unchecked(
                runSeed * 397 ^
                (int)OwnerClientId * 7919 ^
                (int)key.ShopId * 48611 ^
                (int)key.ShopInstanceId ^
                (int)(key.ShopInstanceId >> 32) ^
                refreshCount * 104729);
        }
    }
}
