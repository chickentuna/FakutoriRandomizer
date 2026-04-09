using FakutoriArchipelago.Archipelago;
using HarmonyLib;
using I2.Loc;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace FakutoriArchipelago;

[HarmonyPatch(typeof(Notification))]
class NotificationPatch
{
    [HarmonyPatch("OpenCompendium")]
    [HarmonyPrefix]
    static bool PreOpenCompendium(Notification __instance)
    {
        var blockDataField = AccessTools.Field(typeof(Notification), "blockData");
        var blockData = (BlockData)blockDataField.GetValue(__instance);

        if (BlocksLibraryPatch.CustomBlockDataOriginal.TryGetValue(blockData.blockId, out var actualBlockData))
        {
            AbstractSingleton<UIManager>.Instance.OpenCompendiumOnItem(actualBlockData);
            return false;
        }
        return true;
    }

    [HarmonyPatch("Init")]
    [HarmonyPrefix]
    static bool PreInit(Notification __instance, SerializedNotification data)
    {
        var BindShortcutMethod = AccessTools.Method(typeof(Notification), "BindShortcuts");
        var BindShortcuts = (Action)Delegate.CreateDelegate(typeof(Action), __instance, BindShortcutMethod);

        BindShortcuts();

        var blockDataField = AccessTools.Field(typeof(Notification), "blockData");
        var sourceLocationField = AccessTools.Field(typeof(Notification), "sourceLocation");
        var hasSourceLocationField = AccessTools.Field(typeof(Notification), "hasSourceLocation");
        var BlockImageField = AccessTools.Field(typeof(Notification), "BlockImage");
        var BadgeImageField = AccessTools.Field(typeof(Notification), "BadgeImage");
        var BlockSymbolField = AccessTools.Field(typeof(Notification), "BlockSymbol");
        var TypeTextField = AccessTools.Field(typeof(Notification), "TypeText");
        var BlockNameTextField = AccessTools.Field(typeof(Notification), "BlockNameText");
        var LocateButtonField = AccessTools.Field(typeof(Notification), "LocateButton");
        var ButtonsContainerTransformField = AccessTools.Field(typeof(Notification), "ButtonsContainerTransform");
        var RootTransformField = AccessTools.Field(typeof(Notification), "RootTransform");
        var CompendiumButtonField = AccessTools.Field(typeof(Notification), "CompendiumButton");
        var DismissButtonField = AccessTools.Field(typeof(Notification), "DismissButton");
        var controlsField = AccessTools.Field(typeof(Notification), "controls");
        var LocateSourceMethod = AccessTools.Method(typeof(Notification), "LocateSource");
        var OpenCompendiumMethod = AccessTools.Method(typeof(Notification), "OpenCompendium");
        var DismissMethod = AccessTools.Method(typeof(Notification), "Dismiss");

        var BlockImage = (Image)BlockImageField.GetValue(__instance);
        var BadgeImage = (Image)BadgeImageField.GetValue(__instance);
        var BlockSymbol = (Image)BlockSymbolField.GetValue(__instance);
        var CompendiumButton = (UIShortcutButton)CompendiumButtonField.GetValue(__instance);
        var DismissButton = (UIShortcutButton)DismissButtonField.GetValue(__instance);
        var TypeText = (TMP_Text)TypeTextField.GetValue(__instance);
        var BlockNameText = (TMP_Text)BlockNameTextField.GetValue(__instance);
        var LocateButton = (UIShortcutButton)LocateButtonField.GetValue(__instance);
        var ButtonsContainerTransform = (RectTransform)ButtonsContainerTransformField.GetValue(__instance);
        var RootTransform = (RectTransform)RootTransformField.GetValue(__instance);
        var controls = (GameControls)controlsField.GetValue(__instance);
        var LocateSource = (UnityAction)Delegate.CreateDelegate(typeof(UnityAction), __instance, LocateSourceMethod);
        var OpenCompendium = (UnityAction)Delegate.CreateDelegate(typeof(UnityAction), __instance, OpenCompendiumMethod);
        var Dismiss = (UnityAction)Delegate.CreateDelegate(typeof(UnityAction), __instance, DismissMethod);

        var blockData = AbstractSingleton<BlocksManager>.Instance.blocksLibrary.GetBlockDataById(data.BlockId);
        blockDataField.SetValue(__instance, blockData);
        sourceLocationField.SetValue(__instance, data.SourceLocation);
        var hasSourceLocation = data.HasSourceLocation;
        hasSourceLocationField.SetValue(__instance, hasSourceLocation);
        BlockImage.sprite = blockData.uiSprite;
        BadgeImage.sprite = (data.Type == NotificationType.NewBlock) ? AbstractSingleton<Notifications>.Instance.newBlockBadge : AbstractSingleton<Notifications>.Instance.challengeCompletedBadge;
        BlockSymbol.sprite = blockData.icon;

        bool activeShowInCompendium = true;

        if (blockData.blockId < 0)
        {
            ItemInfo itemInfo = BlocksLibraryPatch.CustomBlockDataItemInfo[blockData.blockId];
            string playerName = BlocksLibraryPatch.CustomBlockDataPlayerName.TryGetValue(blockData.blockId, out var name) ? name : null;

            if (playerName == null)
            {
                playerName = itemInfo.Player.Name == ArchipelagoClient.ServerData.SlotName ? "self" : itemInfo.Player.Name;
            }

            if (data.Type == NotificationType.ChallengeCompleted)
            {
                TypeText.text = LocalizationManager.GetTranslation("notifications/sentItem") + playerName;
                activeShowInCompendium = false;
            }
            else
            {
                TypeText.text = LocalizationManager.GetTranslation("notifications/newItem") + playerName;
            }
        }
        else
        {
            TypeText.text = LocalizationManager.GetTranslation("notifications/" + data.Type.ToString().Decapitalize());
        }

        BlockNameText.text = blockData.blockName;
        UIShortcutButton locateButton = LocateButton;
        InputAction[] inputActions = [controls.menus.notificationLocate];
        UnityAction val = LocateSource;
        locateButton.Init(labelKey: null, label: LocalizationManager.GetTranslation("notifications/locate"), inputActions: (InputAction[])(object)inputActions, action: val, disabled: !hasSourceLocation);
        CompendiumButton.Init((InputAction[])(object)new InputAction[1] { controls.menus.notificationCompendium }, new UnityAction(OpenCompendium), null, disabled: !activeShowInCompendium, LocalizationManager.GetTranslation("notifications/compendium"));
        DismissButton.Init((InputAction[])(object)new InputAction[1] { controls.menus.notificationDismiss }, new UnityAction(Dismiss), null, disabled: false, LocalizationManager.GetTranslation("notifications/dismiss"));
        LayoutRebuilder.ForceRebuildLayoutImmediate(ButtonsContainerTransform);
        LayoutRebuilder.ForceRebuildLayoutImmediate(RootTransform);

        return false;
    }
}
