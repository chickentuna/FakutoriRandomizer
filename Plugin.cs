using Archipelago.MultiClient.Net.Models;
using BepInEx;
using BepInEx.Logging;
using Fakutori.Dialogue;
using Fakutori.Grid;
using FakutoriArchipelago.Archipelago;
using FakutoriArchipelago.Utils;
using HarmonyLib;
using I2.Loc;
using MonoMod.Utils;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using UnityEngine;
using UnityEngine.Android;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.UIElements;
using static SFX;
using static UnityEngine.UIElements.GenericDropdownMenu;

namespace FakutoriArchipelago;

[BepInPlugin(PluginGUID, PluginName, PluginVersion)]
public class Plugin : BaseUnityPlugin
{
    public const string PluginGUID = "com.chickentuna.fakutori";
    public const string PluginName = "FakutoriArchipelago";
    public const string PluginVersion = "1.0.0";

    public const string ModDisplayInfo = $"{PluginName} v{PluginVersion}";
    public const string APDisplayInfo = $"Archipelago v{ArchipelagoClient.APVersion}";

    public static ManualLogSource BepinLogger;
    public static ArchipelagoClient ArchipelagoClient;

    public static bool didRecipeDump = true;
    public static bool didBlockDump = true;
    public static bool didInitShop = false;

    public static ConcurrentQueue<ItemInfo> PendingItems = new();
    public static List<long> UnlockedItemIds = new();
    public static List<long> BlockIdsThatAreNotLocations = new();

    public static void AddToPendingItems(ItemInfo item)
    {
        if (UnlockedItemIds.Contains(item.ItemId))
        {
            return;
        }
        PendingItems.Enqueue(item);
    }

    public static void DoCheck(long locationId)
    {
        if (ArchipelagoClient.Authenticated)
        {
            ArchipelagoClient.session.Locations.CompleteLocationChecksAsync(locationId).ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    ArchipelagoClient.ServerData.CheckedLocations.Add(locationId);
                }
                else
                {
                    Plugin.BepinLogger.LogError($"Failed to complete location check for {locationId}: {task.Exception}");
                }
            });
        }
        else
        {
            //TODO: if were offline, save checks to send instead of simulating recieveing the item
            var lib = Resources.FindObjectsOfTypeAll<BlocksLibrary>()[0];
            var blockData = lib.GetBlockDataById((int)locationId);
            UnlockBlockPatch.ApplyUnlock(blockData);
        }
    }

    public void Awake()
    {
        // Plugin startup logic
        BepinLogger = Logger;
        ArchipelagoClient = new ArchipelagoClient();
        ArchipelagoConsole.Awake();

        ArchipelagoConsole.LogMessage($"{ModDisplayInfo} loaded!");
        var harmony = new Harmony("com.chickentuna.archipelago");
        harmony.PatchAll();

        UnlockedItemIds.Add(11);
        UnlockedItemIds.Add(14);
        UnlockedItemIds.Add(15);
        UnlockedItemIds.Add(16);
        BlockIdsThatAreNotLocations.Add(11);
        BlockIdsThatAreNotLocations.Add(14);
        BlockIdsThatAreNotLocations.Add(15);
        BlockIdsThatAreNotLocations.Add(16);

        //TODO: muliworld stuff, including loading archipelago sprite
    }


}

[HarmonyPatch(typeof(BlockData), "blockName", MethodType.Getter)]
class BlockNamePropertyPatch
{
    static void Postfix(BlockData __instance, ref string __result)
    {
        // You can look at __instance.NameKey if it's a field/property on Block
        if (__result == "Quartz")
        {
            __result = __result + " (" + __instance.color.colorName + ")";
        }
    }
}

[HarmonyPatch(typeof(EditModeMenu))]
class EditModeMenuPatch
{

    [HarmonyPatch("AddItem")]
    [HarmonyPrefix]
    static void PreAddItem(EditModeMenu __instance, Sprite uiSprite, string name, string price, bool showShadow, SelectionTool tool, bool isLocked = false, int atIndex = -1)
    {
        Plugin.BepinLogger.LogInfo($"AddItem called with {name}, {price}, {tool}, {isLocked}, {atIndex}");
    }

    [HarmonyPatch("FillItems")]
    [HarmonyPostfix]
    static void PostFillItems(EditModeMenu __instance)
    {
        Plugin.BepinLogger.LogInfo($"PostFillItems");

        // Game has just filled the menu with the standard icons.
        var menuItemsField = AccessTools.Field(typeof(EditModeMenu), "menuItems");
        var menuItems = (List<SelectMenuItem>)menuItemsField.GetValue(__instance);

        int menuIdx = 3;
        foreach (BlockData blockData in AbstractSingleton<BlocksManager>.Instance.blocksLibrary.machineBlocks)
        {
            SelectMenuItem menuItem = menuItems[menuIdx];
            menuIdx++;

            // What is this replaced by?
            ItemInfo itemInfo;
            if (!UnlockBlockPatch.ShopLocations.TryGetValue(blockData.blockId, out itemInfo))
            {
                continue;
            }

            if (itemInfo.Player.Name == ArchipelagoClient.ServerData.SlotName)
            {
                menuItem.GetComponentInChildren<SpriteRenderer>().sprite = UnlockBlockPatch.BlockSprites[blockData.blockId];
                Plugin.BepinLogger.LogInfo($"Block {blockData.blockName} in menu item {menuIdx} now has the sprite of block {blockData}");
            }
            else
            {
                //TODO: archipelago logo
            }
        }
            // If I have unlocked machine X, but have not yet checked location X, then the shop will need a duplicate.
            //TODO: oh dear.

            // what happens if i just stick an element in the shop?
            // testing that here:
            //AddItem(Sprite uiSprite, string name, string price, bool showShadow, SelectionTool tool, bool isLocked = false, int atIndex = -1)
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

        var blockData13 = AbstractSingleton<BlocksManager>.Instance.blocksLibrary.GetBlockDataById(13);
        Plugin.BepinLogger.LogInfo($"Calling AddItem from hook ({AddItemMethod})");
            AddItemMethod.Invoke(__instance, new object[] {
                UnlockBlockPatch.BlockSprites[13],
                "BLUE FLAME",
                blockData13.unlockCost.ToString(),
                true,
                new UnlockMachineBlockTool(blockData13) as SelectionTool,
                true,
                -1
            });
        // NOTE: this works GREAT. All I need now is to remove the tools unless they are unlocked, then for each tool location reinsert
        // a menu item

    }

    [HarmonyPatch("UnlockMachineAtIndex")]
    [HarmonyPrefix]
    static bool PreUnlockMachineAtIndex(EditModeMenu __instance, int atIndex)
    {

        //TODO: update the local shop datastructure and force close the menu?

        var menuItemsField = AccessTools.Field(typeof(EditModeMenu), "menuItems");
        var menuItems = (List<SelectMenuItem>)menuItemsField.GetValue(__instance);

        BlockData data = (menuItems[atIndex].tool as UnlockMachineBlockTool).blockData;
        AbstractSingleton<CurrencyManager>.Instance.SpendCurrencies(data.unlockCost, 0, null);
        AbstractSingleton<SFXManager>.Instance.PlayUISfx(SFX.Menus.UnlockCurrency, 0f, null);
        UnityEngine.Object.Destroy(menuItems[atIndex].gameObject);
        menuItems.RemoveAt(atIndex);

        //TODO: should i do this for machines only?
        // Maybe i need to keep shop items in a different datastructure
        //TODO: I'm pretty sure i can conflict with the other sprite changing logic
        var lib = Resources.FindObjectsOfTypeAll<BlocksLibrary>()[0];
        var uiSpriteField = AccessTools.Field(typeof(BlockData), "UISprite");
        var originalSprite = UnlockBlockPatch.BlockSprites[data.blockId];
        uiSpriteField.SetValue(data, originalSprite);

        // I have just bought a check. 
        Plugin.DoCheck(data.blockId);

        return false;
    }
}

[HarmonyPatch(typeof(ProgressManager))]
internal class UnlockBlockPatch
{
    static BlocksManager blocksManager;
    static bool AllowOnElementBlockSpawned = false;
    public static Dictionary<long, Sprite> BlockSprites;
    public static Dictionary<long, ItemInfo> ShopLocations = new();

    [HarmonyPatch("OnElementBlockSpawned")]
    [HarmonyPrefix]
    static bool PreOnElementBlockSpawned(ProgressManager __instance, BlockData blockData, Fakutori.Grid.GridCell onCell)
    {
        if (!ArchipelagoClient.Authenticated)
        {
            return true;
        }
        if (AbstractSingleton<GameplayManager>.Instance.state != GameplayManager.GameplayState.Watch)
        {
            return true;
        }

        if (AllowOnElementBlockSpawned)
        {
            return true;
        }


        bool isUnlocked = Plugin.UnlockedItemIds.Contains(blockData.blockId);
        bool uncheckedLocation = !ArchipelagoClient.ServerData.CheckedLocations.Contains(blockData.blockId) && !Plugin.BlockIdsThatAreNotLocations.Contains(blockData.blockId);

        if (uncheckedLocation)
        {

            Plugin.DoCheck(blockData.blockId);
        }

        if (!Plugin.UnlockedItemIds.Contains(blockData.blockId))
        {
            List<Block> AllBlocksAt = AbstractSingleton<GridManager>.Instance.GetBlocksAt<Block>(onCell.position);
            foreach (Block block in AllBlocksAt)
            {
                blocksManager.RemoveBlock(block);
            }
        }

        return false;
    }



    [HarmonyPatch("Start")]
    [HarmonyPostfix]
    static void PostStart(ProgressManager __instance)
    {
        Plugin.BepinLogger.LogInfo("ProgressManager Start called");
        AbstractSingleton<TimeManager>.Instance.OnTick.AddListener(OnGameTick);

        var go = new GameObject("ArchipelagoUI");
        //DontDestroyOnLoad(go);
        var ui = go.AddComponent<ArchipelagoUI>();

        ui.Init();
        AbstractSingleton<TimeManager>.Instance.OnTick.AddListener(ui.OnTick);
        blocksManager = AbstractSingleton<BlocksManager>.Instance;

        var ElementBlocksField = AccessTools.Field(typeof(BlocksLibrary), "ElementBlocks");
        var MachineBlocksField = AccessTools.Field(typeof(BlocksLibrary), "MachineBlocks");
        var lib = Resources.FindObjectsOfTypeAll<BlocksLibrary>()[0];
        var ElementBlocks = (BlockData[])ElementBlocksField.GetValue(lib);
        var MachineBlocks = (BlockData[])MachineBlocksField.GetValue(lib);

        var ShowInCompendiumField = AccessTools.Field(typeof(BlockData), "ShowInCompendium");

        BlockSprites = new Dictionary<long, Sprite>();
        foreach (var blockData in ElementBlocks.Concat(MachineBlocks))
        {
            BlockSprites.Add(blockData.blockId, blockData.uiSprite);
            ShowInCompendiumField.SetValue(blockData, true);
        }
    }

    public static void ApplyUnlock(BlockData blockData)
    {

        // Remove from shop if its still a shop location

        if (ShopLocations.Values.ToList().Find(itemInfo => itemInfo.ItemId == blockData.blockId) != null)
        {
            //TODO: remove it from my list of things that should be in shop (gets recreated when menu opens)

        }

        if (blockData.category.name == "Machine")
        {
            AbstractSingleton<Notifications>.Instance.QueueNotification(NotificationType.NewBlock, blockData, null);
            AbstractSingleton<ProgressManager>.Instance.UnlockMachine(blockData);

            Plugin.BepinLogger.LogInfo($"Unlocked machine block {blockData.blockName}");


            // Note: menu is recreated on the fly using FillItems whenever it opens so this is a no go
        }
        else
        {
            AllowOnElementBlockSpawned = true;
            AbstractSingleton<ProgressManager>.Instance.OnElementBlockSpawned(blockData, null);
            AllowOnElementBlockSpawned = false;
        }
    }

    static void OnGameTick()
    {
        if (!Plugin.didInitShop && ArchipelagoClient.Authenticated)
        {
            //Populate the shop
            List<string> shopLocationNames = new List<string>();
            shopLocationNames.Add("Disassembler");
            shopLocationNames.Add("Puller");
            shopLocationNames.Add("Pusher");
            shopLocationNames.Add("Toggle conveyor");
            shopLocationNames.Add("Cross conveyor");

            var lib = Resources.FindObjectsOfTypeAll<BlocksLibrary>()[0];
            var MachineBlocksField = AccessTools.Field(typeof(BlocksLibrary), "MachineBlocks");
            var MachineBlocks = (BlockData[])MachineBlocksField.GetValue(lib);

            var shopLocationIds = new List<long>();

            shopLocationIds = shopLocationNames.Select(name =>
            Plugin.ArchipelagoClient.session.Locations.GetLocationIdFromName("Fakutori", name))
            .Where(id => id != -1)
            .ToList();

            Plugin.ArchipelagoClient.session.Locations.ScoutLocationsAsync(shopLocationIds.ToArray()).ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    var result = task.Result;
                    foreach (var pair in result)
                    {
                        long id = pair.Key;
                        ItemInfo itemInfo = pair.Value;

                        ShopLocations.Add(id, itemInfo);

                        // Old system was to change uiSprite, but instead we can directly change the game objects sprite
                        //TODO: I should instead create a smaller little sprite in the corner to indicate origin
                        //TODO: that
                        if (itemInfo.Player.Name == ArchipelagoClient.ServerData.SlotName)
                        {
                            var replacingBlockData = lib.GetBlockDataById((int)itemInfo.ItemId);
                            var replacingSprite = BlockSprites[(int)itemInfo.ItemId];
                            var uiSpriteField = AccessTools.Field(typeof(BlockData), "UISprite");
                            var blockData = lib.GetBlockDataById((int)id);
                            uiSpriteField.SetValue(blockData, replacingSprite);
                            Plugin.BepinLogger.LogInfo($"Block {blockData.blockName} now has the sprite of block {replacingBlockData}");
                        }
                        else
                        {
                            // Is for another world!
                            //TODO: an archipelago sprite
                        }

                    }
                }
                else
                {
                    Plugin.BepinLogger.LogError($"Failed to scout locations");
                }
            });


            Plugin.didInitShop = true;
        }

        if (!Plugin.didBlockDump)
        {
            var ElementBlocksField = AccessTools.Field(typeof(BlocksLibrary), "ElementBlocks");
            var MachineBlocksField = AccessTools.Field(typeof(BlocksLibrary), "MachineBlocks");
            var lib = Resources.FindObjectsOfTypeAll<BlocksLibrary>()[0];
            var ElementBlocks = (BlockData[])ElementBlocksField.GetValue(lib);
            var MachineBlocks = (BlockData[])MachineBlocksField.GetValue(lib);

            string path = Path.Combine(Paths.PluginPath, "blocks_dump.txt");
            BlockDumper.DumpBlocks(ElementBlocks.Concat(MachineBlocks).ToArray(), path);
            Plugin.didBlockDump = true;
        }

        if (!Plugin.didRecipeDump)
        {
            string path = Path.Combine(Paths.PluginPath, "recipes_dump.txt");

            var RecipesField = AccessTools.Field(typeof(BlocksLibrary), "Recipes");
            var lib = Resources.FindObjectsOfTypeAll<BlocksLibrary>()[0];
            var Recipes = (Recipe[])RecipesField.GetValue(lib);

            if (Recipes == null)
            {
                Plugin.BepinLogger.LogInfo("Recipes is null!");
                return;
            }
            RecipeDumper.DumpRecipes(Recipes, path);

            Plugin.didRecipeDump = true;
        }

        if (AbstractSingleton<GameplayManager>.Instance.state != GameplayManager.GameplayState.Watch
            && AbstractSingleton<GameplayManager>.Instance.state != GameplayManager.GameplayState.Edit)
        {
            return;
        }

        ItemInfo itemInfo;
        if (Plugin.PendingItems.TryDequeue(out itemInfo))
        {
            Plugin.UnlockedItemIds.Add(itemInfo.ItemId);

            var lib = Resources.FindObjectsOfTypeAll<BlocksLibrary>()[0];

            var blockData = lib.GetBlockDataById((int)itemInfo.ItemId);

            bool fromOwnGame = itemInfo.Player.Name == ArchipelagoClient.ServerData.SlotName;
            if (fromOwnGame)
            {
                if (BlockSprites.ContainsKey(itemInfo.LocationId))
                {
                    var originalSprite = BlockSprites[(int)itemInfo.LocationId];
                    var uiSpriteField = AccessTools.Field(typeof(BlockData), "UISprite");
                    uiSpriteField.SetValue(blockData, originalSprite);
                }
                else if (itemInfo.LocationId > -2)
                {
                    Plugin.BepinLogger.LogWarning($"No sprite found for block id {itemInfo.LocationId}");
                }
            }
            else
            {
                // Archipelago sprite
            }


            ApplyUnlock(blockData);

        }

    }

}





