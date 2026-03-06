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
// using UnityEngine.UIElements;
using TMPro;
using UnityEngine.InputSystem;
using Fakutori.VFX;
using Archipelago.MultiClient.Net.Helpers;
using UnityEngine.Analytics;

namespace FakutoriArchipelago;


//TODO: clear progress when loading/starting game

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
    public static bool didInitUnlocks = false;
    public static bool didInitIcons = false;

    public static ConcurrentQueue<ItemInfo> PendingItems = new();
    public static List<ItemInfo> ReceivedItemHistory = new();
    public static ConcurrentQueue<SentItemInfo> PendingSentItems = new();
    public static List<long> UnlockedItemIds = new();
    public static List<long> BlockIdsThatAreNotLocations = new();

    public static void AddToPendingSentItems(ItemInfo item, PlayerInfo recipient)
    {
        if (recipient.Name == ArchipelagoClient.ServerData.SlotName)
        {
            return;
        }

        var sentItemInfo = new SentItemInfo()
        {
            Item = item,
            Recipient = recipient
        };
        PendingSentItems.Enqueue(sentItemInfo);
    }
    public static void AddToPendingItems(ItemInfo item)
    {
        ReceivedItemHistory.Add(item);
        if (UnlockedItemIds.Contains(item.ItemId))
        {
            return;
        }
        PendingItems.Enqueue(item);
    }

    public static void OnAuthenticated()
    {
        // Doing anything here seems to crash the game
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

        BlockIdsThatAreNotLocations.Add(11);
        BlockIdsThatAreNotLocations.Add(14);
        BlockIdsThatAreNotLocations.Add(15);
        BlockIdsThatAreNotLocations.Add(16);

        ResetUnlocks();
    }

    public static void ResetUnlocks()
    {
        UnlockedItemIds.Clear();
        // Base Elements
        UnlockedItemIds.Add(11);
        UnlockedItemIds.Add(14);
        UnlockedItemIds.Add(15);
        UnlockedItemIds.Add(16);
    }

}

[HarmonyPatch(typeof(ElementBlock))]
class ElementBlockPatch
{

    [HarmonyPatch("TransformInto")]
    [HarmonyPrefix]
    static bool PreTransformInto(ElementBlock __instance, BlockData newBlock, VFX vfx)
    {
        if (!ArchipelagoClient.Authenticated)
        {
            return true;
        }

        if (newBlock == null)
        {
            return true;
        }

        if (Plugin.UnlockedItemIds == null || !Plugin.UnlockedItemIds.Contains(newBlock.blockId))
        {
            // newBlock = null; 
            return false;

        }
        return true;
    }

    [HarmonyPatch("Fall")]
    [HarmonyPrefix]
    public static bool PreFall(ElementBlock __instance)
    {
        var fallDurationField = AccessTools.Field(typeof(ElementBlock), "fallDuration");
        var fallDuration = (int)fallDurationField.GetValue(__instance);
        if (fallDuration >= 10)
        {
            if (__instance.blockData.HasProperty(BlockProperty.Volatile))
            {
                return true;
            }
            BlockData blockData = (
                __instance.blockData.HasProperty(BlockProperty.Aerial)
                ? AbstractSingleton<BlocksManager>.Instance.blocksLibrary.riseProduct
                : (
                    __instance.blockData.HasProperty(BlockProperty.Weightless)
                    ? null
                    : AbstractSingleton<BlocksManager>.Instance.blocksLibrary.fallProduct
                )
            );
            if (blockData == null || !Plugin.UnlockedItemIds.Contains(blockData.blockId))
            {
                __instance.TransformInto(null);
                return false;
            }
            return true;
        }
        else
        {
            return true;
        }
    }
}

[HarmonyPatch(typeof(Notifications))]
class NotificationsPatch
{

    public static bool AllowQueueNotification = false;

    [HarmonyPatch("QueueNotification")]
    [HarmonyPrefix]
    static bool PreQueueNotification(Notifications __instance, NotificationType type, BlockData block, GridCell onCell)
    {
        if (AllowQueueNotification)
        {
            return true;
        }
        return false;
    }


}

[HarmonyPatch(typeof(BlockData))]
class BlockNamePropertyPatch
{
    [HarmonyPatch("blockName", MethodType.Getter)]
    [HarmonyPrefix]
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
}

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
                    if (itemInfo.ItemId >= 1000)
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

                    BlockData blockForSale = ProgressManagerPatch.BaseElementBlocks[itemInfo.ItemId];
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
            else if (itemInfo.ItemId >= 1000)
            {
                blockForSale = ScriptableObject.CreateInstance<BlockData>();
                BlockIdField.SetValue(blockForSale, (int)itemInfo.ItemId);
                UnlockCostField.SetValue(blockForSale, ProgressManagerPatch.BlockUnlockCosts[itemInfo.LocationId]);
                if (itemInfo.ItemId == 1000)
                {
                    NameKeyField.SetValue(blockForSale, $"custom_500 gold");
                    sprite = MoneySprite;
                }
                else if (itemInfo.ItemId == 1001)
                {
                    NameKeyField.SetValue(blockForSale, $"custom_1000 gold");
                    sprite = MoneySprite;
                }
                else if (itemInfo.ItemId == 1002)
                {
                    NameKeyField.SetValue(blockForSale, $"custom_500 mana");
                    sprite = ManaSprite;
                }
                else if (itemInfo.ItemId == 1003)
                {
                    NameKeyField.SetValue(blockForSale, $"custom_Full starpower");
                    sprite = StarPowerSprite;
                }
            }
            else
            {
                blockForSale = ProgressManagerPatch.BaseElementBlocks[itemInfo.ItemId];
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

[HarmonyPatch(typeof(BlocksLibrary))]
internal class BlocksLibraryPatch
{
    public static Dictionary<int, BlockData> CustomBlockData = new();
    public static Dictionary<int, ItemInfo> CustomBlockDataItemInfo = new();
    public static Dictionary<int, string> CustomBlockDataPlayerName = new();
    public static Dictionary<int, BlockData> CustomBlockDataOriginal = new();


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

    [HarmonyPatch("ContributesToUnlockedBlocksCount")]
    [HarmonyPrefix]
    public static bool PreContributesToUnlockedBlocksCount(BlocksLibrary __instance, int blockId, ref bool __result)
    {
        BlockData blockDataById = __instance.GetBlockDataById(blockId);
        if (blockDataById == null)
        {
            __result = false;
            return false;
        }

        return true;
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

[HarmonyPatch(typeof(Notification))]
class NotificationPatch
{

    [HarmonyPatch("OpenCompendium")]
    [HarmonyPrefix]
    static bool PreOpenCompendium(Notification __instance)
    {
        var blockDataField = AccessTools.Field(typeof(Notification), "blockData");
        var blockData = (BlockData)blockDataField.GetValue(__instance);

        if (BlocksLibraryPatch.CustomBlockDataOriginal.TryGetValue(blockData.blockId, out var actualBlockData))
        {
            AbstractSingleton<UIManager>.Instance.OpenCompendiumOnItem(actualBlockData);
            return false;
        }
        return true;
    }

    [HarmonyPatch("Init")]
    [HarmonyPrefix]
    static bool PreInit(Notification __instance, SerializedNotification data)
    {
        var BindShortcutMethod = AccessTools.Method(typeof(Notification), "BindShortcuts");
        var BindShortcuts = (Action)Delegate.CreateDelegate(typeof(Action), __instance, BindShortcutMethod);

        BindShortcuts();

        var blockDataField = AccessTools.Field(typeof(Notification), "blockData");
        var sourceLocationField = AccessTools.Field(typeof(Notification), "sourceLocation");
        var hasSourceLocationField = AccessTools.Field(typeof(Notification), "hasSourceLocation");
        var BlockImageField = AccessTools.Field(typeof(Notification), "BlockImage");
        var BadgeImageField = AccessTools.Field(typeof(Notification), "BadgeImage");
        var BlockSymbolField = AccessTools.Field(typeof(Notification), "BlockSymbol");
        var TypeTextField = AccessTools.Field(typeof(Notification), "TypeText");
        var BlockNameTextField = AccessTools.Field(typeof(Notification), "BlockNameText");
        var LocateButtonField = AccessTools.Field(typeof(Notification), "LocateButton");
        var ButtonsContainerTransformField = AccessTools.Field(typeof(Notification), "ButtonsContainerTransform");
        var RootTransformField = AccessTools.Field(typeof(Notification), "RootTransform");
        var CompendiumButtonField = AccessTools.Field(typeof(Notification), "CompendiumButton");
        var DismissButtonField = AccessTools.Field(typeof(Notification), "DismissButton");
        var controlsField = AccessTools.Field(typeof(Notification), "controls");
        var LocateSourceMethod = AccessTools.Method(typeof(Notification), "LocateSource");
        var OpenCompendiumMethod = AccessTools.Method(typeof(Notification), "OpenCompendium");
        var DismissMethod = AccessTools.Method(typeof(Notification), "Dismiss");

        var BlockImage = (Image)BlockImageField.GetValue(__instance);
        var BadgeImage = (Image)BadgeImageField.GetValue(__instance);
        var BlockSymbol = (Image)BlockSymbolField.GetValue(__instance);
        var CompendiumButton = (UIShortcutButton)CompendiumButtonField.GetValue(__instance);
        var DismissButton = (UIShortcutButton)DismissButtonField.GetValue(__instance);
        var TypeText = (TMP_Text)TypeTextField.GetValue(__instance);
        var BlockNameText = (TMP_Text)BlockNameTextField.GetValue(__instance);
        var LocateButton = (UIShortcutButton)LocateButtonField.GetValue(__instance);
        var ButtonsContainerTransform = (RectTransform)ButtonsContainerTransformField.GetValue(__instance);
        var RootTransform = (RectTransform)RootTransformField.GetValue(__instance);
        var controls = (GameControls)controlsField.GetValue(__instance);
        var LocateSource = (UnityAction)Delegate.CreateDelegate(typeof(UnityAction), __instance, LocateSourceMethod);
        var OpenCompendium = (UnityAction)Delegate.CreateDelegate(typeof(UnityAction), __instance, OpenCompendiumMethod);
        var Dismiss = (UnityAction)Delegate.CreateDelegate(typeof(UnityAction), __instance, DismissMethod);

        var blockData = AbstractSingleton<BlocksManager>.Instance.blocksLibrary.GetBlockDataById(data.BlockId);
        blockDataField.SetValue(__instance, blockData);
        sourceLocationField.SetValue(__instance, data.SourceLocation);
        var hasSourceLocation = data.HasSourceLocation;
        hasSourceLocationField.SetValue(__instance, hasSourceLocation);
        BlockImage.sprite = blockData.uiSprite;
        BadgeImage.sprite = (data.Type == NotificationType.NewBlock) ? AbstractSingleton<Notifications>.Instance.newBlockBadge : AbstractSingleton<Notifications>.Instance.challengeCompletedBadge;
        BlockSymbol.sprite = blockData.icon;

        bool activeShowInCompendium = true;

        if (blockData.blockId < 0)
        {
            ItemInfo itemInfo = BlocksLibraryPatch.CustomBlockDataItemInfo[blockData.blockId];
            string playerName = BlocksLibraryPatch.CustomBlockDataPlayerName.TryGetValue(blockData.blockId, out var name) ? name : null;

            if (playerName == null)
            {
                playerName = itemInfo.Player.Name == ArchipelagoClient.ServerData.SlotName ? "self" : itemInfo.Player.Name;
            }

            if (data.Type == NotificationType.ChallengeCompleted)
            {
                TypeText.text = LocalizationManager.GetTranslation("notifications/sentItem") + playerName;
                activeShowInCompendium = false;
            }
            else
            {
                TypeText.text = LocalizationManager.GetTranslation("notifications/newItem") + playerName;
            }
        }
        else
        {
            TypeText.text = LocalizationManager.GetTranslation("notifications/" + data.Type.ToString().Decapitalize());
        }

        BlockNameText.text = blockData.blockName;
        UIShortcutButton locateButton = LocateButton;
        InputAction[] inputActions = [controls.menus.notificationLocate];
        UnityAction val = LocateSource;
        locateButton.Init(labelKey: null, label: LocalizationManager.GetTranslation("notifications/locate"), inputActions: (InputAction[])(object)inputActions, action: val, disabled: !hasSourceLocation);
        CompendiumButton.Init((InputAction[])(object)new InputAction[1] { controls.menus.notificationCompendium }, new UnityAction(OpenCompendium), null, disabled: !activeShowInCompendium, LocalizationManager.GetTranslation("notifications/compendium"));
        DismissButton.Init((InputAction[])(object)new InputAction[1] { controls.menus.notificationDismiss }, new UnityAction(Dismiss), null, disabled: false, LocalizationManager.GetTranslation("notifications/dismiss"));
        LayoutRebuilder.ForceRebuildLayoutImmediate(ButtonsContainerTransform);
        LayoutRebuilder.ForceRebuildLayoutImmediate(RootTransform);

        return false;
    }
}


[HarmonyPatch(typeof(LocalizationManager))]
class LocalizationManagerPatch
{
    [HarmonyPatch("GetTranslation")]
    [HarmonyPrefix]
    static bool PreGetTranslation(string Term, bool FixForRTL, int maxLineLengthForRTL, bool ignoreRTLnumbers, bool applyParameters, GameObject localParametersRoot, string overrideLanguage, bool allowLocalizedParameters, ref string __result)
    {
        if (Term.StartsWith("notifications/newItem"))
        {
            __result = "Item received from ";
            return false;
        }
        else if (Term.StartsWith("notifications/sentItem"))
        {
            __result = "Item sent to ";
            return false;
        }
        return true;
    }
}

[HarmonyPatch(typeof(BlocksManager))]
class BlocksManagerPatch
{
    [HarmonyPatch("SpawnLegendaryBlock")]
    [HarmonyPostfix]
    static void PostSpawnLegendaryBlock(BlocksManager __instance, GridCell spawnCell, BlockData blockData)
    {
        int quasarId = 50;
        if (blockData.blockId == quasarId)
        {
            Plugin.BepinLogger.LogInfo("A legendary block has spawned");
            bool uncheckedLocation = !ArchipelagoClient.session.Locations.AllLocationsChecked.Contains(quasarId);
            if (uncheckedLocation)
            {
                Plugin.DoCheck(quasarId);
            }
        }
    }
}

[HarmonyPatch(typeof(GameplayManager))]
class GameplayManagerPatch
{

    static void RestoreElementBlocsk()
    {
        if (ProgressManagerPatch.OriginalElementBlocks != null)
        {
            BlocksLibrary lib = AbstractSingleton<BlocksManager>.Instance.blocksLibrary;
            var ElementBlocksField = AccessTools.Field(typeof(BlocksLibrary), "ElementBlocks");
            ElementBlocksField.SetValue(lib, ProgressManagerPatch.OriginalElementBlocks);
        }
    }
    static void Reset()
    {
        Plugin.BepinLogger.LogInfo($"Resetting archipelago progress...");

        Plugin.ResetUnlocks();
        Plugin.didInitRecipes = false;
        Plugin.didInitUnlocks = false;
        Plugin.didInitShop = false;
        foreach (var item in Plugin.ReceivedItemHistory)
        {
            if (!Plugin.UnlockedItemIds.Contains(item.ItemId))
            {
                Plugin.PendingItems.Enqueue(item);
            }
        }
    }

    [HarmonyPatch("LoadGame")]
    [HarmonyPostfix]
    static void PostLoadGame(GameplayManager __instance)
    {
        Reset();

    }

    [HarmonyPatch("NewGame")]
    [HarmonyPostfix]
    static void PostNewGame(GameplayManager __instance)
    {
        Reset();
    }

    [HarmonyPatch("LoadGame")]
    [HarmonyPrefix]
    static void PreLoadGame(GameplayManager __instance)
    {
        RestoreElementBlocsk();

    }

    [HarmonyPatch("NewGame")]
    [HarmonyPrefix]
    static void PreNewGame(GameplayManager __instance)
    {
        RestoreElementBlocsk();
    }
}

[HarmonyPatch(typeof(ProgressManager))]
internal class ProgressManagerPatch
{
    static BlocksManager blocksManager;
    static bool AllowOnElementBlockSpawned = false;
    public static Dictionary<long, Sprite> BlockSprites;
    public static Dictionary<long, int> BlockUnlockCosts = new Dictionary<long, int>();
    public static Dictionary<long, ItemInfo> ShopLocations = new();
    public static Dictionary<BlockData, long> ShopLocationIdFromBlockData = new Dictionary<BlockData, long>();

    public static Dictionary<long, BlockData> BaseElementBlocks = new();
    public static BlockData[] OriginalElementBlocks = null;

    [HarmonyPatch("SetItemSeen")]
    [HarmonyPrefix]
    static bool PreSetItemSeen(ProgressManager __instance, BlockData blockData)
    {
        if (blockData.blockId < 0)
        {
            return false;
        }
        return true;
    }



    [HarmonyPatch("OnBlockDespawned")]
    [HarmonyPrefix]
    static bool PreOnBlockDespawned(ProgressManager __instance, BlockData blockData)
    {
        if (AbstractSingleton<GameplayManager>.Instance.state != GameplayManager.GameplayState.Cutscene)
        {
            var blocksProgressField = AccessTools.Field(typeof(ProgressManager), "blocksProgress");
            var blocksProgress = (SortedDictionary<int, BlockProgress>)blocksProgressField.GetValue(__instance);
            if (blocksProgress.ContainsKey(blockData.blockId))
            {
                return true;
            }
            return false;
        }
        return true;
    }


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
        var ui = go.AddComponent<ArchipelagoUI>();

        ui.Init();
        AbstractSingleton<TimeManager>.Instance.OnTick.AddListener(ui.OnTick);

        var ElementBlocksField = AccessTools.Field(typeof(BlocksLibrary), "ElementBlocks");
        var MachineBlocksField = AccessTools.Field(typeof(BlocksLibrary), "MachineBlocks");
        var lib = Resources.FindObjectsOfTypeAll<BlocksLibrary>()[0];
        var ElementBlocks = (BlockData[])ElementBlocksField.GetValue(lib);
        var MachineBlocks = (BlockData[])MachineBlocksField.GetValue(lib);

        foreach (var blockData in MachineBlocks)
        {
            if (blockData.blockId == 0)
            {
                var BlockIdField = AccessTools.Field(typeof(BlockData), "BlockId");
                BlockIdField.SetValue(blockData, 100); // Setting this block id to 100 because zero is not allowed by archipelago   
            }

            BlockUnlockCosts.Add(blockData.blockId, blockData.unlockCost);
        }

        BlockSprites = new Dictionary<long, Sprite>();
        foreach (var blockData in ElementBlocks.Concat(MachineBlocks))
        {
            BlockSprites.Add(blockData.blockId, blockData.uiSprite);
        }

        BaseElementBlocks = ElementBlocks.ToDictionary(v => (long)v.blockId, v => v);
        OriginalElementBlocks = ElementBlocks.ToList().ToArray();

    }


    public static void PrintElementCount()
    {
        var ElementBlocksField = AccessTools.Field(typeof(BlocksLibrary), "ElementBlocks");
        var lib = Resources.FindObjectsOfTypeAll<BlocksLibrary>()[0];
        var ElementBlocks = (BlockData[])ElementBlocksField.GetValue(lib);
        Plugin.BepinLogger.LogInfo($"Element blocks count in lib: {ElementBlocks.Length}");
    }

    public static void ApplyUnlock(BlockData blockData, ItemInfo itemInfo, bool unlockSilently = false)
    {
        Plugin.BepinLogger.LogInfo($"Applying unlock for block {blockData.blockName} with id {blockData.blockId} and category {blockData.category}");
        ProgressManager progressManager = AbstractSingleton<ProgressManager>.Instance;
        var lib = Resources.FindObjectsOfTypeAll<BlocksLibrary>()[0];

        if (blockData.category.name != "Machine")
        {
            var ElementBlocksField = AccessTools.Field(typeof(BlocksLibrary), "ElementBlocks");
            var ElementBlocks = (BlockData[])ElementBlocksField.GetValue(lib);

            if (ElementBlocks.ToList().Find(block => block.blockId == blockData.blockId))
            {
                // It's already unlocked...
                if (!Plugin.UnlockedItemIds.Contains(blockData.blockId))
                {
                    Plugin.UnlockedItemIds.Add(blockData.blockId);
                    Plugin.BepinLogger.LogWarning($"fixing data mismatch for element block");
                }
                return;
            }

            // Add item to compendium and blocksProgress
            var modifiedElementBlocks = ElementBlocks.Concat(new BlockData[] { blockData }).ToArray();
            ElementBlocksField.SetValue(lib, modifiedElementBlocks);

            var blocksProgressField = AccessTools.Field(typeof(ProgressManager), "blocksProgress");
            var blocksProgress = (SortedDictionary<int, BlockProgress>)blocksProgressField.GetValue(progressManager);
            var blockProgress = new BlockProgress
            {
                isUnlocked = false,
                isNewlyUnlocked = false
            };
            bool newProgress = blocksProgress.TryAdd(blockData.blockId, blockProgress);

            if (!newProgress)
            {
                Plugin.BepinLogger.LogInfo($"Block already in blocksProgress, is this a save file?");
                return;
            }
            string actualName = LocalizationManager.GetTranslation("blockName/" + blockData.nameKey, true, 0, true, false, null, null, true);

            // Insert a special custom block data that will be picked up by the notification when doing getblockdatabyid
            int count = BlocksLibraryPatch.CustomBlockData.Count();
            int customId = -(count + 1);
            var customBlockData = ScriptableObject.Instantiate(blockData);

            var BlockIdField = AccessTools.Field(typeof(BlockData), "BlockId");
            BlockIdField.SetValue(customBlockData, customId);

            // By default, only uiSprite, icon, and blockName are used. We have a hook on blockName so we put our desired name here.
            string unlockText = $"Recipe(s) for {actualName}";
            var NameKeyField = AccessTools.Field(typeof(BlockData), "NameKey");
            NameKeyField.SetValue(customBlockData, $"custom_{unlockText}");
            BlocksLibraryPatch.CustomBlockData[customId] = customBlockData;
            // Now I use itemInfo in the notification init code, so it needs to know where to find it. I'll put it in this static dictionary.
            BlocksLibraryPatch.CustomBlockDataItemInfo[customId] = itemInfo;
            // Add this one is for opening the compendium
            BlocksLibraryPatch.CustomBlockDataOriginal[customId] = blockData;

            // Mark it as discovered in the compendium if the player has already generated that block once (according to archipelago).
            if (ArchipelagoClient.session.Locations.AllLocationsChecked.Contains(blockData.blockId))
            {
                AllowOnElementBlockSpawned = true;
                progressManager.OnElementBlockSpawned(blockData, null);
                AllowOnElementBlockSpawned = false;
            }

            NotificationsPatch.AllowQueueNotification = true;
            // In this line, blockData is used only to access id
            AbstractSingleton<Notifications>.Instance.QueueNotification(NotificationType.NewBlock, customBlockData, null);
            Plugin.BepinLogger.LogInfo($"Unlocked element block {blockData.blockName}");
            NotificationsPatch.AllowQueueNotification = false;
        }
        else
        {
            if (!unlockSilently)
            {
                // Insert a special custom block data that will be picked up by the notification when doing getblockdatabyid
                int count = BlocksLibraryPatch.CustomBlockData.Count();
                int customId = -(count + 1);
                var customBlockData = ScriptableObject.Instantiate(blockData);

                var BlockIdField = AccessTools.Field(typeof(BlockData), "BlockId");
                BlockIdField.SetValue(customBlockData, customId);

                BlocksLibraryPatch.CustomBlockData[customId] = customBlockData;
                BlocksLibraryPatch.CustomBlockDataItemInfo[customId] = itemInfo;
                BlocksLibraryPatch.CustomBlockDataOriginal[customId] = blockData;

                NotificationsPatch.AllowQueueNotification = true;
                AbstractSingleton<Notifications>.Instance.QueueNotification(NotificationType.NewBlock, customBlockData, null);
                NotificationsPatch.AllowQueueNotification = false;
            }
            AbstractSingleton<ProgressManager>.Instance.UnlockMachine(blockData);
            Plugin.BepinLogger.LogInfo($"Unlocked machine block {blockData.blockName}");
        }

        return;
    }

    static void InitRecipes()
    {
        // Remove all unlocked blocks from game
        BlocksLibrary lib = AbstractSingleton<BlocksManager>.Instance.blocksLibrary;
        var ElementBlocksField = AccessTools.Field(typeof(BlocksLibrary), "ElementBlocks");
        var ElementBlocks = (BlockData[])ElementBlocksField.GetValue(lib);

        var modifiedElementsBlocks = ElementBlocks.Where(blockData =>
        {
            var ShowInCompendiumField = AccessTools.Field(typeof(BlockData), "ShowInCompendium");
            var ShowInCompendium = (bool)ShowInCompendiumField.GetValue(blockData);
            return Plugin.UnlockedItemIds.Contains(blockData.blockId) || !ShowInCompendium;
        }).ToArray();
        ElementBlocksField.SetValue(lib, modifiedElementsBlocks);
        ProgressManager progressManager = AbstractSingleton<ProgressManager>.Instance;
        // var blocksProgressField = AccessTools.Field(typeof(ProgressManager), "blocksProgress");
        // var blocksProgress = (SortedDictionary<int, BlockProgress>)blocksProgressField.GetValue(progressManager);
        // blocksProgress.Clear();
            
    }

    static void InitIcons()
    {
        var moneyIcon = GameObject.Find("UI/HUD/Currencies/Money/Badge/Icon");
        var manaIcon = GameObject.Find("UI/HUD/Currencies/Mana/Badge/Icon");
        var lazyIcon = GameObject.Find("UI/HUD/Objective menu button/Content/Lazy");
        EditModeMenuPatch.MoneySprite = moneyIcon.GetComponent<UnityEngine.UI.Image>().sprite;
        EditModeMenuPatch.ManaSprite = manaIcon.GetComponent<UnityEngine.UI.Image>().sprite;
        EditModeMenuPatch.StarPowerSprite = lazyIcon.GetComponentInChildren<UnityEngine.UI.Image>().sprite;

        var bytes = File.ReadAllBytes("BepInEx/plugins/FakutoriArchipelago/color-icon.png");
        var tex = new Texture2D(2, 2);
        tex.LoadImage(bytes);
        EditModeMenuPatch.ArchipelagoSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));

        bytes = File.ReadAllBytes("BepInEx/plugins/FakutoriArchipelago/white-icon.png");
        tex = new Texture2D(2, 2);
        tex.LoadImage(bytes);
        EditModeMenuPatch.ArchipelagoWhiteSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));

        bytes = File.ReadAllBytes("BepInEx/plugins/FakutoriArchipelago/icon.png");
        tex = new Texture2D(2, 2);
        tex.LoadImage(bytes);
        EditModeMenuPatch.ArchipelagoSDFSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
    }

    static void InitShop()
    {
        //Populate the shop
        ShopLocations.Clear();
        List<string> shopLocationNames = new List<string>() {
                "Disassembler",
                "Puller",
                "Pusher",
                "Conveyor alternate",
                "Cross conveyor",
            };

        var extraShopChecks = (long)ArchipelagoClient.ServerData.slotData["extra_shop_checks"];
        for (int i = 0; i < extraShopChecks; i++)
        {
            shopLocationNames.Add($"Extra shop {i + 1}");
        }

        var lib = Resources.FindObjectsOfTypeAll<BlocksLibrary>()[0];
        var MachineBlocksField = AccessTools.Field(typeof(BlocksLibrary), "MachineBlocks");
        var MachineBlocks = (BlockData[])MachineBlocksField.GetValue(lib);

        var shopLocationIds = new List<long>();

        shopLocationIds = shopLocationNames.Select(name =>
            ArchipelagoClient.session.Locations.GetLocationIdFromName("Fakutori", name)
        )
        .ToList();

        Plugin.BepinLogger.LogInfo($"Scouting shop locations with ids: {string.Join(", ", shopLocationIds)}");

        int unlockCost = 2000;

        ArchipelagoClient.session.Locations.ScoutLocationsAsync(shopLocationIds.ToArray()).ContinueWith(task =>
        {
            if (task.IsCompletedSuccessfully)
            {
                var result = task.Result;
                foreach (var pair in result)
                {
                    long id = pair.Key;
                    ItemInfo itemInfo = pair.Value;

                    ShopLocations[id] = itemInfo;
                    BlockUnlockCosts.TryAdd(itemInfo.LocationId, unlockCost);
                }

                try
                {

                    // Apply shop reduction
                    double shopPriceReductionPercent = ((long)ArchipelagoClient.ServerData.slotData["shop_price"]) / 100.0;
                    foreach (var k in BlockUnlockCosts.Keys.ToList())
                    {
                        long locationId = k;
                        int originalCost = BlockUnlockCosts[k];
                        int reducedCost = (int)(originalCost * shopPriceReductionPercent);
                        BlockUnlockCosts[locationId] = reducedCost;
                    }
                }
                catch (Exception ex)
                {
                    Plugin.BepinLogger.LogError(ex.Message);
                    throw;
                }
            }
            else
            {
                Plugin.BepinLogger.LogError($"Failed to scout locations");
            }
        });
    }

    static void InitUnlocks()
    {
        bool startWithDisassembler = ((long)ArchipelagoClient.ServerData.slotData["start_with_disassembler"]) == 1;
        bool startWithBaseMachines = ((long)ArchipelagoClient.ServerData.slotData["start_with_base_machines"]) == 1;

        if (startWithBaseMachines)
        {
            // Generators
            Plugin.UnlockedItemIds.Add(100);
            Plugin.UnlockedItemIds.Add(1);
            Plugin.UnlockedItemIds.Add(2);
            Plugin.UnlockedItemIds.Add(3);

            // Wall
            Plugin.UnlockedItemIds.Add(6);
            // Conveyor
            Plugin.UnlockedItemIds.Add(7);
            // Combiner
            Plugin.UnlockedItemIds.Add(4);
        }

        if (startWithDisassembler)
        {
            Plugin.UnlockedItemIds.Add(5);
        }
    }

    static void OnGameTick()
    {
        if (!Plugin.didInitUnlocks && ArchipelagoClient.Authenticated)
        {
            InitUnlocks();
            Plugin.didInitUnlocks = true;
        }
        if (!Plugin.didInitRecipes && ArchipelagoClient.Authenticated)
        {
            InitRecipes();
            Plugin.didInitRecipes = true;
        }
        if (!Plugin.didInitIcons && ArchipelagoClient.Authenticated)
        {
            InitIcons();
            Plugin.didInitIcons = true;
        }
        if (!Plugin.didInitShop && ArchipelagoClient.Authenticated)
        {
            InitShop();
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

        while (Plugin.PendingSentItems.TryDequeue(out SentItemInfo sentItemInfo))
        {
            int count = BlocksLibraryPatch.CustomBlockData.Count();
            int customId = -(count + 1);
            var customBlockData = ScriptableObject.CreateInstance<BlockData>();

            var BlockIdField = AccessTools.Field(typeof(BlockData), "BlockId");
            var UISpriteField = AccessTools.Field(typeof(BlockData), "UISprite");
            var IconField = AccessTools.Field(typeof(BlockData), "Icon");
            var NameKeyField = AccessTools.Field(typeof(BlockData), "NameKey");

            BlockIdField.SetValue(customBlockData, customId);
            UISpriteField.SetValue(customBlockData, EditModeMenuPatch.ArchipelagoSprite);
            IconField.SetValue(customBlockData, EditModeMenuPatch.ArchipelagoSDFSprite);
            NameKeyField.SetValue(customBlockData, $"custom_{sentItemInfo.Item.ItemDisplayName} ({sentItemInfo.Item.ItemGame})");

            BlocksLibraryPatch.CustomBlockData[customId] = customBlockData;
            BlocksLibraryPatch.CustomBlockDataItemInfo[customId] = sentItemInfo.Item;
            BlocksLibraryPatch.CustomBlockDataPlayerName[customId] = sentItemInfo.Recipient.Name;
            BlocksLibraryPatch.CustomBlockDataOriginal[customId] = null;

            NotificationsPatch.AllowQueueNotification = true;
            AbstractSingleton<Notifications>.Instance.QueueNotification(NotificationType.ChallengeCompleted, customBlockData, null);
            NotificationsPatch.AllowQueueNotification = false;
        }

        while (Plugin.PendingItems.TryDequeue(out ItemInfo itemInfo))
        {
            bool isFiller = itemInfo.ItemId >= 1000;

            bool unlockSilently = Plugin.UnlockedItemIds.Contains(itemInfo.ItemId);


            if (!isFiller && !unlockSilently)
            {
                Plugin.UnlockedItemIds.Add(itemInfo.ItemId);
            }

            if (isFiller)
            {
                CurrencyManager currencyManager = AbstractSingleton<CurrencyManager>.Instance;
                if (itemInfo.ItemId == 1000)
                {
                    // Gain 500 gold
                    currencyManager.AddMoney(500);
                }
                else if (itemInfo.ItemId == 1001)
                {
                    // Gain 1000 gold
                    currencyManager.AddMoney(1000);
                }
                else if (itemInfo.ItemId == 1002)
                {
                    // Gain 500 mana
                    currencyManager.AddMana(null, null, 500);
                }
                else if (itemInfo.ItemId == 1003)
                {
                    // Gain full starpower
                    var StarShowerManager = AbstractSingleton<StarShowerManager>.Instance;
                    StarShowerManager.FillGauge();
                }
                else
                {
                    Plugin.BepinLogger.LogWarning($"Received unknown filler item with id {itemInfo.ItemId}");
                }
            }
            else if (BaseElementBlocks.TryGetValue(itemInfo.ItemId, out BlockData blockData))
            {
                ApplyUnlock(blockData, itemInfo);
            }
            else
            {
                //It must be a machine block.
                var lib = Resources.FindObjectsOfTypeAll<BlocksLibrary>()[0];
                var machineBlockData = lib.GetBlockDataById((int)itemInfo.ItemId);
                ApplyUnlock(machineBlockData, itemInfo, unlockSilently);
            }

        }

    }

}





