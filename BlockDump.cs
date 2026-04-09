using BepInEx;
using HarmonyLib;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace FakutoriArchipelago;

[System.Serializable]
public class BlockCollection
{
    public BlockDump[] blocks;
}

[System.Serializable]
public class BlockDump
{
    public int id;
    public string nameKey;          // localization key — stable identifier across game versions
    public string blockName;        // display name
    public string category;         // "Element" or "Machine"
    public bool showInCompendium;   // true = this block is an AP check location
    public bool unlockedByDefault;
    public string color;
    public string[] properties;
}

public class BlockDumper
{
    public static void Do()
    {
        var ElementBlocksField = AccessTools.Field(typeof(BlocksLibrary), "ElementBlocks");
        var MachineBlocksField = AccessTools.Field(typeof(BlocksLibrary), "MachineBlocks");
        var lib = Resources.FindObjectsOfTypeAll<BlocksLibrary>()[0];
        var ElementBlocks = (BlockData[])ElementBlocksField.GetValue(lib);
        var MachineBlocks = (BlockData[])MachineBlocksField.GetValue(lib);

        string path = Path.Combine(Paths.PluginPath, "blocks_dump.json");
        DumpBlocks(ElementBlocks.Concat(MachineBlocks).ToArray(), path);
    }

    public static void DumpBlocks(BlockData[] blocks, string filePath)
    {
        var NameKeyField = AccessTools.Field(typeof(BlockData), "NameKey");

        var dumps = blocks
            .Where(block => block != null)
            .OrderBy(block => block.blockId)
            .Select(block => new BlockDump
            {
                id = block.blockId,
                nameKey = (string)NameKeyField.GetValue(block),
                blockName = block.blockName,
                category = block.category?.name,
                showInCompendium = block.showInCompendium,
                unlockedByDefault = block.unlockedByDefault,
                color = block.color?.colorName,
                properties = block.properties?
                    .Where(p => p?.property != null)
                    .Select(p => p.property.ToString())
                    .ToArray()
            }).ToArray();

        var collection = new BlockCollection { blocks = dumps };
        Plugin.BepinLogger.LogInfo($"{collection.blocks.Length} blocks dumped to {filePath}");
        File.WriteAllText(filePath, JsonConvert.SerializeObject(collection, Formatting.Indented));
    }
}
