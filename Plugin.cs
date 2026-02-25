using Archipelago.MultiClient.Net.Models;
using BepInEx;
using BepInEx.Logging;
using Fakutori.Dialogue;
using Fakutori.Grid;
using FakutoriArchipelago.Archipelago;
using FakutoriArchipelago.Utils;
using HarmonyLib;
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
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.UIElements;
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
    public static List<long> UnlockedElements = new();
    public static List<long> BlockIdsThatAreNotLocations = new();

    public static void AddToPendingItems(ItemInfo item)
    {
        if (UnlockedElements.Contains(item.ItemId))
        {
            return;
        }
        PendingItems.Enqueue(item);
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

        UnlockedElements.Add(11);
        UnlockedElements.Add(14);
        UnlockedElements.Add(15);
        UnlockedElements.Add(16);
        BlockIdsThatAreNotLocations.Add(11);
        BlockIdsThatAreNotLocations.Add(14);
        BlockIdsThatAreNotLocations.Add(15);
        BlockIdsThatAreNotLocations.Add(16);


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
    [HarmonyPatch("UnlockMachineAtIndex")]
    [HarmonyPrefix]
    static bool PreUnlockMachineAtIndex(EditModeMenu __instance, int atIndex)
    {
        //TODO: put the UISprite back to normal once bought

        //TODO: alternatively, I could change what's in menuItems instead of replacing sprites?

        var menuItemsField = AccessTools.Field(typeof(EditModeMenu), "menuItems");
        var menuItems = (List<SelectMenuItem>)menuItemsField.GetValue(__instance);

        BlockData data = (menuItems[atIndex].tool as UnlockMachineBlockTool).blockData;
        AbstractSingleton<CurrencyManager>.Instance.SpendCurrencies(data.unlockCost, 0, null);
        AbstractSingleton<SFXManager>.Instance.PlayUISfx(SFX.Menus.UnlockCurrency, 0f, null);
        AbstractSingleton<ProgressManager>.Instance.UnlockMachine(data);
        if (data.tutorialDialogue != null)
        {
            AbstractSingleton<DialogueManager>.Instance.UnlockDialogue(data.tutorialDialogue);
        }
        UnityEngine.Object.Destroy(menuItems[atIndex].gameObject);
        menuItems.RemoveAt(atIndex);

        return false;

        //TODO: some of this code will have to be moved to the recieve item logic
        /*
        SelectMenuItem selectMenuItem = this.AddItem(data.uiSprite, data.blockName, data.moneyValue.ToString(), true, new PlaceMachineBlockTool(data), false, atIndex);
        this.UpdateItemsVisibility();
        menuItems[atIndex].ToggleSelected(true, true, false, false);
        selectMenuItem.PlayAnimation(SelectMenuItem.SelectMenuItemAnimation.Fill);
        this.ShortcutsContainer.UpdateShortcut(2, true);
        if (AbstractSingleton<GameplayManager>.Instance.state == GameplayManager.GameplayState.Edit && data.tutorialDialogue != null)
        {
            this.ToggleControls(false);
            AbstractSingleton<GameplayManager>.Instance.SetGameplayState(GameplayManager.GameplayState.Menu);
            AbstractSingleton<UIManager>.Instance.modalWindow.OpenModal(LocalizationManager.GetTranslation("ui/modal/machineUnlocked", true, 0, true, false, null, null, true), data.blockShortDescription, delegate
            {
                AbstractSingleton<GameplayManager>.Instance.SetGameplayState(GameplayManager.GameplayState.Cutscene);
                AbstractSingleton<BGMManager>.Instance.ShushGameplayBGM(0.5f);
                AbstractSingleton<UIManager>.Instance.CloseEditModeMenu();
                AbstractSingleton<UIManager>.Instance.ToggleHUDCurrencies(false);
                AbstractSingleton<DialogueManager>.Instance.StartTutorialDialogue(data.tutorialDialogue, true, null);
            }, delegate
            {
                this.ToggleUI(false);
                AbstractSingleton<UIManager>.Instance.OpenCompendiumOnItem(data);
            }, delegate
            {
                AbstractSingleton<GameplayManager>.Instance.SetGameplayState(GameplayManager.GameplayState.Edit);
                this.ToggleControls(true);
            }, LocalizationManager.GetTranslation("ui/modal/machineUnlockedTutorial", true, 0, true, false, null, null, true), LocalizationManager.GetTranslation("ui/modal/machineUnlockedCompendium", true, 0, true, false, null, null, true), LocalizationManager.GetTranslation("ui/modal/close", true, 0, true, false, null, null, true), ModalWindow.ModalIllustration.UnlockMachine, false, false, false, HorizontalAlignmentOptions.Center, HorizontalAlignmentOptions.Center, ModalWindow.ModalSize.Small, 0f);
        }
        return false;
        */
    }


}

[HarmonyPatch(typeof(ProgressManager))]
internal class UnlockBlockPatch
{
    static BlocksManager blocksManager;
    static bool AllowOnElementBlockSpawned = false;
    public static Dictionary<long, Sprite> BlockSprites;

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


        bool isUnlocked = Plugin.UnlockedElements.Contains(blockData.blockId);
        bool uncheckedLocation = !ArchipelagoClient.ServerData.CheckedLocations.Contains(blockData.blockId) && !Plugin.BlockIdsThatAreNotLocations.Contains(blockData.blockId);

        if (uncheckedLocation)
        {
            // Use the session instance to complete the location check for this block id.
            //Plugin.ArchipelagoClient.session.Locations.ScoutLocationsAsync(blockData.blockId).ContinueWith(task =>
            //{
            //    if (task.IsCompletedSuccessfully)
            //    {
            //        var result = task.Result;
            //        if (result.TryGetValue(blockData.blockId, out var itemInfo))
            //        {
            //            Plugin.BepinLogger.LogInfo($"Scouted location {blockData.blockId}, got item {itemInfo.ItemId}");

            //            //AbstractSingleton<Notifications>.Instance.QueueNotification(NotificationType.NewBlock, blockData, null);
            //        }
            //        else
            //        {
            //            Plugin.BepinLogger.LogWarning($"Scouted location {blockData.blockId} but it was not found in the result!");
            //        }
            //    }
            //    else
            //    {
            //        Plugin.BepinLogger.LogError($"Failed to scout location {blockData.blockId}: {task.Exception}");
            //    }
            //});

            Plugin.ArchipelagoClient.session.Locations.CompleteLocationChecksAsync(blockData.blockId).ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    ArchipelagoClient.ServerData.CheckedLocations.Add(blockData.blockId);
                }
                else
                {
                    Plugin.BepinLogger.LogError($"Failed to complete location check for {blockData.blockId}: {task.Exception}");
                }
            });
        }

        if (!Plugin.UnlockedElements.Contains(blockData.blockId))
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


        //TODO: retrieve progress when joining existing
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

    static void OnGameTick()
    {
        if (!Plugin.didInitShop && ArchipelagoClient.Authenticated)
        {
            //Populate the shop
            List<string> shopLocations = new List<string>();
            shopLocations.Add("Disassembler");
            shopLocations.Add("Puller");
            shopLocations.Add("Pusher");
            shopLocations.Add("Toggle conveyor");
            shopLocations.Add("Cross conveyor");

            var lib = Resources.FindObjectsOfTypeAll<BlocksLibrary>()[0];
            var MachineBlocksField = AccessTools.Field(typeof(BlocksLibrary), "MachineBlocks");
            var MachineBlocks = (BlockData[])MachineBlocksField.GetValue(lib);

            List<long> ShopLocations = new List<long>();
            foreach (var blockData in MachineBlocks)
            {
                if (shopLocations.Contains(blockData.blockName))
                {
                    ShopLocations.Add(blockData.blockId);
                }
            }

            //var EditModeMenu = Resources.FindObjectsOfTypeAll<EditModeMenu>()[0];
            //var menuItemsField = AccessTools.Field(typeof(EditModeMenu), "menuItems");
            //var menuItems = (List<SelectMenuItem>)menuItemsField.GetValue(EditModeMenu);


            Plugin.ArchipelagoClient.session.Locations.ScoutLocationsAsync(ShopLocations.ToArray()).ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    var result = task.Result;
                    foreach (var pair in result)
                    {
                        long id = pair.Key;
                        ItemInfo itemInfo = pair.Value;

                        var replacingBlockData = lib.GetBlockDataById((int)itemInfo.ItemId);
                        var replacingSprite = BlockSprites[(int)itemInfo.ItemId];
                        var uiSpriteField = AccessTools.Field(typeof(BlockData), "UISprite");
                        var blockData = lib.GetBlockDataById((int)id);
                        uiSpriteField.SetValue(blockData, replacingSprite);
                        Plugin.BepinLogger.LogError($"Block {blockData.blockName} now has the sprite of block {replacingBlockData}");

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

        if (AbstractSingleton<GameplayManager>.Instance.state != GameplayManager.GameplayState.Watch)
        {
            return;
        }

        ItemInfo item;
        if (Plugin.PendingItems.TryDequeue(out item))
        {
            Plugin.UnlockedElements.Add(item.ItemId);

            var lib = Resources.FindObjectsOfTypeAll<BlocksLibrary>()[0];
            var blockData = lib.GetBlockDataById((int)item.ItemId);
            if (BlockSprites.ContainsKey(item.LocationId))
            {
                var originalSprite = BlockSprites[(int)item.LocationId];
                var uiSpriteField = AccessTools.Field(typeof(BlockData), "UISprite");
                uiSpriteField.SetValue(blockData, originalSprite);
            }
            else if (item.LocationId > -2)
            {
                Plugin.BepinLogger.LogWarning($"No sprite found for block id {item.LocationId}");
            }


            if (blockData.category.name == "Machine")
            {
                AbstractSingleton<Notifications>.Instance.QueueNotification(NotificationType.NewBlock, blockData, null);
                AbstractSingleton<ProgressManager>.Instance.UnlockMachine(blockData);
            }
            else
            {
                AllowOnElementBlockSpawned = true;
                AbstractSingleton<ProgressManager>.Instance.OnElementBlockSpawned(blockData, null);
                AllowOnElementBlockSpawned = false;
            } 
           
        }

    }

}





