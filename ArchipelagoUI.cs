using System.Reflection;
using FakutoriArchipelago.Archipelago;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FakutoriArchipelago;

// Connection panel built by CLONING the game's own native widgets (the TMP_InputField and Button
// from ModalWindow, the popup the game uses for OpenPlayerNameModal). Cloning native widgets and
// parenting them under the game's own canvas means we inherit its CanvasScaler / GraphicRaycaster,
// so the hit areas line up with the cursor — fixing the old hand-built canvas where clicks landed
// slightly above the visual. ModalWindow's widget fields are [SerializeField] private, so we read
// the templates by reflection; UIManager.modalWindow itself is public.
public class ArchipelagoUI : MonoBehaviour
{
    static readonly FieldInfo F_InputField = AccessTools.Field(typeof(ModalWindow), "InputField");
    static readonly FieldInfo F_PrimaryButton = AccessTools.Field(typeof(ModalWindow), "PrimaryButton");
    static readonly FieldInfo F_LabelText = AccessTools.Field(typeof(ModalWindow), "LabelText");
    static readonly FieldInfo F_TitleText = AccessTools.Field(typeof(ModalWindow), "TitleText");

    GameObject panel;
    TMP_InputField hostField;
    TMP_InputField playerField;
    TMP_InputField passwordField;
    Button connectButton;
    TMP_Text statusText;
    bool built;
    string lastDefer = "";

    public void Tick()
    {
        // If the panel was destroyed along with its canvas on a scene transition, rebuild it.
        if (built && panel == null) built = false;

        if (!built) TryBuild();
        if (!built) return;

        var titleScreen = Object.FindObjectOfType<TitleScreen>();
        bool onTitle = titleScreen != null && titleScreen.gameObject.activeSelf;
        UpdateUI(onTitle);
    }

    void Defer(string reason)
    {
        if (reason == lastDefer) return;
        lastDefer = reason;
        Plugin.BepinLogger.LogInfo($"ArchipelagoUI: waiting — {reason}");
    }

    void TryBuild()
    {
        try
        {
            var uiManager = AbstractSingleton<UIManager>.Instance;
            if (uiManager == null) { Defer("UIManager null"); return; }

            var modal = uiManager.modalWindow;
            if (modal == null) { Defer("modalWindow null"); return; }

            var inputTemplate = F_InputField.GetValue(modal) as TMP_InputField;
            var buttonTemplate = F_PrimaryButton.GetValue(modal) as Button;
            var labelTemplate = (F_LabelText.GetValue(modal) ?? F_TitleText.GetValue(modal)) as TMP_Text;
            if (inputTemplate == null) { Defer("InputField template null"); return; }
            if (buttonTemplate == null) { Defer("Button template null"); return; }
            if (labelTemplate == null) { Defer("Label template null"); return; }

            var canvas = modal.GetComponentInParent<Canvas>(true);
            if (canvas == null) { Defer("no parent Canvas on modal"); return; }

            BuildPanel(canvas, inputTemplate, buttonTemplate, labelTemplate);
            built = true;

            hostField.text = ArchipelagoClient.ServerData.Uri ?? "";
            playerField.text = ArchipelagoClient.ServerData.SlotName ?? "";
            passwordField.text = ArchipelagoClient.ServerData.Password ?? "";

            Plugin.BepinLogger.LogInfo("ArchipelagoUI: connection panel built.");
        }
        catch (System.Exception e)
        {
            Plugin.BepinLogger.LogError("ArchipelagoUI: TryBuild failed — " + e);
        }
    }

    void BuildPanel(Canvas canvas, TMP_InputField inputTemplate, Button buttonTemplate, TMP_Text labelTemplate)
    {
        panel = new GameObject("ArchipelagoPanel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
        panel.transform.SetParent(canvas.transform, false);

        var prt = (RectTransform)panel.transform;
        prt.anchorMin = prt.anchorMax = prt.pivot = new Vector2(0f, 1f);  // top-left
        prt.anchoredPosition = new Vector2(40f, -40f);
        prt.sizeDelta = new Vector2(440f, 340f);

        panel.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

        var vlg = panel.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(16, 16, 16, 16);
        vlg.spacing = 8f;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        CloneLabel(labelTemplate, Plugin.ModDisplayInfo, 28f, 34f);
        statusText = CloneLabel(labelTemplate, "", 20f, 26f);

        hostField = CloneField(inputTemplate, "Host (e.g. archipelago.gg:38281)");
        playerField = CloneField(inputTemplate, "Slot name");
        passwordField = CloneField(inputTemplate, "Password (optional)");
        passwordField.contentType = TMP_InputField.ContentType.Password;
        passwordField.ForceLabelUpdate();

        connectButton = CloneButton(buttonTemplate, "Connect");
        connectButton.onClick.AddListener(OnConnectClicked);
    }

    TMP_Text CloneLabel(TMP_Text template, string text, float fontSize, float height)
    {
        var clone = Instantiate(template.gameObject, panel.transform, false);
        clone.name = "Label";
        clone.SetActive(true);
        var t = clone.GetComponent<TMP_Text>();
        t.text = text;
        t.fontSize = fontSize;
        Normalize(clone, height);
        return t;
    }

    TMP_InputField CloneField(TMP_InputField template, string placeholder)
    {
        var clone = Instantiate(template.gameObject, panel.transform, false);
        clone.name = "Field";
        clone.SetActive(true);
        var input = clone.GetComponent<TMP_InputField>();
        // Drop any listeners the modal had serialized/wired on its template field.
        input.onValueChanged = new TMP_InputField.OnChangeEvent();
        input.onEndEdit = new TMP_InputField.SubmitEvent();
        input.onSubmit = new TMP_InputField.SubmitEvent();
        input.contentType = TMP_InputField.ContentType.Standard;
        input.text = "";
        if (input.placeholder is TMP_Text ph) ph.text = placeholder;
        Normalize(clone, 44f);
        return input;
    }

    Button CloneButton(Button template, string label)
    {
        var clone = Instantiate(template.gameObject, panel.transform, false);
        clone.name = "ConnectButton";
        clone.SetActive(true);
        var btn = clone.GetComponent<Button>();
        btn.onClick = new Button.ButtonClickedEvent();
        var t = clone.GetComponentInChildren<TMP_Text>(true);
        if (t != null) t.text = label;
        Normalize(clone, 52f);
        return btn;
    }

    // Reset the clone's root anchors so the VerticalLayoutGroup positions it predictably, and pin a
    // preferred height (layout group controls width).
    static void Normalize(GameObject go, float preferredHeight)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = preferredHeight;
    }

    void UpdateUI(bool onTitle)
    {
        panel.SetActive(onTitle);
        if (!onTitle) return;

        if (ArchipelagoClient.Authenticated)
        {
            statusText.text = Plugin.APDisplayInfo + " — Connected";
            SetInputsActive(false);
        }
        else
        {
            statusText.text = Plugin.APDisplayInfo + " — Disconnected";
            SetInputsActive(true);
        }
    }

    void SetInputsActive(bool active)
    {
        hostField.gameObject.SetActive(active);
        playerField.gameObject.SetActive(active);
        passwordField.gameObject.SetActive(active);
        connectButton.gameObject.SetActive(active);
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
