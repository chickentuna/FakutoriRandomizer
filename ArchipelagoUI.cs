using FakutoriArchipelago.Archipelago;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FakutoriArchipelago;

// The Archipelago connection UI, built into the "Archipelago" tab of the game's Settings menu
// (see OptionsPatch). Input field / button / label are cloned from the game's own ModalWindow
// widgets so they inherit the options canvas's CanvasScaler + GraphicRaycaster — meaning hit areas
// line up with the cursor (the old hand-built canvas had a click offset). Attached to the tab's
// content container; OnEnable refreshes the connection status whenever the tab is opened.
public class ArchipelagoUI : MonoBehaviour
{
    TMP_InputField hostField;
    TMP_InputField playerField;
    TMP_InputField passwordField;
    Button connectButton;
    TMP_Text connectButtonLabel;
    TMP_Text statusText;
    bool built;

    // Connect() runs on a thread pool and flips Authenticated off the main thread, so we poll it from
    // Update. connectingUntil drives a transient "Connecting…" message that self-heals back to
    // editable if the attempt fails (the client never signals failure to us). shownState avoids
    // rebuilding the labels every frame.
    float connectingUntil;
    string shownState;

    // The OptionsMenu's GameControls — disabled while a field is focused so its tab-switch (A/E) and
    // revert (R) shortcuts don't fire while typing. Invoked by reflection to avoid type coupling.
    System.Action enableControls;
    System.Action disableControls;

    public void Build(TMP_InputField inputTemplate, Button buttonTemplate, TMP_Text labelTemplate, object menuControls)
    {
        if (built) return;
        built = true;

        if (menuControls != null)
        {
            var enable = menuControls.GetType().GetMethod("Enable", System.Type.EmptyTypes);
            var disable = menuControls.GetType().GetMethod("Disable", System.Type.EmptyTypes);
            if (enable != null) enableControls = () => enable.Invoke(menuControls, null);
            if (disable != null) disableControls = () => disable.Invoke(menuControls, null);
        }

        // The cloned container already carries a VerticalLayoutGroup; ensure sane settings.
        var vlg = GetComponent<VerticalLayoutGroup>() ?? gameObject.AddComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(24, 24, 24, 24);
        vlg.spacing = 12f;
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        statusText = CloneLabel(labelTemplate, "", 24f, 30f);

        hostField = CloneField(inputTemplate, "Host (e.g. archipelago.gg:38281)");
        playerField = CloneField(inputTemplate, "Slot name");
        passwordField = CloneField(inputTemplate, "Password (optional)");
        passwordField.contentType = TMP_InputField.ContentType.Password;
        passwordField.ForceLabelUpdate();

        connectButton = CloneButton(buttonTemplate, "Connect");
        connectButtonLabel = connectButton.GetComponentInChildren<TMP_Text>(true);
        connectButton.onClick.AddListener(OnConnectClicked);

        hostField.text = ArchipelagoClient.ServerData.Uri ?? "";
        playerField.text = ArchipelagoClient.ServerData.SlotName ?? "";
        passwordField.text = ArchipelagoClient.ServerData.Password ?? "";

        RefreshStatus();
    }

    // Fires whenever the Archipelago tab becomes the active content panel.
    void OnEnable()
    {
        shownState = null;  // force a recompute on (re)open
        if (built) RefreshStatus();
    }

    void Update()
    {
        if (built) RefreshStatus();
    }

    void RefreshStatus()
    {
        if (statusText == null) return;

        bool connected = ArchipelagoClient.Authenticated;
        bool connecting = !connected && Time.realtimeSinceStartup < connectingUntil;

        string state = connected ? "connected" : connecting ? "connecting" : "disconnected";
        if (state == shownState) return;
        shownState = state;
        if (connected)
        {
            connectingUntil = 0f;
            Plugin.SaveLastConnection();  // remember a successful connection for next launch
        }

        statusText.text = Plugin.APDisplayInfo + (connected ? " — Connected"
                                                : connecting ? " — Connecting…"
                                                : " — Disconnected");

        bool editable = !connected && !connecting;
        hostField.interactable = editable;
        playerField.interactable = editable;
        passwordField.interactable = editable;

        // The button toggles to "Disconnect" while connected; it's only disabled mid-attempt.
        if (connectButtonLabel != null) connectButtonLabel.text = connected ? "Disconnect" : "Connect";
        connectButton.interactable = !connecting;
    }

    TMP_Text CloneLabel(TMP_Text template, string text, float fontSize, float height)
    {
        var clone = Instantiate(template.gameObject, transform, false);
        clone.name = "AP_Label";
        clone.SetActive(true);
        var t = clone.GetComponent<TMP_Text>();
        t.text = text;
        t.fontSize = fontSize;
        Normalize(clone, height);
        return t;
    }

    TMP_InputField CloneField(TMP_InputField template, string placeholder)
    {
        var clone = Instantiate(template.gameObject, transform, false);
        clone.name = "AP_Field";
        clone.SetActive(true);
        var input = clone.GetComponent<TMP_InputField>();
        // Drop any listeners the modal had wired on its template field.
        input.onValueChanged = new TMP_InputField.OnChangeEvent();
        input.onEndEdit = new TMP_InputField.SubmitEvent();
        input.onSubmit = new TMP_InputField.SubmitEvent();
        input.contentType = TMP_InputField.ContentType.Standard;
        input.text = "";
        if (input.placeholder is TMP_Text ph) ph.text = placeholder;

        // Suppress the menu's A/E/R keyboard shortcuts while this field has focus.
        input.onSelect.AddListener(_ => disableControls?.Invoke());
        input.onDeselect.AddListener(_ => enableControls?.Invoke());

        Normalize(clone, 48f);
        return input;
    }

    Button CloneButton(Button template, string label)
    {
        var clone = Instantiate(template.gameObject, transform, false);
        clone.name = "AP_ConnectButton";
        clone.SetActive(true);
        var btn = clone.GetComponent<Button>();
        btn.onClick = new Button.ButtonClickedEvent();
        var t = clone.GetComponentInChildren<TMP_Text>(true);
        if (t != null) t.text = label;
        Normalize(clone, 56f);
        return btn;
    }

    // Reset anchors so the VerticalLayoutGroup positions the clone predictably, and pin a preferred
    // height (the layout group controls width).
    static void Normalize(GameObject go, float preferredHeight)
    {
        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
        var le = go.GetComponent<LayoutElement>() ?? go.AddComponent<LayoutElement>();
        le.minHeight = le.preferredHeight = preferredHeight;
    }

    void OnConnectClicked()
    {
        if (ArchipelagoClient.Authenticated)
        {
            Plugin.ArchipelagoClient.Disconnect();
            return;
        }

        ArchipelagoClient.ServerData.Uri = hostField.text;
        ArchipelagoClient.ServerData.SlotName = playerField.text;
        ArchipelagoClient.ServerData.Password = passwordField.text;

        if (!string.IsNullOrWhiteSpace(playerField.text))
        {
            connectingUntil = Time.realtimeSinceStartup + 10f;
            Plugin.ArchipelagoClient.Connect();
        }
    }
}
