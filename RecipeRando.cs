public class RecipeRando
{
    public RecipeRando()
    {
        /*
        GameplayManager gm = AbstractSingleton<GameplayManager>.Instance;
        var TitleScreenWakasField = AccessTools.Field(typeof(GameplayManager), "TitleScreenWakas");

        var TitleScreenWakas = (GameObject[])TitleScreenWakasField.GetValue(gm);
        var sr = TitleScreenWakas[0].GetComponent<SpriteRenderer>();
        if (sr == null)
        {
            sr = TitleScreenWakas[0].GetComponentInChildren<SpriteRenderer>();
        }
        if (sr != null)
        {
            Plugin.BepinLogger.LogInfo("Original title screen sprite: " + sr.sprite.name);
        }


        var RecipesField = AccessTools.Field(typeof(BlocksLibrary), "Recipes");
        var lib = Resources.FindObjectsOfTypeAll<BlocksLibrary>()[0];
        var Recipes = (Recipe[])RecipesField.GetValue(lib);
        var productField = AccessTools.Field(typeof(Recipe), "Product");

        var recipes = (Recipe[])AccessTools
            .Field(typeof(BlocksLibrary), "Recipes")
            .GetValue(lib);

        var ElementBlocksField = AccessTools.Field(typeof(BlocksLibrary), "ElementBlocks");
        var MachineBlocksField = AccessTools.Field(typeof(BlocksLibrary), "MachineBlocks");
        var ElementBlocks = (BlockData[])ElementBlocksField.GetValue(lib);
        var MachineBlocks = (BlockData[])MachineBlocksField.GetValue(lib);


        foreach (var v in ElementBlocks)
        {
            if (v.prefab == null)
            {
                continue;
            }
            var psr = v.prefab.GetComponent<SpriteRenderer>();
            if (psr == null)
            {
                psr = v.prefab.GetComponentInChildren<SpriteRenderer>();
            }
            if (psr != null)
            {
                psr.sprite = sr.sprite;
            }
            //var uiSpriteField = AccessTools.Field(typeof(BlockData), "UISprite");
            //uiSpriteField.SetValue(v, sr.sprite);
        }

        // Collect products
        var products = recipes
            .Where(r => r != null)
            .Select(r => (BlockData)productField.GetValue(r))
            .ToList();

        // Shuffle
        var rng = new System.Random();
        products = products.OrderBy(_ => rng.Next()).ToList();

        // Reassign
        for (int i = 0; i < recipes.Length; i++)
        {
            if (recipes[i] != null)
                productField.SetValue(recipes[i], products[i]);
        }
        */
    }
}
