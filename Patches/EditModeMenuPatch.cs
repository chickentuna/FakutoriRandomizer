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
        {
            return;
        }


        // Game has just filled the menu with the standard icons.
        var menuItemsField = AccessTools.Field(typeof(EditModeMenu), "menuItems");
        var menuItems = (List<SelectMenuItem>)menuItemsField.GetValue(__instance);
        var lib = AbstractSingleton<BlocksManager>.Instance.blocksLibrary;

        List<BlockData> machinesToKeepBecauseUnlocked = lib.machineBlocks.Where(blockData =>
            Plugin.UnlockedItemIds.Contains(blockData.blockId)
        ).ToList();

        List<ItemInfo> toAddToShop = new List<ItemInfo>();
        int indexAtWhichToClear = 3;

        foreach (var kv in ProgressManagerPatch.ShopLocations)
        {
            long shoplocationId = kv.Key;

            ItemInfo itemInfo = kv.Value;

            // If location hasn't been checked, will need to go back into shop
            if (!ArchipelagoClient.session.Locations.AllLocationsChecked.Contains(shoplocationId))
            {
                if (itemInfo.Player.Name == ArchipelagoClient.ServerData.SlotName)
                {

                    // Handle items 100X (filler items)
                    if (itemInfo.ItemId >= Constants.Filler500GoldItemId)
                    {
                        toAddToShop.Add(itemInfo);
                        continue;
                    }

                    // Skip the item if already owned
                    // if (Plugin.UnlockedItemIds.Contains(shoplocationId))
                    if (Plugin.UnlockedItemIds.Contains(itemInfo.ItemId))
                    {
                        continue;
                    }

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
        }


        // Remove everything from the shop
        if (indexAtWhichToClear != -1)
        {
            for (int i = menuItems.Count - 1; i >= indexAtWhichToClear; i--)
            {
                UnityEngine.Object.Destroy(menuItems[i].gameObject);
                menuItems.RemoveAt(i);
            }
        }

        var AddItemMethod = AccessTools.Method(typeof(EditModeMenu), "AddItem",
            new Type[] {
                    typeof(Sprite),      // uiSprite
                    typeof(string),      // name
                    typeof(string),      // price
                    typeof(bool),        // showShadow
                    typeof(SelectionTool), // tool (base class of UnlockMachineBlockTool)
                    typeof(bool),        // isLocked
                    typeof(int)          // atIndex
            }
        );

        // Put unlocked machines back into the menu
        foreach (BlockData blockData in machinesToKeepBecauseUnlocked)
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

        // Put buyable things into the menu
        foreach (ItemInfo itemInfo in toAddToShop)
        {
            BlockData blockForSale = null;

            /*
                item_name_to_id["500 gold"] = 1000
                item_name_to_id["1000 gold"] = 1001
                item_name_to_id["500 mana"] = 1002
                item_name_to_id["Full starpower"] = 1003
            */
            var BlockIdField = AccessTools.Field(typeof(BlockData), "BlockId");
            var NameKeyField = AccessTools.Field(typeof(BlockData), "NameKey");
            var UnlockCostField = AccessTools.Field(typeof(BlockData), "UnlockCost");
            Sprite sprite = null;
            if (itemInfo.Player.Name != ArchipelagoClient.ServerData.SlotName)
            {
                // Remote item
                blockForSale = ScriptableObject.CreateInstance<BlockData>();
                BlockIdField.SetValue(blockForSale, (int)itemInfo.ItemId);
                UnlockCostField.SetValue(blockForSale, ProgressManagerPatch.BlockUnlockCosts[itemInfo.LocationId]);
                NameKeyField.SetValue(blockForSale, $"custom_{itemInfo.ItemDisplayName} ({itemInfo.Player.Name})");
                sprite = ArchipelagoSprite;

            }
            else if (itemInfo.ItemId >= Constants.Filler500GoldItemId)
            {
                blockForSale = ScriptableObject.CreateInstance<BlockData>();
                BlockIdField.SetValue(blockForSale, (int)itemInfo.ItemId);
                UnlockCostField.SetValue(blockForSale, ProgressManagerPatch.BlockUnlockCosts[itemInfo.LocationId]);
                if (itemInfo.ItemId == Constants.Filler500GoldItemId)
                {
                    NameKeyField.SetValue(blockForSale, $"custom_500 gold");
                    sprite = MoneySprite;
                }
                else if (itemInfo.ItemId == Constants.Filler1000GoldItemId)
                {
                    NameKeyField.SetValue(blockForSale, $"custom_1000 gold");
                    sprite = MoneySprite;
                }
                else if (itemInfo.ItemId == Constants.Filler500ManaItemId)
                {
                    NameKeyField.SetValue(blockForSale, $"custom_500 mana");
                    sprite = ManaSprite;
                }
                else if (itemInfo.ItemId == Constants.FillerFullStarpowerItemId)
                {
                    NameKeyField.SetValue(blockForSale, $"custom_Full starpower");
                    sprite = StarPowerSprite;
                }
            }
            else
            {
                blockForSale = lib.GetBlockDataById((int)itemInfo.ItemId);
                sprite = ProgressManagerPatch.BlockSprites[blockForSale.blockId];
            }

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
