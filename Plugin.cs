using BepInEx;
using BepInEx.Logging;
using FakutoriArchipelago.Archipelago;
using FakutoriArchipelago.Utils;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

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

    public static bool didRecipeDump = false;
    public static bool didBlockDump = false;


    private void Awake()
    {
        // Plugin startup logic
        BepinLogger = Logger;
        ArchipelagoClient = new ArchipelagoClient();
        ArchipelagoConsole.Awake();

        ArchipelagoConsole.LogMessage($"{ModDisplayInfo} loaded!");
        var harmony = new Harmony("com.chickentuna.archipelago");
        harmony.PatchAll(typeof(Plugin));
        harmony.PatchAll(typeof(UnlockBlockPatch));

    }


}


[HarmonyPatch(typeof(ProgressManager))]
internal class UnlockBlockPatch
        
{

    //	public void OnElementBlockSpawned(BlockData blockData, GridCell onCell)
    [HarmonyPatch("OnElementBlockSpawned")]
    [HarmonyPostfix]
    static void PostOnElementBlockSpawned(ProgressManager __instance, BlockData blockData, Fakutori.Grid.GridCell onCell)
    {
        Plugin.BepinLogger.LogInfo("OnElementBlockSpawned called: " + blockData.blockName);
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

    }
    static void OnGameTick()
    {
        //Plugin.BepinLogger.LogInfo("Game tick! " + AbstractSingleton<ProgressManager>.Instance.blocksDiscovered);
        var ui = GameObject.FindObjectOfType<ArchipelagoUI>();
        if (ui != null)
            AbstractSingleton<TimeManager>.Instance.OnTick.AddListener(ui.OnTick);
        else
            Plugin.BepinLogger.LogInfo("UI not found on tick");

        var blocksManager = AbstractSingleton<BlocksManager>.Instance;
        var progressManager = AbstractSingleton<ProgressManager>.Instance;

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





