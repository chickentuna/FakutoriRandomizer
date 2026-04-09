using BepInEx;
using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FakutoriArchipelago;

[System.Serializable]
public class RecipeCollection
{
    public RecipeDump[] recipes;
}

[System.Serializable]
public class IngredientDump
{
    public int blockId;
    public string blockName;
    public int quantity;
    public string ingredientType;
    public string property;
}

[System.Serializable]
public class RecipeDump
{
    public string type;
    public int productId;
    public string productName;
    public int? byproductId;
    public string byproductName;
    public bool displayOnly;

    public int moneyCost;
    public int manaCost;
    public float timeCost;
    public float probability;

    public IngredientDump[] ingredients;
}

public class RecipeDumper
{
    public static void Do()
    {
        string path = Path.Combine(Paths.PluginPath, "recipes_dump.json");

        var RecipesField = AccessTools.Field(typeof(BlocksLibrary), "Recipes");
        var lib = Resources.FindObjectsOfTypeAll<BlocksLibrary>()[0];
        var Recipes = (Recipe[])RecipesField.GetValue(lib);
        DumpRecipes(Recipes, path);
    }

    public static void DumpRecipes(Recipe[] recipes, string filePath)
    {
        var dumps = recipes
            .Where(recipe => recipe?.product != null)
            .OrderBy(recipe => recipe.product.blockId)
            .Select(recipe => new RecipeDump
            {
                type = recipe.type.ToString(),
                productId = recipe.product.blockId,
                productName = recipe.product.blockName,
                byproductId = recipe.byproduct?.blockId,
                byproductName = recipe.byproduct?.blockName,
                displayOnly = recipe.displayOnly,

                moneyCost = recipe.cost.moneyCost,
                manaCost = recipe.cost.manaCost,
                timeCost = recipe.cost.timeCost,
                probability = recipe.cost.probability,

                ingredients = recipe.ingredients?.Select(ing => new IngredientDump
                {
                    blockId = ing.block?.blockId ?? -1,
                    blockName = ing.block?.blockName ?? "NO_BLOCK",
                    quantity = ing.quantity,
                    ingredientType = ing.ingredientType.ToString(),
                    property = ing.property.ToString()
                }).ToArray()
            }).ToArray();

        var collection = new RecipeCollection { recipes = dumps };
        Plugin.BepinLogger.LogInfo($"{collection.recipes.Length} recipes dumped to {filePath}");
        File.WriteAllText(filePath, JsonConvert.SerializeObject(collection, Formatting.Indented));
    }
}
