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
using static UnityEngine.UIElements.UIR.BestFitAllocator;

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
    public static bool didInitRecipes = false;


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

[HarmonyPatch(typeof(BlockData))]
class BlockNamePropertyPatch
{
    [HarmonyPatch("blockName", MethodType.Getter)]
    static bool Prefix(BlockData __instance, ref string __result)
    {
        var NameKeyField = AccessTools.Field(typeof(BlockData), "NameKey");
        var NameKey = (string)NameKeyField.GetValue(__instance);
        if (NameKey.StartsWith("custom_"))
        {
            __result = NameKey.Substring("custom_".Length);
            return false;
        }
        return true;
    }

    [HarmonyPatch("blockName", MethodType.Getter)]
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
            if (!ProgressManagerPatch.ShopLocations.TryGetValue(shoplocationId, out itemInfo))
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

                    BlockData blockForSale = ProgressManagerPatch.BaseElementBlocks[itemInfo.ItemId];
                    var unlockCostField = AccessTools.Field(typeof(BlockData), "UnlockCost");
                    unlockCostField.SetValue(blockForSale, ProgressManagerPatch.BlockUnlockCosts[originalMachineBlock.blockId] / 10);
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
            BlockData blockForSale = ProgressManagerPatch.BaseElementBlocks[itemInfo.ItemId];

            SelectionTool selectionTool = new UnlockMachineBlockTool(blockForSale);

            ProgressManagerPatch.ShopLocationIdFromBlockData.TryAdd(blockForSale, itemInfo.LocationId);

            AddItemMethod.Invoke(__instance, new object[] {
                ProgressManagerPatch.BlockSprites[blockForSale.blockId],
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
        // var GameplayManager = AbstractSingleton<GameplayManager>.Instance;
        // GameplayManager.ToggleEdit(false); // <- buggy

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

[HarmonyPatch(typeof(BlocksLibrary))]
internal class BlocksLibraryPatch
{
    public static Dictionary<int, BlockData> CustomBlockData = new();


    static void FilterRecipe(ref ValueTuple<int, Recipe> __result, int blockCount)
    {
        if (__result.Item2 != null)
        {
            int blockId = __result.Item2.product.blockId;
            if (!Plugin.UnlockedItemIds.Contains(blockId))
            {
                __result = new ValueTuple<int, Recipe>(blockCount, null);
            }
        }
    }

    [HarmonyPatch("GetBlockDataById")]
    [HarmonyPrefix]
    public static bool PreGetBlockDataById(BlocksLibrary __instance, int id, ref BlockData __result)
    {
        if (CustomBlockData.TryGetValue(id, out var blockData))
        {
            __result = blockData;
            return false;
        }

        if (ProgressManagerPatch.BaseElementBlocks.TryGetValue(id, out blockData))
        {
            __result = blockData;
            return false;
        }
        return true;
    }

    [HarmonyPatch("GetRecipeFromIngredients")]
    [HarmonyPostfix]
    public static void PostGetRecipeFromIngredients(BlocksLibrary __instance, List<ElementBlock> blocks, SortedDictionary<int, RecipeTree> recipes, ref ValueTuple<int, Recipe> __result)
    {
        FilterRecipe(ref __result, blocks.Count);
    }

    [HarmonyPatch("GetCombineRecipeFromIngredients")]
    [HarmonyPostfix]
    public static void PostGetCombineRecipeFromIngredients(BlocksLibrary __instance, List<ElementBlock> blocks, ref ValueTuple<int, Recipe> __result)
    {
        FilterRecipe(ref __result, blocks.Count);
    }

    [HarmonyPatch("fallProduct", MethodType.Getter)]
    [HarmonyPostfix]
    public static void PostGetFallProduct(BlockData __instance, ref BlockData __result)
    {
        __result = Plugin.UnlockedItemIds.Contains(__result.blockId) ? __result : null;
    }

    [HarmonyPatch("riseProduct", MethodType.Getter)]
    [HarmonyPostfix]
    public static void PostGetRiseProduct(BlockData __instance, ref BlockData __result)
    {
        __result = Plugin.UnlockedItemIds.Contains(__result.blockId) ? __result : null;
    }
}

/*
[HarmonyPatch(typeof(RecipeIngredient))]
class RecipeIngredientPatch
{
    [HarmonyPatch("IsIngredientAvailable")]
    [HarmonyPrefix]
    public static bool PreIsIngredientAvailable(RecipeIngredient __instance, ref bool __result)
    {
        var IngredientTypeField = AccessTools.Field(typeof(RecipeIngredient), "IngredientType");
        var ColorField = AccessTools.Field(typeof(RecipeIngredient), "Color");
        var IngredientType = (RecipeIngredient.RecipeIngredientType)IngredientTypeField.GetValue(__instance);
        var Color = (BlockColor)ColorField.GetValue(__instance);


        switch (IngredientType)
        {
            case RecipeIngredient.RecipeIngredientType.Block:
                Plugin.BepinLogger.LogInfo($"Checking if block {__instance.block.blockName} is unlocked");
                __result = AbstractSingleton<ProgressManager>.Instance.GetProgress(__instance.block).isUnlocked;
                return false;
            case RecipeIngredient.RecipeIngredientType.Property:
                Plugin.BepinLogger.LogInfo($"Checking if property {__instance.property} is unlocked");
                __result = AbstractSingleton<ProgressManager>.Instance.UnlockedBlocksCount((BlockData x) => x.HasProperty(__instance.property)) >= __instance.quantity;
                return false;
            case RecipeIngredient.RecipeIngredientType.Color:
                Plugin.BepinLogger.LogInfo($"Checking if color {Color.colorName} is unlocked");
                __result = AbstractSingleton<ProgressManager>.Instance.UnlockedBlocksCount((BlockData x) => x.color == Color) >= __instance.quantity;
                return false;
            case RecipeIngredient.RecipeIngredientType.Any:
                Plugin.BepinLogger.LogInfo($"Checking if any block is unlocked");
                __result = true;
                return false;
        }
        Plugin.BepinLogger.LogWarning($"Unknown ingredient type {IngredientType}");
        __result = false;
        return false;
    }
}
*/

/*
[HarmonyPatch(typeof(Compendium))]
class CompendiumPatch
{
    [HarmonyPatch("FillBlockList")]
    [HarmonyPostfix]
    public static void FillBlockList(Compendium __instance)
    {
        Plugin.BepinLogger.LogInfo($"Post FillBlockList");
        var ElementBlocksField = AccessTools.Field(typeof(BlocksLibrary), "ElementBlocks");
        var lib = Resources.FindObjectsOfTypeAll<BlocksLibrary>()[0];
        var ElementBlocks = (BlockData[])ElementBlocksField.GetValue(lib);
        Plugin.BepinLogger.LogInfo($"Element blocks count in lib: {ElementBlocks.Length}");
    }

}
*/

[HarmonyPatch(typeof(ProgressManager))]
internal class ProgressManagerPatch
{
    static BlocksManager blocksManager;
    static bool AllowOnElementBlockSpawned = false;
    public static Dictionary<long, Sprite> BlockSprites;
    public static Dictionary<long, int> BlockUnlockCosts;
    public static Dictionary<long, ItemInfo> ShopLocations = new();
    public static Dictionary<BlockData, long> ShopLocationIdFromBlockData = new Dictionary<BlockData, long>();

    public static Dictionary<long, BlockData> BaseElementBlocks = new();

    /*
    [HarmonyPatch("ResetAllProgress")]
    [HarmonyPrefix]
    static bool PreResetAllProgress(ProgressManager __instance)
    {
        if (!ArchipelagoClient.Authenticated)
        {
            return true;
        }
        var blocksProgressField = AccessTools.Field(typeof(ProgressManager), "blocksProgress");
        var spawnLegendariesField = AccessTools.Field(typeof(ProgressManager), "spawnedLegendaries");
        var challengesCompletedProperty = AccessTools.Property(typeof(ProgressManager), "challengesCompleted");
        var ResetAllBlockChallengesMethod = AccessTools.Method(typeof(ProgressManager), "ResetAllBlockChallenges");
        var unlockedMachinesField = AccessTools.Field(typeof(ProgressManager), "unlockedMachines");
        var activeFlagsField = AccessTools.Field(typeof(ProgressManager), "activeFlags");

        var this_blocksProgress = (SortedDictionary<int, BlockProgress>)blocksProgressField.GetValue(__instance);
        var this_spawnedLegendaries = (HashSet<LegendaryBlocks>)spawnLegendariesField.GetValue(__instance);
        var this_ResetAllBlockChallenges = (Action)ResetAllBlockChallengesMethod.CreateDelegate(typeof(Action), __instance);
        var this_unlockedMachines = (HashSet<int>)unlockedMachinesField.GetValue(__instance);
        var this_activeFlags = (HashSet<ProgressFlag>)activeFlagsField.GetValue(__instance);


        this_blocksProgress.Clear();
        this_spawnedLegendaries.Clear();
        challengesCompletedProperty.SetValue(__instance, 0);
        this_ResetAllBlockChallenges();
        foreach (BlockData blockData in BaseElementBlocks.Values.Concat(AbstractSingleton<BlocksManager>.Instance.blocksLibrary.machineBlocks))
        {
            BlockProgress blockProgress = new BlockProgress();
            blockProgress.isUnlocked = blockData.unlockedByDefault;
            blockProgress.isNewlyUnlocked = blockData.unlockedByDefault;
            this_blocksProgress.Add(blockData.blockId, blockProgress);
        }
        this_unlockedMachines.Clear();
        this_activeFlags.Clear();

        return false;
    }
    */

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

        bool uncheckedLocation = !Plugin.BlockIdsThatAreNotLocations.Contains(blockData.blockId)
            && !ArchipelagoClient.session.Locations.AllLocationsChecked.Contains(blockData.blockId);


        if (uncheckedLocation)
        {
            Plugin.DoCheck(blockData.blockId);
        }

        return true;
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

    public static void PrintElementCount()
    {
        var ElementBlocksField = AccessTools.Field(typeof(BlocksLibrary), "ElementBlocks");
        var lib = Resources.FindObjectsOfTypeAll<BlocksLibrary>()[0];
        var ElementBlocks = (BlockData[])ElementBlocksField.GetValue(lib);
        Plugin.BepinLogger.LogInfo($"Element blocks count in lib: {ElementBlocks.Length}");
    }

    public static void ApplyUnlock(BlockData blockData)
    {
        if (blockData.category.name != "Machine")
        {
            var lib = Resources.FindObjectsOfTypeAll<BlocksLibrary>()[0];
            var ElementBlocksField = AccessTools.Field(typeof(BlocksLibrary), "ElementBlocks");
            var ElementBlocks = (BlockData[])ElementBlocksField.GetValue(lib);

            if (ElementBlocks.ToList().Find(block => block.blockId == blockData.blockId))
            {
                // It's already unlocked...
                if (!Plugin.UnlockedItemIds.Contains(blockData.blockId))
                {
                    Plugin.UnlockedItemIds.Add(blockData.blockId);
                    Plugin.BepinLogger.LogWarning($"impossible data mismatch");
                }
                return;
            }

            // Add item to compendium and blocksProgress
            var modifiedElementBlocks = ElementBlocks.Concat(new BlockData[] { blockData }).ToArray();
            ElementBlocksField.SetValue(lib, modifiedElementBlocks);

            ProgressManager progressManager = AbstractSingleton<ProgressManager>.Instance;
            var blocksProgressField = AccessTools.Field(typeof(ProgressManager), "blocksProgress");
            var blocksProgress = (SortedDictionary<int, BlockProgress>)blocksProgressField.GetValue(progressManager);
            var blockProgress = new BlockProgress();
            blockProgress.isUnlocked = false;
            blockProgress.isNewlyUnlocked = false;
            blocksProgress.Add(blockData.blockId, blockProgress);

            string actualName = LocalizationManager.GetTranslation("blockName/" + blockData.nameKey, true, 0, true, false, null, null, true);

            // Insert a special custom block data that will be picked up by the notification when doing getblockdatabyid
            int count = BlocksLibraryPatch.CustomBlockData.Count();
            int customId = -(count + 1);
            var customBlockData = ScriptableObject.Instantiate(blockData);

            var BlockIdField = AccessTools.Field(typeof(BlockData), "BlockId");
            BlockIdField.SetValue(customBlockData, customId);

            // Today, only uiSprite, icon, and blockName are used. We have a hook on blockName so we put our desired name here.
            string unlockText = $"Recipe(s) for {actualName}";
            var NameKeyField = AccessTools.Field(typeof(BlockData), "NameKey");
            NameKeyField.SetValue(customBlockData, $"custom_{unlockText}");

            BlocksLibraryPatch.CustomBlockData[blockData.blockId] = customBlockData;

            // In this line, blockData is used only to access id
            AbstractSingleton<Notifications>.Instance.QueueNotification(NotificationType.NewBlock, blockData, null);
            Plugin.BepinLogger.LogInfo($"Unlocked element block {blockData.blockName}");
        }
        else
        {
            AbstractSingleton<Notifications>.Instance.QueueNotification(NotificationType.NewBlock, blockData, null);
            AbstractSingleton<ProgressManager>.Instance.UnlockMachine(blockData);
            Plugin.BepinLogger.LogInfo($"Unlocked machine block {blockData.blockName}");
        }

        return;
    }



    static void OnGameTick()
    {
        if (!Plugin.didInitRecipes && ArchipelagoClient.Authenticated)
        {
            // Remove all unlocked blocks from game
            BlocksLibrary lib = AbstractSingleton<BlocksManager>.Instance.blocksLibrary;
            var ElementBlocksField = AccessTools.Field(typeof(BlocksLibrary), "ElementBlocks");
            var ElementBlocks = (BlockData[])ElementBlocksField.GetValue(lib);
            BaseElementBlocks = ElementBlocks.ToDictionary(v => (long)v.blockId, v => v);
            var modifiedElementsBlocks = ElementBlocks.Where(blockData => Plugin.UnlockedItemIds.Contains(blockData.blockId)).ToArray();
            ElementBlocksField.SetValue(lib, modifiedElementsBlocks);

            Plugin.didInitRecipes = true;
        }

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
            BlockDumper.Do();
            Plugin.didBlockDump = true;
        }

        if (!Plugin.didRecipeDump)
        {
            RecipeDumper.Do();
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

            if (BaseElementBlocks.TryGetValue(itemInfo.ItemId, out BlockData blockData))
            {
                ApplyUnlock(blockData);
            }
            else
            {
                //It must be a machine block.
                var lib = Resources.FindObjectsOfTypeAll<BlocksLibrary>()[0];
                var machineBlockData = lib.GetBlockDataById((int)itemInfo.ItemId);
                ApplyUnlock(machineBlockData);
            }

        }

    }

}





