using Archipelago.MultiClient.Net.Helpers;
using BepInEx;
using BepInEx.Logging;
using FakutoriArchipelago.Archipelago;
using FakutoriArchipelago.Utils;
using HarmonyLib;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

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

    public static bool didInitShop = true;
    public static bool didInitUnlocks = true;

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

        BlockIdsThatAreNotLocations.Add(Constants.BaseElement1BlockId);
        BlockIdsThatAreNotLocations.Add(Constants.BaseElement2BlockId);
        BlockIdsThatAreNotLocations.Add(Constants.BaseElement3BlockId);
        BlockIdsThatAreNotLocations.Add(Constants.BaseElement4BlockId);
    }

    //TODO: test this victory condition
    public static void CheckGoal()
    {
        long victoryCondition = (long)ArchipelagoClient.ServerData.slotData["victory_condition"];
        if (victoryCondition == 0)
        {
            // Unlock all element blocks
            var progressManager = AbstractSingleton<ProgressManager>.Instance;
            var blocksProgressField = AccessTools.Field(typeof(ProgressManager), "blocksProgress");
            var blocksProgress = (SortedDictionary<int, BlockProgress>)blocksProgressField.GetValue(progressManager);
            var lib = Resources.FindObjectsOfTypeAll<BlocksLibrary>()[0];

            foreach (var kv in blocksProgress)
            {
                int blockId = kv.Key;
                BlockProgress blockProgress = kv.Value;
                BlockData blockData = lib.GetBlockDataById(blockId);
                if (blockData == null)
                    continue;
                if (blockData.category == null)
                    continue;
                if (blockData.category.isMachine)
                    continue;
                if (!blockData.showInCompendium)
                    continue;

                // An element block to unlock
                if (!blockProgress.isUnlocked)
                {
                    return;
                }

            }
            ArchipelagoClient.session.SetGoalAchieved();
        }
    }
}
