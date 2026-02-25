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
public class BlockCollection
{
    public BlockDump[] blocks;
}


[System.Serializable]
public class BlockDump
{
    public String name;
    public String blockName;
    public int id;
    public String category;
    public String color;
    public Boolean unlockedByDefault;
    public String[] properties;
}

public class BlockDumper
    { 
    public static void DumpBlocks(BlockData[] blocks, string filePath)
    {

        var dumps = blocks
            .Where(block => block != null)
            .Select(block=> new BlockDump
        {
            blockName = block.blockName,
            name = block.name,
            id = block.blockId,
            category = block.category?.name,
            color = block.color?.colorName,
            unlockedByDefault = block.unlockedByDefault,
            properties = block.properties?
                .Where(p => p?.property != null)
                .Select(p => p.property.ToString())
                .ToArray()

            }).ToArray();


        var collection = new BlockCollection { blocks = dumps };
        Plugin.BepinLogger.LogInfo(collection.blocks.Length + " blocks dumped.");
        var json = JsonConvert.SerializeObject(collection, Formatting.Indented);
        File.WriteAllText(filePath, json);
    }
}
/*
[Error: Unity Log] NullReferenceException: Object reference not set to an instance of an object
Stack trace:
FakutoriArchipelago.BlockDumper+<>c.<DumpBlocks>b__0_0(BlockData block) (at<8286589535824951b19a85a6e94323ad>:0)
System.Linq.Enumerable+SelectArrayIterator`2[TSource, TResult].ToArray() (at<7619ace64c6741ef82e9e455289b1377>:0)
System.Linq.Enumerable.ToArray[TSource] (System.Collections.Generic.IEnumerable`1[T] source) (at<7619ace64c6741ef82e9e455289b1377>:0)
FakutoriArchipelago.BlockDumper.DumpBlocks(BlockData[] blocks, System.String filePath) (at<8286589535824951b19a85a6e94323ad>:0)
FakutoriArchipelago.UnlockBlockPatch.OnGameTick() (at<8286589535824951b19a85a6e94323ad>:0)
UnityEngine.Events.InvokableCall.Invoke() (at<561971c91c08471b9e995b582c6b3074>:0)
UnityEngine.Events.UnityEvent.Invoke() (at<561971c91c08471b9e995b582c6b3074>:0)
TimeManager.Update() (at<23fbe48a4b564663a8c714a8d03b72cc>:0)
*/