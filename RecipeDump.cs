using BepInEx;
using BepInEx.Logging;
using FakutoriArchipelago.Archipelago;
using FakutoriArchipelago.Utils;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Newtonsoft.Json;

namespace FakutoriArchipelago;

[System.Serializable]
public class RecipeCollection
{
    public RecipeDump[] recipes;
}

[System.Serializable]
public class IngredientDump
{
    public string blockName;
    public int quantity;
    public string ingredientType;
    public String property;
}

[System.Serializable]
public class RecipeDump
{
    public string type;
    public string product;
    public string byproduct;
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
        string path = Path.Combine(Paths.PluginPath, "recipes_dump.txt");

        var RecipesField = AccessTools.Field(typeof(BlocksLibrary), "Recipes");
        var lib = Resources.FindObjectsOfTypeAll<BlocksLibrary>()[0];
        var Recipes = (Recipe[])RecipesField.GetValue(lib);
        RecipeDumper.DumpRecipes(Recipes, path);
    }

    public static void DumpRecipes(Recipe[] recipes, string filePath)
    {

        var dumps = recipes.Select(recipe => new RecipeDump
        {
            type = recipe.type.ToString(),
            product = recipe.product?.name,
            byproduct = recipe.byproduct?.name,
            displayOnly = recipe.displayOnly,

            moneyCost = recipe.cost.moneyCost,
            manaCost = recipe.cost.manaCost,
            timeCost = recipe.cost.timeCost,
            probability = recipe.cost.probability,

            ingredients = recipe.ingredients?.Select(ing => new IngredientDump
            {
                blockName = ing.block?.blockName ?? "NO_BLOCK",
                quantity = ing.quantity,
                ingredientType = ing.ingredientType.ToString(),
                property = ing.property.ToString()
            }).ToArray()
        }).ToArray();


        var collection = new RecipeCollection { recipes = dumps };
        Plugin.BepinLogger.LogInfo(collection.recipes.Length + " recipes dumped.");
        var json = JsonConvert.SerializeObject(collection, Formatting.Indented);
        File.WriteAllText(filePath, json);

        //string json = JsonUtility.ToJson(collection, true);
        //File.WriteAllText(filePath, json);
    }
}
