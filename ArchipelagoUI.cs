using BepInEx;
using BepInEx.Logging;
using FakutoriArchipelago.Archipelago;
using FakutoriArchipelago.Utils;
using HarmonyLib;
using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace FakutoriArchipelago;

public class ArchipelagoUI : MonoBehaviour
{

    private GameObject root;
    private Text statusText;
    private InputField hostField;
    private InputField playerField;
    private InputField passwordField;
    private Button connectButton;

    public void Init()
    {
        CreateUI();
    }

    public void OnTick()
    {
        var titleScreen = UnityEngine.Object.FindObjectOfType<TitleScreen>();
        if (titleScreen != null)
        {
            UpdateUI(titleScreen.gameObject.activeSelf);
        }
        else
        {
            UpdateUI(false);
        }
    }

    Text CreateText(string name, string content, Vector2 pos, Font font)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform);

        var text = go.AddComponent<Text>();
        text.font = font;
        text.text = content;
        text.color = Color.black;

        var rect = text.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = pos;
        rect.sizeDelta = new Vector2(300, 20);

        return text;
    }

    InputField CreateInput(string name, Vector2 pos, Font font)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform, false);

        var image = go.AddComponent<Image>();
        image.color = Color.white;

        var input = go.AddComponent<InputField>();

        // Create Text for input
        var textGO = new GameObject("Text");
        textGO.transform.SetParent(go.transform, false);
        var text = textGO.AddComponent<Text>();
        text.font = font;
        text.text = "";
        text.color = Color.black;
        text.alignment = TextAnchor.MiddleLeft;
        //textGO.AddComponent<CanvasRenderer>();

        input.textComponent = text;

        // RectTransform for input field
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = pos;
        rect.sizeDelta = new Vector2(250, 20);

        return input;
    }





    void CreateUI()
    {
        Plugin.BepinLogger.LogInfo("UI creation");

        // Canvas
        var canvasGO = new GameObject("ArchipelagoCanvas");
        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        root = canvasGO;

        Font font = Resources.GetBuiltinResource<Font>("Arial.ttf");

        int x = 816;
        int y = -16;
        int sepY = -30;
        int curY = y;

        // Mod label
        CreateText("ModInfo", Plugin.ModDisplayInfo, new Vector2(x, curY), font);
        curY+= sepY;

        statusText = CreateText("StatusText", "", new Vector2(x, curY), font);
        curY += sepY;

        hostField = CreateInput("HostField", new Vector2(x + 134, curY), font);
        CreateText("HostLabel", "Host:", new Vector2(x, curY), font);
        curY += sepY;

        playerField = CreateInput("PlayerField", new Vector2(x + 134, curY), font);
        CreateText("PlayerLabel", "Player Name:", new Vector2(x, curY), font);
        curY += sepY;

        passwordField = CreateInput("PasswordField", new Vector2(x + 134, curY), font);
        CreateText("PasswordLabel", "Password:", new Vector2(x, curY), font);
        curY += sepY;

        connectButton = CreateButton("ConnectButton", "Connect", new Vector2(x, curY), font);
        connectButton.onClick.AddListener(OnConnectClicked);


        hostField.text = ArchipelagoClient.ServerData.Uri;
        playerField.text = ArchipelagoClient.ServerData.SlotName;
        passwordField.text = ArchipelagoClient.ServerData.Password;

        
    }

    Button CreateButton(string name, string label, Vector2 pos, Font font)
    {
        var go = new GameObject(name);
        go.transform.SetParent(root.transform);

        var image = go.AddComponent<Image>();
        image.color = Color.white;

        var button = go.AddComponent<Button>();

        var text = CreateText("ButtonText", label, Vector2.zero, font);
        text.alignment = TextAnchor.MiddleCenter;
        text.transform.SetParent(go.transform);
        text.color = Color.black;

        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0, 1);
        rect.anchorMax = new Vector2(0, 1);
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = pos;
        rect.sizeDelta = new Vector2(100, 20);

        return button;
    }

    void UpdateUI(bool show)
    {
        if (show) {
            root.SetActive(true);
        }
        else
        {
            root.SetActive(false);
            return;
        }
        if (ArchipelagoClient.Authenticated)
        {
            statusText.text = Plugin.APDisplayInfo + " Status: Connected";

            hostField.gameObject.SetActive(false);
            playerField.gameObject.SetActive(false);
            passwordField.gameObject.SetActive(false);
            connectButton.gameObject.SetActive(false);
        }
        else
        {
            statusText.text = Plugin.APDisplayInfo + " Status: Disconnected";

            hostField.gameObject.SetActive(true);
            playerField.gameObject.SetActive(true);
            passwordField.gameObject.SetActive(true);
            connectButton.gameObject.SetActive(true);

        }
    }

    void OnConnectClicked()
    {
        ArchipelagoClient.ServerData.Uri = hostField.text;
        ArchipelagoClient.ServerData.SlotName = playerField.text;
        ArchipelagoClient.ServerData.Password = passwordField.text;

        if (!string.IsNullOrWhiteSpace(playerField.text))
        {
            Plugin.ArchipelagoClient.Connect();
        }
    }
}