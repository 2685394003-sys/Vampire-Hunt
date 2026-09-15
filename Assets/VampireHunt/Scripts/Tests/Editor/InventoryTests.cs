using System;
using Blocks.Gameplay.Core;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using VampireHunt.Contracts;
using VampireHunt.Economy;
using VampireHunt.Effects;
using VampireHunt.Infrastructure.Integration;
using VampireHunt.Infrastructure.Unity;

namespace VampireHunt.Tests.Editor
{
    public sealed class InventoryTests
    {
        [Test]
        public void UsableInventory_StacksThenUsesAnEmptySlot()
        {
            UsableItemDefinition item = CreateUsable(100, 3);
            var inventory = new UsableItemInventory(2);

            Assert.That(inventory.TryAdd(item, 5), Is.True);
            Assert.That(inventory.OccupiedSlotCount, Is.EqualTo(2));
            Assert.That(inventory.GetTotalQuantity(item.ItemId), Is.EqualTo(5));
            Assert.That(inventory.TryGetSlot(0, out UsableItemSlot first), Is.True);
            Assert.That(inventory.TryGetSlot(1, out UsableItemSlot second), Is.True);
            Assert.That(first.Quantity, Is.EqualTo(3));
            Assert.That(second.Quantity, Is.EqualTo(2));
        }

        [Test]
        public void UsableInventory_FailedAddDoesNotPartiallyMutate()
        {
            UsableItemDefinition item = CreateUsable(100, 3);
            var inventory = new UsableItemInventory(1);

            Assert.That(inventory.TryAdd(item, 2), Is.True);
            uint revision = inventory.Revision;
            Assert.That(inventory.TryAdd(item, 2), Is.False);
            Assert.That(inventory.GetTotalQuantity(item.ItemId), Is.EqualTo(2));
            Assert.That(inventory.Revision, Is.EqualTo(revision));
        }

        [Test]
        public void RetainedUsableItem_IsLimitedToOnePerSlot()
        {
            var item = new UsableItemDefinition(
                101,
                "Reusable",
                string.Empty,
                UseConsumptionPolicy.RetainOnUse,
                99,
                0f);
            var inventory = new UsableItemInventory(2);

            Assert.That(item.MaxStackPerSlot, Is.EqualTo(1));
            Assert.That(inventory.TryAdd(item, 2), Is.True);
            Assert.That(inventory.OccupiedSlotCount, Is.EqualTo(2));
        }

        [Test]
        public void UsableInventory_RemoveLastItemClearsSlot()
        {
            UsableItemDefinition item = CreateUsable(100, 3);
            var inventory = new UsableItemInventory(1);
            inventory.TryAdd(item, 1);

            Assert.That(inventory.TryRemoveAt(0, 1, out UsableItemSlot slot), Is.True);
            Assert.That(slot.IsEmpty, Is.True);
            Assert.That(inventory.OccupiedSlotCount, Is.Zero);
        }

        [Test]
        public void AccessoryInventory_EnforcesPerDefinitionStackPolicyWithoutSlotLimit()
        {
            AccessoryDefinition unique = CreateAccessory(200, AccessoryStackPolicy.Unique, 1);
            AccessoryDefinition unlimited = CreateAccessory(201, AccessoryStackPolicy.Unlimited, 1);
            var inventory = new AccessoryInventory();

            Assert.That(inventory.TryAddOrStack(unique, 1, out _), Is.True);
            Assert.That(inventory.TryAddOrStack(unique, 1, out _), Is.False);
            Assert.That(inventory.TryAddOrStack(unlimited, 1000, out AccessoryStack stack), Is.True);
            Assert.That(stack.Stacks, Is.EqualTo(1000));
            Assert.That(inventory.DistinctCount, Is.EqualTo(2));
        }

        [Test]
        public void ItemCatalog_RejectsDuplicateIdsAcrossItemKinds()
        {
            ItemDefinition[] definitions =
            {
                CreateUsable(300, 1),
                CreateAccessory(300, AccessoryStackPolicy.Unique, 1)
            };

            Assert.Throws<InvalidOperationException>(() => new ItemCatalog(definitions));
        }

        [Test]
        public void ConsumableService_SuccessConsumesAndStartsCooldown()
        {
            UsableItemDefinition item = CreateUsable(
                400,
                3,
                UseConsumptionPolicy.ConsumeOnSuccess,
                2f);
            var inventory = new FakeUseInventory(item);
            var effects = new FakeEffectExecutor();
            var service = new ConsumableService(inventory, effects);

            ItemUseResult first = service.TryUse(0, 10d);
            ItemUseResult second = service.TryUse(0, 11d);

            Assert.That(first.Success, Is.True);
            Assert.That(first.NextReadyTime, Is.EqualTo(12d));
            Assert.That(inventory.ConsumeCalls, Is.EqualTo(1));
            Assert.That(second.Success, Is.False);
            Assert.That(second.FailureReason, Is.EqualTo(ItemUseFailureReason.Cooldown));
            Assert.That(effects.ApplyCalls, Is.EqualTo(1));
        }

        [Test]
        public void ConsumableService_RetainedItemAppliesWithoutConsumption()
        {
            UsableItemDefinition item = CreateUsable(
                401,
                1,
                UseConsumptionPolicy.RetainOnUse,
                0f);
            var inventory = new FakeUseInventory(item);
            var service = new ConsumableService(inventory, new FakeEffectExecutor());

            ItemUseResult result = service.TryUse(0, 1d);

            Assert.That(result.Success, Is.True);
            Assert.That(inventory.ConsumeCalls, Is.Zero);
        }

        [Test]
        public void ConsumableService_UnavailableEffectDoesNotConsumeItem()
        {
            UsableItemDefinition item = CreateUsable(402, 1);
            var inventory = new FakeUseInventory(item);
            var effects = new FakeEffectExecutor { CanApply = false };
            var service = new ConsumableService(inventory, effects);

            ItemUseResult result = service.TryUse(0, 1d);

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(ItemUseFailureReason.EffectUnavailable));
            Assert.That(inventory.ConsumeCalls, Is.Zero);
            Assert.That(effects.ApplyCalls, Is.Zero);
        }

        [Test]
        public void HpBottlePickup_GrantsConfiguredItemAndOwnsSuccessDespawn()
        {
            const string pickupPath = "Assets/VampireHunt/Prefabs/Items/HPBottlePickup.prefab";
            const string itemPath = "Assets/VampireHunt/Data/Items/HP Bottle.asset";

            GameObject pickup = AssetDatabase.LoadAssetAtPath<GameObject>(pickupPath);
            ItemDefinitionAsset item = AssetDatabase.LoadAssetAtPath<ItemDefinitionAsset>(itemPath);

            Assert.That(pickup, Is.Not.Null);
            Assert.That(item, Is.Not.Null);
            GrantItemInteractionEffect grant = pickup.GetComponent<GrantItemInteractionEffect>();
            ModularInteractable interactable = pickup.GetComponent<ModularInteractable>();
            Assert.That(grant, Is.Not.Null);
            Assert.That(interactable, Is.Not.Null);
            Assert.That(grant.Item, Is.SameAs(item));
            Assert.That(grant.Quantity, Is.EqualTo(1));
            Assert.That(grant.DespawnOnSuccess, Is.True);

            var interactableState = new SerializedObject(interactable);
            Assert.That(interactableState.FindProperty("despawnAfterInteraction").boolValue, Is.False,
                "The grant effect must own despawn so a rejected grant leaves the pickup available.");

            NetworkPrefabsList networkPrefabs =
                AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>("Assets/DefaultNetworkPrefabs.asset");
            Assert.That(networkPrefabs, Is.Not.Null);
            Assert.That(networkPrefabs.Contains(pickup), Is.True);
        }

        private static UsableItemDefinition CreateUsable(
            uint id,
            int maxStack,
            UseConsumptionPolicy policy = UseConsumptionPolicy.ConsumeOnSuccess,
            float cooldown = 0f) =>
            new UsableItemDefinition(
                id,
                "Usable",
                string.Empty,
                policy,
                maxStack,
                cooldown);

        private static AccessoryDefinition CreateAccessory(
            uint id,
            AccessoryStackPolicy policy,
            int maxStacks) =>
            new AccessoryDefinition(
                id,
                "Accessory",
                string.Empty,
                policy,
                maxStacks,
                Array.Empty<IEffectModuleDescriptor>());

        private sealed class FakeUseInventory : IUsableItemUseInventory
        {
            private readonly UsableItemDefinition m_Definition;

            public int ConsumeCalls { get; private set; }

            public FakeUseInventory(UsableItemDefinition definition)
            {
                m_Definition = definition;
            }

            public bool TryGetUsableForUse(int slotIndex, out UsableItemDefinition definition)
            {
                definition = slotIndex == 0 ? m_Definition : null;
                return definition != null;
            }

            public bool TryConsumeUsable(int slotIndex, int quantity)
            {
                ConsumeCalls++;
                return slotIndex == 0 && quantity == 1;
            }
        }

        private sealed class FakeEffectExecutor : IUsableItemEffectExecutor
        {
            public bool HasEffects = true;
            public bool CanApply = true;
            public bool ApplySucceeds = true;
            public int ApplyCalls { get; private set; }

            public bool HasConfiguredEffects(uint itemId) => HasEffects;
            public bool CanApplyAny(uint itemId) => CanApply;

            public bool TryApplyConfiguredEffects(uint itemId)
            {
                ApplyCalls++;
                return ApplySucceeds;
            }
        }
    }
}
