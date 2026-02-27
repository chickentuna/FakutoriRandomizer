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
        
        Plugin.BepinLogger.LogInfo($"Doing check for location {locationId}");
        if (ArchipelagoClient.Authenticated)
        {
            ArchipelagoClient.session.Locations.CompleteLocationChecksAsync(locationId).ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    // should i keep like a local cache or something?
                    Plugin.BepinLogger.LogInfo($"Success");
                }
                else
                {
                    Plugin.BepinLogger.LogError($"Failed to complete location check for {locationId}: {task.Exception}");
                }
            });
        }
        else
        {
            Plugin.BepinLogger.LogWarning($"Not authenticated :(");
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

        // I don't understand why I receive the three default machines from server but not the basic elements
        // TODO: figure it out
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
    static bool PreAddItem(EditModeMenu __instance, Sprite uiSprite, string name, string price, bool showShadow, SelectionTool tool, bool isLocked = false, int atIndex = -1)
    {
        return true;
    }

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
        Plugin.BepinLogger.LogInfo($"PostFillItems");


        // Game has just filled the menu with the standard icons.
        var menuItemsField = AccessTools.Field(typeof(EditModeMenu), "menuItems");
        var menuItems = (List<SelectMenuItem>)menuItemsField.GetValue(__instance);
        var lib = AbstractSingleton<BlocksManager>.Instance.blocksLibrary;
        int menuIdx = 3;

        List<BlockData> machinesToKeepBecauseUnlocked = lib.machineBlocks.Where(blockData =>
            Plugin.UnlockedItemIds.Contains(blockData.blockId)
        ).ToList();

        List<ItemInfo> toAddToShop = new List<ItemInfo>();
        int indexAtWhichToClear = 3 + 4; // because the generators aren't checks


        foreach (BlockData originalMachineBlock in lib.machineBlocks)
        {
            SelectMenuItem menuItem = menuItems[menuIdx];
            menuIdx++;
            long shoplocationId = originalMachineBlock.blockId;


            // What is this replaced by?
            ItemInfo itemInfo;
            if (!UnlockBlockPatch.ShopLocations.TryGetValue(shoplocationId, out itemInfo))
            {
                continue;
            }

            // If location hasn't been checked, will need to go back into shop
            if (!ArchipelagoClient.session.Locations.AllLocationsChecked.Contains(shoplocationId))
            {
                if (itemInfo.Player.Name == ArchipelagoClient.ServerData.SlotName)
                {
                    // Skip the item is already owned
                    if (Plugin.UnlockedItemIds.Contains(originalMachineBlock.blockId))
                    {
                        continue;
                    }
                    BlockData blockForSale = lib.GetBlockDataById((int)itemInfo.ItemId);
                    var unlockCostField = AccessTools.Field(typeof(BlockData), "UnlockCost");
                    unlockCostField.SetValue(blockForSale, UnlockBlockPatch.BlockUnlockCosts[originalMachineBlock.blockId] / 10);
                    toAddToShop.Add(itemInfo);
                }
                else
                {
                    //TODO: add archipelago logo to shop
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

        // Put unlocked machines back into the menu
        foreach (BlockData blockData in machinesToKeepBecauseUnlocked)
        {
            AddItemMethod.Invoke(__instance, new object[] {
                UnlockBlockPatch.BlockSprites[blockData.blockId],
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

            BlockData blockForSale = lib.GetBlockDataById((int)itemInfo.ItemId);

            SelectionTool selectionTool = new UnlockMachineBlockTool(blockForSale);

            UnlockBlockPatch.ShopLocationIdFromBlockData.TryAdd(blockForSale, itemInfo.LocationId);

            AddItemMethod.Invoke(__instance, new object[] {
                UnlockBlockPatch.BlockSprites[blockForSale.blockId],
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

        //force close menu
        var GameplayManager = AbstractSingleton<GameplayManager>.Instance;
        // GameplayManager.ToggleEdit(false); // <- buggy

        // I have just bought a check.
        if (UnlockBlockPatch.ShopLocationIdFromBlockData.TryGetValue(data, out long shopLocationId))
        {
            Plugin.DoCheck(shopLocationId);
        }
        else
        { 
            Plugin.BepinLogger.LogWarning($"Problem! bought a {data.blockName} from the shop, but there was no associated shop location ID");
            Plugin.BepinLogger.LogWarning("ShopLocationIds:");
            foreach(var kv in UnlockBlockPatch.ShopLocationIdFromBlockData)
            {
                Plugin.BepinLogger.LogWarning($" - {kv.Key.blockName}: {kv.Value}");
            }
        }



        return false;
    }
}




[HarmonyPatch(typeof(ProgressManager))]
internal class UnlockBlockPatch
{
    static BlocksManager blocksManager;
    static bool AllowOnElementBlockSpawned = false;
    public static Dictionary<long, Sprite> BlockSprites;
    public static Dictionary<long, int> BlockUnlockCosts;
    public static Dictionary<long, ItemInfo> ShopLocations = new();
    public static Dictionary<BlockData, long> ShopLocationIdFromBlockData = new Dictionary<BlockData, long>();

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
        bool uncheckedLocation = !Plugin.BlockIdsThatAreNotLocations.Contains(blockData.blockId) && !ArchipelagoClient.session.Locations.AllLocationsChecked.Contains(blockData.blockId);


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

        int colorlessQuartzId = 47;

        BlockSprites = new Dictionary<long, Sprite>();
        foreach (var blockData in ElementBlocks.Concat(MachineBlocks))
        {
            BlockSprites.Add(blockData.blockId, blockData.uiSprite);
            if (blockData.blockId != colorlessQuartzId)
            {
                ShowInCompendiumField.SetValue(blockData, true);
            }
        }

        BlockUnlockCosts = new Dictionary<long, int>();
        foreach (var blockData in MachineBlocks)
        {
            BlockUnlockCosts.Add(blockData.blockId, blockData.unlockCost);
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
            ArchipelagoClient.session.Locations.GetLocationIdFromName("Fakutori", name))
            .Where(id => id != -1)
            .ToList();

            ArchipelagoClient.session.Locations.ScoutLocationsAsync(shopLocationIds.ToArray()).ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    var result = task.Result;
                    foreach (var pair in result)
                    {
                        long id = pair.Key;
                        ItemInfo itemInfo = pair.Value;

                        ShopLocations.Add(id, itemInfo);

                        if (itemInfo.Player.Name == ArchipelagoClient.ServerData.SlotName)
                        {
                            //var replacingBlockData = lib.GetBlockDataById((int)itemInfo.ItemId);
                            //var replacingSprite = BlockSprites[(int)itemInfo.ItemId];
                            //var uiSpriteField = AccessTools.Field(typeof(BlockData), "UISprite");
                            //var blockData = lib.GetBlockDataById((int)id);
                            //uiSpriteField.SetValue(blockData, replacingSprite);
                            //Plugin.BepinLogger.LogInfo($"Block {blockData.blockName} now has the sprite of block {replacingBlockData}");
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
            Plugin.BepinLogger.LogInfo($"all unlocked items: ");
            foreach (var id in Plugin.UnlockedItemIds)
            {
                Plugin.BepinLogger.LogInfo($" - {id}");
            }

            var lib = Resources.FindObjectsOfTypeAll<BlocksLibrary>()[0];

            var blockData = lib.GetBlockDataById((int)itemInfo.ItemId);

            bool fromOwnGame = itemInfo.Player.Name == ArchipelagoClient.ServerData.SlotName;
            if (fromOwnGame)
            {
                if (BlockSprites.ContainsKey(itemInfo.LocationId))
                {
                    //var originalSprite = BlockSprites[(int)itemInfo.LocationId];
                    //var uiSpriteField = AccessTools.Field(typeof(BlockData), "UISprite");
                    //uiSpriteField.SetValue(blockData, originalSprite);
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





