using Fakutori.VFX;
using FakutoriArchipelago.Archipelago;
using HarmonyLib;
using UnityEngine;

namespace FakutoriArchipelago;

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
