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
using System.IO;
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

    }


}


[HarmonyPatch(typeof(ProgressManager))]
internal class UnlockBlockPatch
{

    static BlocksManager blocksManager;

    //	public void OnElementBlockSpawned(BlockData blockData, GridCell onCell)
    [HarmonyPatch("OnElementBlockSpawned")]
    [HarmonyPostfix]
    static void PostOnElementBlockSpawned(ProgressManager __instance, BlockData blockData, Fakutori.Grid.GridCell onCell)
    {
        var titleScreen = UnityEngine.Object.FindObjectOfType<TitleScreen>();
        if (titleScreen == null || !titleScreen.gameObject.activeSelf)
        {
            //Plugin.BepinLogger.LogInfo("1");
            Plugin.BepinLogger.LogInfo("OnElementBlockSpawned called: " + blockData.blockName);
            if (!ArchipelagoClient.ServerData.CheckedLocations.Contains(blockData.blockId))
            {
                //Plugin.BepinLogger.LogInfo("1.5");
                //Plugin.BepinLogger.LogInfo(Plugin.ArchipelagoClient);
                //Plugin.BepinLogger.LogInfo(Plugin.ArchipelagoClient.session);
                //Plugin.BepinLogger.LogInfo(Plugin.ArchipelagoClient.session.Locations);
                // Use the session instance to complete the location check for this block id.
                Plugin.ArchipelagoClient.session.Locations.CompleteLocationChecks(blockData.blockId);
            }
            //Plugin.BepinLogger.LogInfo("2");
            if (!Plugin.UnlockedElements.Contains(blockData.blockId))
            {
                //Plugin.BepinLogger.LogInfo("3");
                List<Block> blocksAt = AbstractSingleton<GridManager>.Instance.GetBlocksAt<Block>(onCell.position);
                //Plugin.BepinLogger.LogInfo("4");
                foreach (Block block in blocksAt)
                {
                    blocksManager.RemoveBlock(block);
                }
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
        if (blocksManager == null)
        {
            Plugin.BepinLogger.LogError("Where id blocksManager?");
        }

    }
    static void OnGameTick()
    {


        if (!Plugin.didBlockDump)
        {
            var ElementBlocksField = AccessTools.Field(typeof(BlocksLibrary), "ElementBlocks");
            var lib = Resources.FindObjectsOfTypeAll<BlocksLibrary>()[0];
            var ElementBlocks = (BlockData[])ElementBlocksField.GetValue(lib);

            if (ElementBlocks == null)
            {
                Plugin.BepinLogger.LogInfo("ElementBlocks is null!");
                return;
            }

            foreach (var block in ElementBlocks)
            {
                Plugin.BepinLogger.LogInfo($"Block: {block.blockName}, ID: {block.blockId}, color: {block.color}");
            }

            Plugin.didBlockDump = true;

            //var field = AccessTools.Field(typeof(ProgressManager), "blocksProgress");
            //var blocksProgress = (SortedDictionary<int, BlockProgress>) field.GetValue(progressManager);


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





