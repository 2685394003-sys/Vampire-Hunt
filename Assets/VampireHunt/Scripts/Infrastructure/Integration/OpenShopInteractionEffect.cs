using System.Collections;
using Blocks.Gameplay.Core;
using Unity.Netcode;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Infrastructure.Unity;

namespace VampireHunt.Infrastructure.Integration
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [RequireComponent(typeof(ModularInteractable))]
    public sealed class OpenShopInteractionEffect : MonoBehaviour, IInteractionEffect
    {
        [SerializeField] private ShopDefinitionAsset shop;
        [SerializeField] private int priority = 100;

        public ShopDefinitionAsset Shop => shop;
        public int Priority => priority;

        public IEnumerator ApplyEffect(GameObject interactor, GameObject interactable)
        {
            if (shop == null || interactor == null) yield break;
            MonoBehaviour[] behaviours = interactor.GetComponentsInParent<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (!(behaviours[i] is IShopInteractionGateway gateway)) continue;
                NetworkObject source = GetComponent<NetworkObject>();
                ulong instanceId = source != null && source.IsSpawned ? source.NetworkObjectId : 0UL;
                gateway.OpenShop(shop.ShopId, instanceId);
                yield break;
            }
        }

        public void CancelEffect(GameObject interactor)
        {
        }
    }
}
