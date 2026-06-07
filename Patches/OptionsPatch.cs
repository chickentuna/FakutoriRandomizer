using System;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FakutoriArchipelago.Patches;

// Adds an "Archipelago" tab to the game's Settings menu (OptionsMenu), available from both the title
// screen and the in-game pause menu since they share one OptionsMenu instance. We postfix
// OptionsMenu.Start (which runs once, after Tabs.Awake has cached its Tab[] and after the game wires
// up its 3 tabs). We clone one of the game's own Tab buttons and content containers, extend the
// Tabs component's cached Tab[] array, append our tab to the existing tab items, and re-run
// Tabs.Init so the new tab is fully wired with the game's own selection/animation behaviour.
[HarmonyPatch]
internal static class OptionsPatch
{
    static readonly Type OptionsMenuType = AccessTools.TypeByName("Fakutori.UI.Options.OptionsMenu");

    static readonly FieldInfo F_TabsComponent = AccessTools.Field(OptionsMenuType, "TabsComponent");
    static readonly FieldInfo F_TabComponents = AccessTools.Field(typeof(Tabs), "TabComponents");
    static readonly FieldInfo F_TabItems = AccessTools.Field(typeof(Tabs), "tabItems");
    static readonly FieldInfo F_Controls = AccessTools.Field(typeof(UIWindow), "controls");

    // Templates for the connection UI widgets, borrowed from the game's ModalWindow.
    static readonly FieldInfo F_InputField = AccessTools.Field(typeof(ModalWindow), "InputField");
    static readonly FieldInfo F_PrimaryButton = AccessTools.Field(typeof(ModalWindow), "PrimaryButton");
    static readonly FieldInfo F_LabelText = AccessTools.Field(typeof(ModalWindow), "LabelText");
    static readonly FieldInfo F_TitleText = AccessTools.Field(typeof(ModalWindow), "TitleText");

    static MethodBase TargetMethod() => AccessTools.Method(OptionsMenuType, "Start");

    [HarmonyPostfix]
    static void AddArchipelagoTab(MonoBehaviour __instance)
    {
        try
        {
            var tabs = (Tabs)F_TabsComponent.GetValue(__instance);
            var tabComponents = (Tab[])F_TabComponents.GetValue(tabs);
            var tabItems = (Tabs.TabItem[])F_TabItems.GetValue(tabs);
            if (tabs == null || tabComponents == null || tabItems == null
                || tabComponents.Length == 0 || tabItems.Length == 0)
            {
                Plugin.BepinLogger.LogWarning("OptionsPatch: tabs not initialized as expected; skipping.");
                return;
            }

            // Clone an existing tab button as our 4th tab.
            var newTab = UnityEngine.Object.Instantiate(
                tabComponents[0].gameObject, tabComponents[0].transform.parent).GetComponent<Tab>();
            newTab.name = "AP_Tab";

            // Clone an existing content container and empty it.
            var srcContainer = (RectTransform)tabItems[0].target.transform;
            var newContainer = UnityEngine.Object.Instantiate(
                srcContainer.gameObject, srcContainer.parent).GetComponent<RectTransform>();
            newContainer.name = "AP_TabContainer";
            foreach (Transform child in newContainer)
                UnityEngine.Object.Destroy(child.gameObject);

            // Extend the cached Tab[] (set in Tabs.Awake) so Init can address our new tab.
            var extendedTabs = new Tab[tabComponents.Length + 1];
            Array.Copy(tabComponents, extendedTabs, tabComponents.Length);
            extendedTabs[tabComponents.Length] = newTab;
            F_TabComponents.SetValue(tabs, extendedTabs);

            // Build the connection UI into the new container, cloning ModalWindow widgets.
            var modal = AbstractSingleton<UIManager>.Instance.modalWindow;
            var inputTemplate = (TMP_InputField)F_InputField.GetValue(modal);
            var buttonTemplate = (Button)F_PrimaryButton.GetValue(modal);
            var labelTemplate = (TMP_Text)(F_LabelText.GetValue(modal) ?? F_TitleText.GetValue(modal));

            var controls = F_Controls.GetValue(__instance);

            var ui = newContainer.gameObject.AddComponent<ArchipelagoUI>();
            ui.Build(inputTemplate, buttonTemplate, labelTemplate, controls);

            // Append our tab to the game's existing items and re-init so it's fully wired.
            var newItems = new Tabs.TabItem[tabItems.Length + 1];
            Array.Copy(tabItems, newItems, tabItems.Length);
            newItems[tabItems.Length] = new Tabs.TabItem("Archipelago", newContainer.gameObject);
            tabs.Init(newItems);

            Plugin.BepinLogger.LogInfo("OptionsPatch: added Archipelago settings tab.");
        }
        catch (Exception e)
        {
            Plugin.BepinLogger.LogError("OptionsPatch.AddArchipelagoTab failed: " + e);
        }
    }
}
