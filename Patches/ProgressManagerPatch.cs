using Archipelago.MultiClient.Net.Helpers;
using FakutoriArchipelago.Archipelago;
using Fakutori.Grid;
using HarmonyLib;
using I2.Loc;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace FakutoriArchipelago;

[HarmonyPatch(typeof(ProgressManager))]
internal class ProgressManagerPatch
{
    static bool AllowOnElementBlockSpawned = false;
    public static Dictionary<long, Sprite> BlockSprites;
    public static Dictionary<long, int> BlockUnlockCosts = new Dictionary<long, int>();
    public static Dictionary<long, int> OriginalBlockUnlockCosts = new Dictionary<long, int>();
    public static Dictionary<long, ItemInfo> ShopLocations = new();
    public static Dictionary<BlockData, long> ShopLocationIdFromBlockData = new Dictionary<BlockData, long>();

    public static Dictionary<long, BlockData> ElementBlocksById = new();

    [HarmonyPatch("BlockChallengeCompleted")]
    [HarmonyPostfix]
    static void PostBlockChallengeCompleted(ProgressManager __instance, BlockData blockData, GridCell onCell = null)
    {
        //TODO: do a check for this
    }

    [HarmonyPatch("SetItemSeen")]
    [HarmonyPrefix]
    static bool PreSetItemSeen(ProgressManager __instance, BlockData blockData)
    {
        if (blockData.blockId < 0)
        {
            return false;
        }
        return true;
    }



    [HarmonyPatch("OnBlockDespawned")]
    [HarmonyPrefix]
    static bool PreOnBlockDespawned(ProgressManager __instance, BlockData blockData)
    {
        if (AbstractSingleton<GameplayManager>.Instance.state != GameplayManager.GameplayState.Cutscene)
        {
            var blocksProgressField = AccessTools.Field(typeof(ProgressManager), "blocksProgress");
            var blocksProgress = (SortedDictionary<int, BlockProgress>)blocksProgressField.GetValue(__instance);
            if (blocksProgress.ContainsKey(blockData.blockId))
            {
                return true;
            }
            return false;
        }
        return true;
    }


    [HarmonyPatch("OnElementBlockSpawned")]
    [HarmonyPrefix]
    static bool PreOnElementBlockSpawned(ProgressManager __instance, BlockData blockData, Fakutori.Grid.GridCell onCell)
    {
        if (!ArchipelagoClient.Authenticated)
        {
            return true;
        }
        if (AbstractSingleton<GameplayManager>.Instance.state != GameplayManager.GameplayState.Watch)
        {
            return true;
        }

        if (AllowOnElementBlockSpawned)
        {
            return true;
        }

        bool uncheckedLocation = !Plugin.BlockIdsThatAreNotLocations.Contains(blockData.blockId)
            && !ArchipelagoClient.session.Locations.AllLocationsChecked.Contains(blockData.blockId);


        if (uncheckedLocation)
        {
            Plugin.DoCheck(blockData.blockId);
        }

        return true;
    }



    [HarmonyPatch("Start")]
    [HarmonyPostfix]
    static void PostStart(ProgressManager __instance)
    {
        Plugin.BepinLogger.LogInfo("ProgressManager Start called");
        AbstractSingleton<TimeManager>.Instance.OnTick.AddListener(OnGameTick);

        var go = new GameObject("ArchipelagoUI");
        var ui = go.AddComponent<ArchipelagoUI>();

        //TODO: rebuild UI using the sam lib as unity explorer

        ui.Init();
        AbstractSingleton<TimeManager>.Instance.OnTick.AddListener(ui.OnTick);

        var ElementBlocksField = AccessTools.Field(typeof(BlocksLibrary), "ElementBlocks");
        var MachineBlocksField = AccessTools.Field(typeof(BlocksLibrary), "MachineBlocks");
        var lib = Resources.FindObjectsOfTypeAll<BlocksLibrary>()[0];
        var ElementBlocks = (BlockData[])ElementBlocksField.GetValue(lib);
        var MachineBlocks = (BlockData[])MachineBlocksField.GetValue(lib);

        foreach (var blockData in MachineBlocks)
        {
            if (blockData.blockId == 0)
            {
                var BlockIdField = AccessTools.Field(typeof(BlockData), "BlockId");
                BlockIdField.SetValue(blockData, Constants.MachinePlaceholderBlockId); // Setting this block id to 100 because zero is not allowed by archipelago
            }

            OriginalBlockUnlockCosts.Add(blockData.blockId, blockData.unlockCost);
        }

        BlockSprites = new Dictionary<long, Sprite>();
        foreach (var blockData in ElementBlocks.Concat(MachineBlocks))
        {
            BlockSprites.Add(blockData.blockId, blockData.uiSprite);
        }

        ElementBlocksById = ElementBlocks.ToDictionary(v => (long)v.blockId, v => v);
    }


    public static void PrintBlocksProgressSmoke()
    {
        var blocksProgressField = AccessTools.Field(typeof(ProgressManager), "blocksProgress");
        var progressManager = AbstractSingleton<ProgressManager>.Instance;
        var blocksProgress = (SortedDictionary<int, BlockProgress>)blocksProgressField.GetValue(progressManager);
        Plugin.BepinLogger.LogInfo($"blocksProgress for block smoke:");
        var smokeId = 20;
        Plugin.BepinLogger.LogInfo($" - is unlocked: {blocksProgress[smokeId].isUnlocked}");
    }

    public static void PrintElementCount()
    {
        var ElementBlocksField = AccessTools.Field(typeof(BlocksLibrary), "ElementBlocks");
        var lib = Resources.FindObjectsOfTypeAll<BlocksLibrary>()[0];
        var ElementBlocks = (BlockData[])ElementBlocksField.GetValue(lib);
        Plugin.BepinLogger.LogInfo($"Element blocks count in lib: {ElementBlocks.Length}");
    }

    public static void ApplyUnlock(BlockData blockData, ItemInfo itemInfo, bool unlockSilently = false)
    {
        ProgressManager progressManager = AbstractSingleton<ProgressManager>.Instance;

        if (blockData.category.name != "Machine")
        {
            string actualName = LocalizationManager.GetTranslation("blockName/" + blockData.nameKey, true, 0, true, false, null, null, true);

            // Insert a special custom block data that will be picked up by the notification when doing getblockdatabyid
            int count = BlocksLibraryPatch.CustomBlockData.Count();
            int customId = -(count + 1);
            var customBlockData = ScriptableObject.Instantiate(blockData);

            var BlockIdField = AccessTools.Field(typeof(BlockData), "BlockId");
            BlockIdField.SetValue(customBlockData, customId);

            // By default, only uiSprite, icon, and blockName are used. We have a hook on blockName so we put our desired name here.
            string unlockText = $"Recipe(s) for {actualName}";
            var NameKeyField = AccessTools.Field(typeof(BlockData), "NameKey");
            NameKeyField.SetValue(customBlockData, $"custom_{unlockText}");
            BlocksLibraryPatch.CustomBlockData[customId] = customBlockData;
            // Now I use itemInfo in the notification init code, so it needs to know where to find it. I'll put it in this static dictionary.
            BlocksLibraryPatch.CustomBlockDataItemInfo[customId] = itemInfo;
            // Add this one is for opening the compendium
            BlocksLibraryPatch.CustomBlockDataOriginal[customId] = blockData;

            // Mark it as discovered in the compendium if the player has already generated that block once (according to archipelago).
            if (ArchipelagoClient.session.Locations.AllLocationsChecked.Contains(blockData.blockId))
            {
                AllowOnElementBlockSpawned = true;
                progressManager.OnElementBlockSpawned(blockData, null);
                AllowOnElementBlockSpawned = false;

            }

            if (!unlockSilently)
            {
                NotificationsPatch.AllowQueueNotification = true;
                // In this line, blockData is used only to access id
                AbstractSingleton<Notifications>.Instance.QueueNotification(NotificationType.NewBlock, customBlockData, null);
                Plugin.BepinLogger.LogInfo($"Unlocked element block {blockData.blockName}");
                NotificationsPatch.AllowQueueNotification = false;
            }
        }
        else
        {
            if (!unlockSilently)
            {
                SendItemReceivedNotification(blockData, itemInfo);
            }
            AbstractSingleton<ProgressManager>.Instance.UnlockMachine(blockData);
            Plugin.BepinLogger.LogInfo($"Unlocked machine block {blockData.blockName}");
        }

        return;
    }

    static Sprite LoadSprite(string filename)
    {
        var bytes = File.ReadAllBytes($"BepInEx/plugins/FakutoriArchipelago/{filename}");
        var tex = new Texture2D(2, 2);
        tex.LoadImage(bytes);
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f));
    }

    static void InitIcons()
    {
        var moneyIcon = GameObject.Find("UI/HUD/Currencies/Money/Badge/Icon");
        var manaIcon = GameObject.Find("UI/HUD/Currencies/Mana/Badge/Icon");
        var lazyIcon = GameObject.Find("UI/HUD/Objective menu button/Content/Lazy");
        EditModeMenuPatch.MoneySprite = moneyIcon.GetComponent<Image>().sprite;
        EditModeMenuPatch.ManaSprite = manaIcon.GetComponent<Image>().sprite;
        EditModeMenuPatch.StarPowerSprite = lazyIcon.GetComponentInChildren<Image>().sprite;

        EditModeMenuPatch.ArchipelagoSprite = LoadSprite("color-icon.png");
        EditModeMenuPatch.ArchipelagoWhiteSprite = LoadSprite("white-icon.png");
        EditModeMenuPatch.ArchipelagoSDFSprite = LoadSprite("icon.png");
    }

    static List<string> GetShopLocationNames()
    {
        var names = new List<string>
        {
            "Disassembler",
            "Puller",
            "Pusher",
            "Conveyor alternate",
            "Cross conveyor",
        };

        var extraShopChecks = (long)ArchipelagoClient.ServerData.slotData["extra_shop_checks"];
        for (int i = 0; i < extraShopChecks; i++)
        {
            names.Add($"Extra shop {i + 1}");
        }

        return names;
    }

    static void InitShop()
    {
        ShopLocations.Clear();
        BlockUnlockCosts.Clear();

        var shopLocationNames = GetShopLocationNames();
        var shopLocationIds = shopLocationNames
            .Select(name => ArchipelagoClient.session.Locations.GetLocationIdFromName("Fakutori", name))
            .ToList();

        Plugin.BepinLogger.LogInfo($"Scouting shop locations with ids: {string.Join(", ", shopLocationIds)}");

        int defaultUnlockCost = 2000;

        ArchipelagoClient.session.Locations.ScoutLocationsAsync(shopLocationIds.ToArray()).ContinueWith(task =>
        {
            if (task.IsCompletedSuccessfully)
            {
                try
                {
                    var result = task.Result;
                    foreach (var pair in result)
                    {
                        long shopLocationId = pair.Key;
                        ItemInfo itemInfo = pair.Value;

                        ShopLocations[shopLocationId] = itemInfo;
                        if (OriginalBlockUnlockCosts.TryGetValue(shopLocationId, out var unlockCost))
                        {
                            BlockUnlockCosts.TryAdd(shopLocationId, unlockCost);
                        }
                        else
                        {
                            BlockUnlockCosts.TryAdd(shopLocationId, defaultUnlockCost);
                        }
                    }


                    // Apply shop reduction
                    double shopPriceReductionPercent = ((long)ArchipelagoClient.ServerData.slotData["shop_price"]) / 100.0;
                    foreach (var k in BlockUnlockCosts.Keys.ToList())
                    {
                        long locationId = k;
                        int originalCost = BlockUnlockCosts[k];
                        int reducedCost = (int)(originalCost * shopPriceReductionPercent);
                        BlockUnlockCosts[locationId] = reducedCost;
                    }
                }
                catch (Exception ex)
                {
                    Plugin.BepinLogger.LogError(ex.Message);
                    throw;
                }
            }
            else
            {
                Plugin.BepinLogger.LogError($"Failed to scout locations");
            }
        });
    }

    static void InitUnlocks()
    {
        bool startWithDisassembler = ((long)ArchipelagoClient.ServerData.slotData["start_with_disassembler"]) == 1;
        bool startWithBaseMachines = ((long)ArchipelagoClient.ServerData.slotData["start_with_base_machines"]) == 1;

        // Base Elements
        Plugin.UnlockedItemIds.Add(Constants.BaseElement1BlockId);
        Plugin.UnlockedItemIds.Add(Constants.BaseElement2BlockId);
        Plugin.UnlockedItemIds.Add(Constants.BaseElement3BlockId);
        Plugin.UnlockedItemIds.Add(Constants.BaseElement4BlockId);

        if (startWithBaseMachines)
        {
            // Generators
            Plugin.UnlockedItemIds.Add(Constants.MachinePlaceholderBlockId);
            Plugin.UnlockedItemIds.Add(1);
            Plugin.UnlockedItemIds.Add(2);
            Plugin.UnlockedItemIds.Add(3);

            // Wall
            Plugin.UnlockedItemIds.Add(6);
            // Conveyor
            Plugin.UnlockedItemIds.Add(7);
            // Combiner
            Plugin.UnlockedItemIds.Add(4);
        }

        if (startWithDisassembler)
        {
            Plugin.UnlockedItemIds.Add(5);
        }
    }



    static void OnGameTick()
    {
        if (!Plugin.didInitUnlocks && ArchipelagoClient.Authenticated)
        {
            InitUnlocks();
            Plugin.didInitUnlocks = true;
        }
        if (!Plugin.didInitIcons && ArchipelagoClient.Authenticated)
        {
            InitIcons();
            Plugin.didInitIcons = true;
        }
        if (!Plugin.didInitShop && ArchipelagoClient.Authenticated)
        {
            InitShop();
            Plugin.didInitShop = true;
        }
        if (ArchipelagoClient.Authenticated)
        {
            Plugin.CheckGoal();
        }

        if (!Plugin.didBlockDump)
        {
            BlockDumper.Do();
            Plugin.didBlockDump = true;
        }

        if (!Plugin.didRecipeDump)
        {
            RecipeDumper.Do();
            Plugin.didRecipeDump = true;
        }

        if (AbstractSingleton<GameplayManager>.Instance.state != GameplayManager.GameplayState.Watch
            && AbstractSingleton<GameplayManager>.Instance.state != GameplayManager.GameplayState.Edit)
        {
            return;
        }

        while (Plugin.PendingSentItems.TryDequeue(out SentItemInfo sentItemInfo))
            ProcessPendingSentItem(sentItemInfo);

        while (Plugin.PendingItems.TryDequeue(out ItemInfo itemInfo))
            ProcessPendingReceivedItem(itemInfo);
    }

    // Show a notification on screen when this player's item reaches another player's world.
    static void ProcessPendingSentItem(SentItemInfo sentItemInfo)
    {
        int count = BlocksLibraryPatch.CustomBlockData.Count();
        int customId = -(count + 1);
        var customBlockData = ScriptableObject.CreateInstance<BlockData>();

        var BlockIdField = AccessTools.Field(typeof(BlockData), "BlockId");
        var UISpriteField = AccessTools.Field(typeof(BlockData), "UISprite");
        var IconField = AccessTools.Field(typeof(BlockData), "Icon");
        var NameKeyField = AccessTools.Field(typeof(BlockData), "NameKey");

        BlockIdField.SetValue(customBlockData, customId);
        UISpriteField.SetValue(customBlockData, EditModeMenuPatch.ArchipelagoSprite);
        IconField.SetValue(customBlockData, EditModeMenuPatch.ArchipelagoSDFSprite);
        NameKeyField.SetValue(customBlockData, $"custom_{sentItemInfo.Item.ItemDisplayName} ({sentItemInfo.Item.ItemGame})");

        BlocksLibraryPatch.CustomBlockData[customId] = customBlockData;
        BlocksLibraryPatch.CustomBlockDataItemInfo[customId] = sentItemInfo.Item;
        BlocksLibraryPatch.CustomBlockDataPlayerName[customId] = sentItemInfo.Recipient.Name;
        BlocksLibraryPatch.CustomBlockDataOriginal[customId] = null;

        NotificationsPatch.AllowQueueNotification = true;
        AbstractSingleton<Notifications>.Instance.QueueNotification(NotificationType.ChallengeCompleted, customBlockData, null);
        NotificationsPatch.AllowQueueNotification = false;
    }

    // Apply a single item received from the Archipelago server to the current game session.
    static void ProcessPendingReceivedItem(ItemInfo itemInfo)
    {
        bool isFiller = itemInfo.ItemId >= Constants.Filler500GoldItemId;

        bool unlockSilently = Plugin.UnlockedItemIds.Contains(itemInfo.ItemId);
        // If according to the save file, the item was already "discovered"
        // then we also unlock silently. But we have no way of detecting if the
        // item recipe is unlocked but undiscovered.
        var progressManager = AbstractSingleton<ProgressManager>.Instance;
        var blocksProgressField = AccessTools.Field(typeof(ProgressManager), "blocksProgress");
        var blocksProgress = (SortedDictionary<int, BlockProgress>)blocksProgressField.GetValue(progressManager);
        if (blocksProgress.TryGetValue((int)itemInfo.ItemId, out var blockProgress))
        {
            if (blockProgress.isUnlocked)
                unlockSilently = true;
        }

        if (!isFiller && !Plugin.UnlockedItemIds.Contains(itemInfo.ItemId))
            Plugin.UnlockedItemIds.Add(itemInfo.ItemId);

        if (isFiller)
        {
            ApplyFillerItem(itemInfo);
        }
        else if (ElementBlocksById.TryGetValue(itemInfo.ItemId, out BlockData blockData))
        {
            ApplyUnlock(blockData, itemInfo, unlockSilently);
        }
        else
        {
            var lib = Resources.FindObjectsOfTypeAll<BlocksLibrary>()[0];
            var machineBlockData = lib.GetBlockDataById((int)itemInfo.ItemId);
            ApplyUnlock(machineBlockData, itemInfo, unlockSilently);
        }
    }

    // Grant currency/resources for a filler item and show the received-item notification.
    static void ApplyFillerItem(ItemInfo itemInfo)
    {
        CurrencyManager currencyManager = AbstractSingleton<CurrencyManager>.Instance;
        Sprite sprite = null;
        if (itemInfo.ItemId == Constants.Filler500GoldItemId)
        {
            currencyManager.AddMoney(500);
            sprite = EditModeMenuPatch.MoneySprite;
        }
        else if (itemInfo.ItemId == Constants.Filler1000GoldItemId)
        {
            currencyManager.AddMoney(1000);
            sprite = EditModeMenuPatch.MoneySprite;
        }
        else if (itemInfo.ItemId == Constants.Filler500ManaItemId)
        {
            currencyManager.AddMana(null, null, 500);
            sprite = EditModeMenuPatch.ManaSprite;
        }
        else if (itemInfo.ItemId == Constants.FillerFullStarpowerItemId)
        {
            AbstractSingleton<StarShowerManager>.Instance.FillGauge();
            sprite = EditModeMenuPatch.StarPowerSprite;
        }
        else
        {
            Plugin.BepinLogger.LogWarning($"Received unknown filler item with id {itemInfo.ItemId}");
        }

        BlockData blockData = ScriptableObject.CreateInstance<BlockData>();
        var BlockIdField = AccessTools.Field(typeof(BlockData), "BlockId");
        var UISpriteField = AccessTools.Field(typeof(BlockData), "UISprite");
        var IconField = AccessTools.Field(typeof(BlockData), "Icon");
        BlockIdField.SetValue(blockData, (int)itemInfo.ItemId);
        UISpriteField.SetValue(blockData, sprite);
        IconField.SetValue(blockData, EditModeMenuPatch.ArchipelagoSDFSprite);
        SendItemReceivedNotification(blockData, itemInfo, itemInfo.ItemDisplayName);
    }

    static void SendItemReceivedNotification(BlockData blockData, ItemInfo itemInfo, string unlockText = null)
    {
        int count = BlocksLibraryPatch.CustomBlockData.Count();
        int customId = -(count + 1);
        var customBlockData = ScriptableObject.Instantiate(blockData);

        var BlockIdField = AccessTools.Field(typeof(BlockData), "BlockId");
        BlockIdField.SetValue(customBlockData, customId);

        if (unlockText != null)
        {
            var NameKeyField = AccessTools.Field(typeof(BlockData), "NameKey");
            NameKeyField.SetValue(customBlockData, $"custom_{unlockText}");
        }

        BlocksLibraryPatch.CustomBlockData[customId] = customBlockData;
        BlocksLibraryPatch.CustomBlockDataItemInfo[customId] = itemInfo;
        BlocksLibraryPatch.CustomBlockDataOriginal[customId] = blockData;

        NotificationsPatch.AllowQueueNotification = true;
        AbstractSingleton<Notifications>.Instance.QueueNotification(NotificationType.NewBlock, customBlockData, null);
        NotificationsPatch.AllowQueueNotification = false;
    }

}
