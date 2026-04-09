using HarmonyLib;

namespace FakutoriArchipelago;

[HarmonyPatch(typeof(BlocksList))]
class BlocksListPatch
{
    [HarmonyPatch("CanBlockAppearInCompendium")]
    [HarmonyPrefix]
    static bool PreCanBlockAppearInCompendium(BlocksList __instance, BlockData data, ref bool __result)
    {
        if (!Plugin.UnlockedItemIds.Contains(data.blockId))
        {
            __result = false;
            return false;
        }
        return true;
    }
}
