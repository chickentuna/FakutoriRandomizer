using Archipelago.MultiClient.Net.Models;
using BepInEx;
using BepInEx.Logging;
using Fakutori.Grid;
using FakutoriArchipelago.Archipelago;
using FakutoriArchipelago.Utils;
using HarmonyLib;
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

    public static ConcurrentQueue<ItemInfo> PendingItems = new();
    public static List<long> UnlockedElements = new();
    public static List<long> BlockIdsThatAreNotLocations = new();



    public void Awake()
    {
        // Plugin startup logic
        BepinLogger = Logger;
        ArchipelagoClient = new ArchipelagoClient();
        ArchipelagoConsole.Awake();

        ArchipelagoConsole.LogMessage($"{ModDisplayInfo} loaded!");
        var harmony = new Harmony("com.chickentuna.archipelago");
        harmony.PatchAll(typeof(Plugin));
        harmony.PatchAll(typeof(UnlockBlockPatch));

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


[HarmonyPatch(typeof(ProgressManager))]
internal class UnlockBlockPatch
{
    static BlocksManager blocksManager;

    [HarmonyPatch("OnElementBlockSpawned")]
    [HarmonyPostfix]
    static void PostOnElementBlockSpawned(ProgressManager __instance, BlockData blockData, Fakutori.Grid.GridCell onCell)
    {
        if (!ArchipelagoClient.Authenticated)
        {
            return;
        }
        if (AbstractSingleton<GameplayManager>.Instance.state != GameplayManager.GameplayState.Watch)
        {
            return;
        }

        bool isUnlocked = Plugin.UnlockedElements.Contains(blockData.blockId);
        bool uncheckedLocation = !ArchipelagoClient.ServerData.CheckedLocations.Contains(blockData.blockId) && !Plugin.BlockIdsThatAreNotLocations.Contains(blockData.blockId);
        
        if (uncheckedLocation)
        {
            // Use the session instance to complete the location check for this block id.
            Plugin.ArchipelagoClient.session.Locations.ScoutLocationsAsync(blockData.blockId).ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    var result = task.Result;
                    if (result.TryGetValue(blockData.blockId, out var itemInfo))
                    {
                        Plugin.BepinLogger.LogInfo($"Scouted location {blockData.blockId}, got item {itemInfo.ItemId}");

                        //AbstractSingleton<Notifications>.Instance.QueueNotification(NotificationType.NewBlock, blockData, null);
                    }
                    else
                    {
                        Plugin.BepinLogger.LogWarning($"Scouted location {blockData.blockId} but it was not found in the result!");
                    }
                }
                else
                {
                    Plugin.BepinLogger.LogError($"Failed to scout location {blockData.blockId}: {task.Exception}");
                }
            });

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




    }
    static void OnGameTick()
    {

        ItemInfo item;
        if (Plugin.PendingItems.TryDequeue(out item))
        {
            Plugin.UnlockedElements.Add(item.ItemId);

            var lib = Resources.FindObjectsOfTypeAll<BlocksLibrary>()[0];
            var blockData = lib.GetBlockDataById((int)item.ItemId);

            AbstractSingleton<Notifications>.Instance.QueueNotification(NotificationType.NewBlock, blockData, null);
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




    }

}





