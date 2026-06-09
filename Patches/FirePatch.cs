using System.Reflection;
using FakutoriArchipelago.Archipelago;
using HarmonyLib;

namespace FakutoriArchipelago;

// Fire evolves (e.g. into Yellow fire) once it has consumed 5 blocks, via Fire.BlockAction. That path
// reads the Fire's own EvolutionBlock field and spawns it directly, bypassing the recipe/transform
// gates in BlocksLibraryPatch/ElementBlockPatch. Rather than let it spawn and then delete the result
// (jarring), we hide the evolution target while it's locked: Fire's `if (EvolutionBlock != null)`
// branch is skipped, so it just keeps burning until the player unlocks the evolved block.
// FireHot/FireHotter inherit Fire.BlockAction (no override), so this covers the whole fire chain.
[HarmonyPatch(typeof(Fire), "BlockAction")]
internal static class FirePatch
{
    static readonly FieldInfo F_EvolutionBlock = AccessTools.Field(typeof(Fire), "EvolutionBlock");

    [HarmonyPrefix]
    static void Prefix(Fire __instance, ref BlockData __state)
    {
        __state = null;
        if (!ArchipelagoClient.Authenticated) return;

        var evo = F_EvolutionBlock.GetValue(__instance) as BlockData;
        if (evo != null && (Plugin.UnlockedItemIds == null || !Plugin.UnlockedItemIds.Contains(evo.blockId)))
        {
            __state = evo;
            F_EvolutionBlock.SetValue(__instance, null);
        }
    }

    // Finalizer (not postfix) so the field is restored even if BlockAction throws.
    [HarmonyFinalizer]
    static void Finalizer(Fire __instance, BlockData __state)
    {
        if (__state != null)
            F_EvolutionBlock.SetValue(__instance, __state);
    }
}
