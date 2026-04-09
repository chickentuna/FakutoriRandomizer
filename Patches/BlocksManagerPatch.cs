using Fakutori.Grid;
using FakutoriArchipelago.Archipelago;
using HarmonyLib;

namespace FakutoriArchipelago;

[HarmonyPatch(typeof(BlocksManager))]
class BlocksManagerPatch
{
    [HarmonyPatch("SpawnLegendaryBlock")]
    [HarmonyPostfix]
    static void PostSpawnLegendaryBlock(BlocksManager __instance, GridCell spawnCell, BlockData blockData)
    {
        if (blockData.blockId == Constants.QuasarBlockId)
        {
            Plugin.BepinLogger.LogInfo("A legendary block has spawned");
            bool uncheckedLocation = !ArchipelagoClient.session.Locations.AllLocationsChecked.Contains(Constants.QuasarBlockId);
            if (uncheckedLocation)
            {
                Plugin.DoCheck(Constants.QuasarBlockId);

                long victoryCondition = (long)ArchipelagoClient.ServerData.slotData["victory_condition"];
                if (victoryCondition == 2)
                {
                    ArchipelagoClient.session.SetGoalAchieved();
                }
            }
        }
    }
}
