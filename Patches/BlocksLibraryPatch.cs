using Archipelago.MultiClient.Net.Helpers;
using HarmonyLib;
using System.Collections.Generic;

namespace FakutoriArchipelago;

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
