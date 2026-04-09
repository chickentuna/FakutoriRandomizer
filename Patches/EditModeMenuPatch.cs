using Archipelago.MultiClient.Net.Helpers;
using FakutoriArchipelago.Archipelago;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace FakutoriArchipelago;

[HarmonyPatch(typeof(EditModeMenu))]
class EditModeMenuPatch
{
    public static Sprite MoneySprite;
    public static Sprite ManaSprite;
    public static Sprite StarPowerSprite;
    public static Sprite ArchipelagoSprite;
    public static Sprite ArchipelagoWhiteSprite;
    public static Sprite ArchipelagoSDFSprite;

    [HarmonyPatch("FillItems")]
    [HarmonyPostfix]
    static void PostFillItems(EditModeMenu __instance)
    {
        var currentLayerField = AccessTools.Field(typeof(EditModeMenu), "currentLayer");
        var currentLayer = (EditModeLayer)currentLayerField.GetValue(__instance);
        if (currentLayer != EditModeLayer.Machines)
            return;

        var menuItemsField = AccessTools.Field(typeof(EditModeMenu), "menuItems");
        var menuItems = (List<SelectMenuItem>)menuItemsField.GetValue(__instance);
        var lib = AbstractSingleton<BlocksManager>.Instance.blocksLibrary;

        List<BlockData> unlockedMachines = lib.machineBlocks
            .Where(bd => Plugin.UnlockedItemIds.Contains(bd.blockId))
            .ToList();
        List<ItemInfo> toAddToShop = BuildShopItemList(lib);

        // Remove everything past the first 3 entries (eraser/move/select stay)
        for (int i = menuItems.Count - 1; i >= 3; i--)
        {
            UnityEngine.Object.Destroy(menuItems[i].gameObject);
            menuItems.RemoveAt(i);
        }

        var AddItemMethod = AccessTools.Method(typeof(EditModeMenu), "AddItem",
            new Type[] { typeof(Sprite), typeof(string), typeof(string), typeof(bool), typeof(SelectionTool), typeof(bool), typeof(int) }
        );

        // Put already-unlocked machines back in (not locked)
        foreach (BlockData blockData in unlockedMachines)
        {
            AddItemMethod.Invoke(__instance, new object[] {
                ProgressManagerPatch.BlockSprites[blockData.blockId],
                blockData.blockName,
                blockData.moneyValue.ToString(),
                true,
                new PlaceMachineBlockTool(blockData) as SelectionTool,
                false,
                -1
            });
        }

        // Add buyable shop items (shown as locked/purchasable)
        foreach (ItemInfo itemInfo in toAddToShop)
        {
            var (blockForSale, sprite) = CreateShopBlockData(itemInfo, lib);
            SelectionTool selectionTool = new UnlockMachineBlockTool(blockForSale);
            ProgressManagerPatch.ShopLocationIdFromBlockData.TryAdd(blockForSale, itemInfo.LocationId);

            AddItemMethod.Invoke(__instance, new object[] {
                sprite != null ? sprite : ProgressManagerPatch.BlockSprites[1],
                blockForSale.blockName,
                blockForSale.unlockCost.ToString(),
                true,
                selectionTool,
                true,
                -1
            });
        }
    }

    // Determine which AP shop locations are still unchecked and should appear as buyable items.
    static List<ItemInfo> BuildShopItemList(BlocksLibrary lib)
    {
        var toAddToShop = new List<ItemInfo>();

        foreach (var kv in ProgressManagerPatch.ShopLocations)
        {
            long shoplocationId = kv.Key;
            ItemInfo itemInfo = kv.Value;

            if (ArchipelagoClient.session.Locations.AllLocationsChecked.Contains(shoplocationId))
                continue;

            if (itemInfo.Player.Name == ArchipelagoClient.ServerData.SlotName)
            {
                // Filler items always go in the shop
                if (itemInfo.ItemId >= Constants.Filler500GoldItemId)
                {
                    toAddToShop.Add(itemInfo);
                    continue;
                }

                // Skip blocks already owned
                if (Plugin.UnlockedItemIds.Contains(itemInfo.ItemId))
                    continue;

                BlockData blockForSale = lib.GetBlockDataById((int)itemInfo.ItemId);
                var unlockCostField = AccessTools.Field(typeof(BlockData), "UnlockCost");
                unlockCostField.SetValue(blockForSale, ProgressManagerPatch.BlockUnlockCosts[shoplocationId]);
                toAddToShop.Add(itemInfo);
            }
            else
            {
                toAddToShop.Add(itemInfo);
            }
        }

        return toAddToShop;
    }

    // Build the BlockData and icon sprite for a single shop slot.
    // Handles three cases: remote item (another player's world), filler item, or a local block.
    static (BlockData blockForSale, Sprite sprite) CreateShopBlockData(ItemInfo itemInfo, BlocksLibrary lib)
    {
        var BlockIdField = AccessTools.Field(typeof(BlockData), "BlockId");
        var NameKeyField = AccessTools.Field(typeof(BlockData), "NameKey");
        var UnlockCostField = AccessTools.Field(typeof(BlockData), "UnlockCost");

        if (itemInfo.Player.Name != ArchipelagoClient.ServerData.SlotName)
        {
            // Item belongs to another player's world — show with AP icon
            var block = ScriptableObject.CreateInstance<BlockData>();
            BlockIdField.SetValue(block, (int)itemInfo.ItemId);
            UnlockCostField.SetValue(block, ProgressManagerPatch.BlockUnlockCosts[itemInfo.LocationId]);
            NameKeyField.SetValue(block, $"custom_{itemInfo.ItemDisplayName} ({itemInfo.Player.Name})");
            return (block, ArchipelagoSprite);
        }

        if (itemInfo.ItemId >= Constants.Filler500GoldItemId)
        {
            // Filler item — currency/resource reward
            var block = ScriptableObject.CreateInstance<BlockData>();
            BlockIdField.SetValue(block, (int)itemInfo.ItemId);
            UnlockCostField.SetValue(block, ProgressManagerPatch.BlockUnlockCosts[itemInfo.LocationId]);

            Sprite sprite = null;
            if (itemInfo.ItemId == Constants.Filler500GoldItemId)      { NameKeyField.SetValue(block, "custom_500 gold");        sprite = MoneySprite; }
            else if (itemInfo.ItemId == Constants.Filler1000GoldItemId) { NameKeyField.SetValue(block, "custom_1000 gold");       sprite = MoneySprite; }
            else if (itemInfo.ItemId == Constants.Filler500ManaItemId)  { NameKeyField.SetValue(block, "custom_500 mana");        sprite = ManaSprite; }
            else if (itemInfo.ItemId == Constants.FillerFullStarpowerItemId) { NameKeyField.SetValue(block, "custom_Full starpower"); sprite = StarPowerSprite; }

            return (block, sprite);
        }

        // Local block for this player's world
        var localBlock = lib.GetBlockDataById((int)itemInfo.ItemId);
        return (localBlock, ProgressManagerPatch.BlockSprites[localBlock.blockId]);
    }

    [HarmonyPatch("UnlockMachineAtIndex")]
    [HarmonyPrefix]
    static bool PreUnlockMachineAtIndex(EditModeMenu __instance, int atIndex)
    {
        var menuItemsField = AccessTools.Field(typeof(EditModeMenu), "menuItems");
        var menuItems = (List<SelectMenuItem>)menuItemsField.GetValue(__instance);

        BlockData data = (menuItems[atIndex].tool as UnlockMachineBlockTool).blockData;
        AbstractSingleton<CurrencyManager>.Instance.SpendCurrencies(data.unlockCost, 0, null);
        AbstractSingleton<SFXManager>.Instance.PlayUISfx(SFX.Menus.UnlockCurrency, 0f, null);
        UnityEngine.Object.Destroy(menuItems[atIndex].gameObject);
        menuItems.RemoveAt(atIndex);

        // I have just bought a check.
        if (ProgressManagerPatch.ShopLocationIdFromBlockData.TryGetValue(data, out long shopLocationId))
        {
            Plugin.DoCheck(shopLocationId);
        }
        else
        {
            Plugin.BepinLogger.LogWarning($"Problem! bought a {data.blockName} from the shop, but there was no associated shop location ID");
            Plugin.BepinLogger.LogWarning("ShopLocationIds:");
            foreach (var kv in ProgressManagerPatch.ShopLocationIdFromBlockData)
            {
                Plugin.BepinLogger.LogWarning($" - {kv.Key.blockName}: {kv.Value}");
            }
        }



        return false;
    }
}
