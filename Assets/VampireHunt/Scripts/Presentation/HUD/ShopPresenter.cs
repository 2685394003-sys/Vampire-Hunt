using System;
using System.Collections;
using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;
using VampireHunt.Economy;
using VampireHunt.Infrastructure.Netcode;
using VampireHunt.Infrastructure.Unity;
using VampireHunt.Systems;

namespace VampireHunt.Presentation.HUD
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class ShopPresenter : NetworkBehaviour
    {
        [SerializeField] private UIDocument uiDocument;
        [SerializeField] private PlayerShopNetworkBridge shopBridge;
        [SerializeField] private ShopCatalogAsset catalog;

        private readonly Button[] m_BuyButtons = new Button[3];
        private readonly Action[] m_BuyCallbacks = new Action[3];
        private readonly Label[] m_Names = new Label[3];
        private readonly Label[] m_Descriptions = new Label[3];
        private readonly Label[] m_Quantities = new Label[3];
        private readonly Label[] m_Prices = new Label[3];
        private readonly VisualElement[] m_Cards = new VisualElement[3];
        private VisualElement m_Overlay;
        private Label m_Title;
        private Label m_Coin;
        private Label m_Result;
        private Button m_Refresh;
        private Button m_Close;
        private bool m_Bound;
        private bool m_CursorCaptured;
        private int m_LastKnownCoin;
        private bool m_ShopPausedByUs;
        private CursorLockMode m_PreviousLockMode;
        private bool m_PreviousCursorVisible;

        private void Awake()
        {
            if (uiDocument == null) uiDocument = GetComponent<UIDocument>();
            if (shopBridge == null) shopBridge = GetComponent<PlayerShopNetworkBridge>();
        }

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (!IsOwner || shopBridge == null) return;
            shopBridge.ShopOpened += HandleShopOpened;
            shopBridge.ShopChanged += Render;
            shopBridge.ShopClosed += HandleShopClosed;
            shopBridge.TransactionResolved += HandleTransactionResolved;
            m_LastKnownCoin = shopBridge.CoinBalance;
            StartCoroutine(BindNextFrame());
        }

        public override void OnNetworkDespawn()
        {
            if (shopBridge != null)
            {
                shopBridge.ShopOpened -= HandleShopOpened;
                shopBridge.ShopChanged -= Render;
                shopBridge.ShopClosed -= HandleShopClosed;
                shopBridge.TransactionResolved -= HandleTransactionResolved;
            }
            UnbindUi();
            SetCursorForShop(false);
            if (m_ShopPausedByUs)
            {
                m_ShopPausedByUs = false;
                MenuPauseController.ReleaseShopPause();
            }
            base.OnNetworkDespawn();
        }

        private IEnumerator BindNextFrame()
        {
            yield return null;
            BindUi();
            SetVisible(false);
        }

        private void BindUi()
        {
            if (m_Bound || uiDocument == null) return;
            VisualElement root = uiDocument.rootVisualElement;
            if (root == null) return;
            m_Overlay = root.Q<VisualElement>("shop-overlay");
            m_Title = root.Q<Label>("shop-title");
            m_Coin = root.Q<Label>("shop-coin");
            m_Result = root.Q<Label>("shop-result");
            for (int i = 0; i < 3; i++)
            {
                int captured = i;
                m_Cards[i] = root.Q<VisualElement>($"shop-card-{i}");
                m_Names[i] = root.Q<Label>($"shop-name-{i}");
                m_Descriptions[i] = root.Q<Label>($"shop-description-{i}");
                m_Quantities[i] = root.Q<Label>($"shop-quantity-{i}");
                m_Prices[i] = root.Q<Label>($"shop-price-{i}");
                m_BuyButtons[i] = root.Q<Button>($"shop-buy-{i}");
                if (m_BuyButtons[i] != null)
                {
                    m_BuyCallbacks[i] = () => shopBridge?.Purchase(captured);
                    m_BuyButtons[i].clicked += m_BuyCallbacks[i];
                }
            }
            m_Refresh = root.Q<Button>("shop-refresh");
            m_Close = root.Q<Button>("shop-close");
            if (m_Refresh != null) m_Refresh.clicked += Refresh;
            if (m_Close != null) m_Close.clicked += Close;
            m_Bound = true;
        }

        private void UnbindUi()
        {
            for (int i = 0; i < m_BuyButtons.Length; i++)
            {
                if (m_BuyButtons[i] != null && m_BuyCallbacks[i] != null)
                    m_BuyButtons[i].clicked -= m_BuyCallbacks[i];
                m_BuyCallbacks[i] = null;
            }
            if (m_Refresh != null) m_Refresh.clicked -= Refresh;
            if (m_Close != null) m_Close.clicked -= Close;
            m_Bound = false;
        }

        private void HandleShopOpened(ShopSession session)
        {
            BindUi();
            m_LastKnownCoin = shopBridge != null ? shopBridge.CoinBalance : 0;
            if (m_Result != null) m_Result.text = string.Empty;
            SetVisible(true);
            SetCursorForShop(true);
            // 单人模式下，打开商店菜单时暂停游戏
            if (!m_ShopPausedByUs)
            {
                m_ShopPausedByUs = true;
                MenuPauseController.RequestShopPause();
            }
            Render(session);
        }

        private void HandleShopClosed()
        {
            SetVisible(false);
            SetCursorForShop(false);
            if (m_ShopPausedByUs)
            {
                m_ShopPausedByUs = false;
                MenuPauseController.ReleaseShopPause();
            }
        }

        private void Render(ShopSession session)
        {
            BindUi();
            if (session == null || m_Overlay == null) return;
            if (m_Title != null) m_Title.text = session.Definition.DisplayName;
            if (m_Coin != null) m_Coin.text = $"魔币  {m_LastKnownCoin}";

            for (int i = 0; i < 3; i++)
            {
                bool available = i < session.Offers.Count;
                if (m_Cards[i] != null)
                    m_Cards[i].style.display = available ? DisplayStyle.Flex : DisplayStyle.None;
                if (!available) continue;

                ShopOffer offer = session.Offers[i];
                ShopProductDefinition product = offer.Product;
                ShopProductDefinitionAsset productAsset = null;
                catalog?.TryGetProductAsset(product.ProductId, out productAsset);
                ItemDefinitionAsset item = productAsset != null ? productAsset.Item : null;
                if (m_Names[i] != null) m_Names[i].text = product.DisplayName;
                if (m_Descriptions[i] != null)
                    m_Descriptions[i].text = item != null ? item.Description : string.Empty;
                if (m_Quantities[i] != null) m_Quantities[i].text = $"数量 ×{product.Quantity}";
                if (m_Prices[i] != null) m_Prices[i].text = $"{product.BasePrice} 魔币";
                if (m_BuyButtons[i] != null)
                {
                    m_BuyButtons[i].text = offer.Purchased
                        ? "已售罄"
                        : offer.PurchasePending ? "处理中…" : "购买";
                    m_BuyButtons[i].SetEnabled(
                        !offer.Purchased && !offer.PurchasePending && !session.RefreshPending &&
                        m_LastKnownCoin >= product.BasePrice);
                }
            }

            if (m_Refresh != null)
            {
                m_Refresh.text = session.RefreshPending
                    ? "刷新中…"
                    : $"刷新商品（{session.Definition.RefreshPrice} 魔币）";
                m_Refresh.SetEnabled(
                    !session.RefreshPending && m_LastKnownCoin >= session.Definition.RefreshPrice);
            }
        }

        private void HandleTransactionResolved(ShopTransactionResult result)
        {
            m_LastKnownCoin = result.RemainingCoin;
            if (m_Result != null)
            {
                m_Result.text = result.Success
                    ? result.Kind == ShopTransactionKind.Refresh ? "商品已经刷新" : "购买成功"
                    : FailureText(result.FailureReason);
                m_Result.EnableInClassList("shop-result--failure", !result.Success);
            }
            Render(shopBridge != null ? shopBridge.CurrentSession : null);
        }

        private static string FailureText(ShopTransactionFailureReason failure)
        {
            return failure switch
            {
                ShopTransactionFailureReason.InsufficientCoin => "魔币不足",
                ShopTransactionFailureReason.InventoryRejected => "背包空间不足或已经达到堆叠上限",
                ShopTransactionFailureReason.ProductUnavailable => "该商品当前不可用",
                ShopTransactionFailureReason.DuplicateTransaction => "重复交易已被忽略",
                _ => "交易失败"
            };
        }

        private void Refresh() => shopBridge?.Refresh();
        private void Close() => shopBridge?.CloseShop();

        private void SetVisible(bool visible)
        {
            if (m_Overlay != null)
                m_Overlay.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void SetCursorForShop(bool active)
        {
            if (active && !m_CursorCaptured)
            {
                m_PreviousLockMode = UnityEngine.Cursor.lockState;
                m_PreviousCursorVisible = UnityEngine.Cursor.visible;
                m_CursorCaptured = true;
                UnityEngine.Cursor.lockState = CursorLockMode.None;
                UnityEngine.Cursor.visible = true;
            }
            else if (!active && m_CursorCaptured)
            {
                UnityEngine.Cursor.lockState = m_PreviousLockMode;
                UnityEngine.Cursor.visible = m_PreviousCursorVisible;
                m_CursorCaptured = false;
            }
        }
    }
}
