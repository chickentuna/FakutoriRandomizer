using FakutoriArchipelago.Archipelago;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Events;

namespace FakutoriArchipelago;

[HarmonyPatch(typeof(GameplayManager))]
class GameplayManagerPatch
{
    static void Reset()
    {
        BlocksLibraryPatch.CustomBlockData.Clear();
        BlocksLibraryPatch.CustomBlockDataItemInfo.Clear();
        BlocksLibraryPatch.CustomBlockDataPlayerName.Clear();
        Plugin.UnlockedItemIds.Clear();
        Plugin.PendingItems.Clear();
        Plugin.PendingSilentItems.Clear();
        Plugin.BepinLogger.LogInfo($"Resetting archipelago progress...");

        Plugin.didInitUnlocks = false;
        Plugin.didInitShop = false;

        // apIndex (from the save's .apindex side file) = how many history items were already applied
        // and saved. Items before it rebuild unlock state silently; items at/after it are new since the
        // save and get their notifications.
        int apIndex = ArchipelagoClient.ServerData.Index;

        Plugin.BepinLogger.LogInfo($"Pushing history (apIndex {apIndex}):");
        for (int i = 0; i < Plugin.ReceivedItemHistory.Count; i++)
        {
            var item = Plugin.ReceivedItemHistory[i];
            bool alreadyApplied = i < apIndex;
            bool isFiller = item.ItemId >= Constants.Filler500GoldItemId;

            if (alreadyApplied)
            {
                // Filler currency is already in the save — don't re-grant. Unlocks are re-applied
                // silently to rebuild UnlockedItemIds / recipes without spamming notifications.
                if (isFiller)
                    Plugin.BepinLogger.LogInfo($"   filler {item.ItemName} (index {i} already applied)");
                else
                    Plugin.PendingSilentItems.Enqueue(item);
            }
            else
            {
                Plugin.BepinLogger.LogInfo($"   item: {item.ItemName}");
                Plugin.PendingItems.Enqueue(item);
            }
        }
        ArchipelagoClient.ServerData.Index = Plugin.ReceivedItemHistory.Count;
    }

    [HarmonyPatch("SaveGame")]
    [HarmonyPostfix]
    static void PostSaveGame(GameplayManager __instance, string fileName, string saveName, Texture2D previewTexture, UnityAction callback, bool toVirtualFile)
    {
        if (!ArchipelagoClient.Authenticated) return;
        Plugin.BepinLogger.LogInfo($"Saving game... writing AP index {ArchipelagoClient.ServerData.Index} to {fileName}.apindex");
        ArchipelagoSaveHelper.WriteLastAppliedIndex(fileName, ArchipelagoClient.ServerData.Index);
    }

    [HarmonyPatch("LoadGame")]
    [HarmonyPostfix]
    static void PostLoadGame(GameplayManager __instance, string fileName, UnityAction callback, bool fromVirtualFile)
    {
        if (!ArchipelagoClient.Authenticated) return;
        Plugin.BepinLogger.LogInfo($"Loading game... reading AP index from {fileName}.apindex");
        int apIndex = ArchipelagoSaveHelper.ReadLastAppliedIndex(fileName);
        ArchipelagoClient.ServerData.Index = apIndex;
        Reset();
    }

    [HarmonyPatch("NewGame")]
    [HarmonyPostfix]
    static void PostNewGame(GameplayManager __instance)
    {
        if (!ArchipelagoClient.Authenticated) return;
        Plugin.BepinLogger.LogInfo($"Starting new game... resetting AP index to 0");
        ArchipelagoClient.ServerData.Index = 0;
        Reset();
    }
}
