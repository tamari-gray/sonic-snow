using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The "ENTER NAME" screen, built to the design artifact.
///
/// Constructed in code for the same reason as the leaderboard: the design is specified
/// in exact pixel values, and expressing them once here beats hand-nesting RectTransforms
/// and hoping every 6px border stayed 6px.
///
/// Functionally this is a straight reskin. It exposes the real <see cref="TMP_InputField"/>
/// and <see cref="Button"/> it builds, and the editor setup assigns those to GameLogic's
/// existing fields — so username sanitising, the play-button gating in
/// UsernameInputValidator, and OnPlayButtonPressed all work unchanged.
///
/// Also drives an idle auto-start: the Play button relies on a raycast hitting a
/// World Space canvas, which isn't reliably reachable via the Beam Pro's touchscreen in
/// an HMD. Rather than fix touch targeting, this sidesteps it — stop typing for a beat
/// with a real name entered, and a short "3, 2, 1, GET READY" overlay counts down and
/// then fires the same path OnPlayButtonPressed already does. Manually tapping Play (if
/// it does land) still works exactly as before; the two paths are safe to race, since
/// OnPlayButtonPressed itself is gated on CurrentState and no-ops the second call.
/// </summary>
public class RetroUsernamePanel : MonoBehaviour
{
    public static RetroUsernamePanel Instance;

    [Header("Fonts")]
    [SerializeField] private TMP_FontAsset displayFont;

    [Header("Idle auto-start")]
    [Tooltip("Seconds of no typing before the auto-start countdown begins.")]
    [SerializeField] private float idleSecondsBeforeAutoStart = 1f;

    [Tooltip("Minimum characters typed before idle time is allowed to trigger the countdown.")]
    [SerializeField] private int minCharactersToAutoStart = 2;

    [Tooltip("Seconds held on each countdown beat (3, 2, 1, GET READY).")]
    [SerializeField] private float autoStartStepDuration = 1f;

    /// <summary>The input the player types into. Wire this to GameLogic.usernameInputField.</summary>
    public TMP_InputField InputField { get; private set; }

    /// <summary>The play button. Wire this to GameLogic.playButton.</summary>
    public Button PlayButton { get; private set; }

    private const float MaxPanelWidth = 900f;
    private const float SidePadding = 24f;

    // Doubled from the artifact's px values, same as the leaderboard — see RetroUI.Scale.
    private const float TitleSize  = 68f;   // artifact: 34
    private const float InputSize  = 44f;   // artifact: 22
    private const float ButtonSize = 40f;   // artifact: 20
    private const float CountdownSize = 120f;

    private const int MaxNameLength = 12;   // matches the artifact's maxlength

    private RectTransform root;
    private RectTransform column;

    private RectTransform countdownOverlay;
    private TMP_Text countdownShadow;
    private TMP_Text countdownFace;

    private float lastEditTime;
    private bool autoStartTriggered;
    private Coroutine autoStartRoutine;

    private void Awake()
    {
        Instance = this;
        Build();
        Hide();
    }

    private void Start()
    {
        // Hand the widgets to GameLogic so username sanitising and OnPlayButtonPressed
        // keep working against the same two references they always used. Start, not
        // Awake — GameLogic's own Awake has to have set Instance first.
        if (GameLogic.Instance != null && InputField != null && PlayButton != null)
        {
            GameLogic.Instance.BindUsernameUI(InputField, PlayButton.gameObject);
            PlayButton.onClick.AddListener(GameLogic.Instance.OnPlayButtonPressed);
        }
        else
        {
            Debug.LogWarning("[RetroUsernamePanel] No GameLogic to bind to — the Play button will do nothing.");
        }

        // Same gating the old UsernameInputValidator did: no name, no Play button.
        if (InputField != null)
        {
            InputField.onValueChanged.AddListener(UpdatePlayButtonVisibility);
            InputField.onValueChanged.AddListener(OnTextEdited);
            UpdatePlayButtonVisibility(InputField.text);
        }
    }

    private void UpdatePlayButtonVisibility(string current)
    {
        if (PlayButton != null) PlayButton.gameObject.SetActive(!string.IsNullOrEmpty(current));
    }

    /// <summary>
    /// Every keystroke pushes the idle clock back and cancels a countdown already in
    /// progress — the player is clearly still editing, so let them.
    /// </summary>
    private void OnTextEdited(string current)
    {
        lastEditTime = Time.unscaledTime;

        if (autoStartRoutine != null)
        {
            StopCoroutine(autoStartRoutine);
            autoStartRoutine = null;
            HideCountdownOverlay();
        }
    }

    private void Update()
    {
        if (root == null || !root.gameObject.activeInHierarchy) return;
        if (autoStartTriggered || autoStartRoutine != null) return;
        if (InputField == null || InputField.text.Length < minCharactersToAutoStart) return;
        if (Time.unscaledTime - lastEditTime < idleSecondsBeforeAutoStart) return;

        autoStartRoutine = StartCoroutine(RunAutoStartCountdown());
    }

    private IEnumerator RunAutoStartCountdown()
    {
        countdownOverlay.gameObject.SetActive(true);

        string[] beats = { "3", "2", "1", "GET READY" };
        Color[] colours = { RetroUI.AccentCyan, RetroUI.Gold, RetroUI.AccentLocal, RetroUI.Go };

        for (int i = 0; i < beats.Length; i++)
        {
            SetCountdownLabel(beats[i], colours[i]);
            yield return new WaitForSecondsRealtime(autoStartStepDuration);
        }

        HideCountdownOverlay();
        autoStartRoutine = null;
        autoStartTriggered = true;

        if (GameLogic.Instance != null) GameLogic.Instance.OnPlayButtonPressed();
    }

    private void SetCountdownLabel(string label, Color colour)
    {
        countdownShadow.text = label;
        countdownFace.text = label;
        countdownFace.color = colour;
    }

    private void HideCountdownOverlay()
    {
        if (countdownOverlay != null) countdownOverlay.gameObject.SetActive(false);
    }

    public void Show()
    {
        if (root == null) return;

        root.gameObject.SetActive(true);
        FitToScreen();

        if (InputField != null) InputField.ActivateInputField();

        // Fresh run: a name typed for a previous race shouldn't instantly re-trigger.
        lastEditTime = Time.unscaledTime;
        autoStartTriggered = false;
        if (autoStartRoutine != null)
        {
            StopCoroutine(autoStartRoutine);
            autoStartRoutine = null;
        }
        HideCountdownOverlay();
    }

    public void Hide()
    {
        if (root != null) root.gameObject.SetActive(false);
    }

    /// <summary>Keeps the panel inside the screen, like the leaderboard's own fit.</summary>
    private void FitToScreen()
    {
        if (column == null || root == null) return;

        float available = root.rect.width - SidePadding * 2f;
        column.sizeDelta = new Vector2(Mathf.Min(available, MaxPanelWidth), 0f);
    }

    private void Build()
    {
        root = RetroUI.Full("RetroUsernamePanel", (RectTransform)transform, RetroUI.BgDeep);

        RectTransform vignette = RetroUI.Full("Vignette", root,
            new Color(RetroUI.BgVignette.r, RetroUI.BgVignette.g, RetroUI.BgVignette.b, 0.55f));
        vignette.anchorMin = new Vector2(0f, 0.45f);

        GameObject columnObject = new GameObject("Column", typeof(RectTransform));
        column = (RectTransform)columnObject.transform;
        column.SetParent(root, false);

        // Centred both ways, per the artifact's align-items:center.
        column.anchorMin = new Vector2(0.5f, 0.5f);
        column.anchorMax = new Vector2(0.5f, 0.5f);
        column.pivot = new Vector2(0.5f, 0.5f);
        column.sizeDelta = new Vector2(MaxPanelWidth, 0f);
        column.anchoredPosition = Vector2.zero;

        VerticalLayoutGroup columnLayout = columnObject.AddComponent<VerticalLayoutGroup>();
        columnLayout.childAlignment = TextAnchor.UpperCenter;
        columnLayout.childForceExpandWidth = true;
        columnLayout.childForceExpandHeight = false;
        columnObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        RetroUI.Ribbon(column, "ENTER NAME", displayFont,
                       TitleSize, plateWidth: 720f, plateHeight: 132f, blockHeight: 172f);

        GameObject panel = RetroUI.BorderedPanel(column,
            new RectOffset(34, 34, 36, 34), spacing: 26f);

        BuildInput((RectTransform)panel.transform);
        BuildPlayButton((RectTransform)panel.transform);

        // Built last so it's the top sibling and draws over the whole panel, name field
        // included — the point is to cover input while the auto-start countdown runs.
        BuildCountdownOverlay(root);
    }

    /// <summary>
    /// The "3, 2, 1, GET READY" auto-start overlay. Deliberately plainer than the
    /// race-start CountdownTimer (no light strip, no pop animation) — this is a brief
    /// "your name is locked in" notice, not the dramatic go-moment.
    /// </summary>
    private void BuildCountdownOverlay(RectTransform parent)
    {
        countdownOverlay = RetroUI.Full("AutoStartCountdown", parent,
            new Color(RetroUI.BgDeep.r, RetroUI.BgDeep.g, RetroUI.BgDeep.b, 0.85f));

        GameObject slot = new GameObject("Number", typeof(RectTransform));
        RectTransform slotRect = (RectTransform)slot.transform;
        slotRect.SetParent(countdownOverlay, false);
        RetroUI.Stretch(slotRect, Vector2.zero);

        // Hard drop shadow, no blur — the same convention every other retro screen uses.
        countdownShadow = RetroUI.Label(slotRect, "3", CountdownSize,
            new Color(0f, 0f, 0f, 0.55f), TextAlignmentOptions.Center, displayFont);
        RetroUI.Stretch((RectTransform)countdownShadow.transform, new Vector2(6f, -6f));
        countdownShadow.characterSpacing = 4f;

        countdownFace = RetroUI.Label(slotRect, "3", CountdownSize,
            RetroUI.AccentCyan, TextAlignmentOptions.Center, displayFont);
        RetroUI.Stretch((RectTransform)countdownFace.transform, Vector2.zero);
        countdownFace.characterSpacing = 4f;

        countdownOverlay.gameObject.SetActive(false);
    }

    private void BuildInput(RectTransform parent)
    {
        // Border first, field inset inside it — the artifact's 4px #22233f rule.
        GameObject border = RetroUI.Block("InputBorder", parent, RetroUI.Rule);
        border.AddComponent<LayoutElement>().preferredHeight = 116f;

        GameObject field = RetroUI.Block("InputField", (RectTransform)border.transform, RetroUI.Field);
        RetroUI.Inset(field, 8f);
        field.GetComponent<Image>().raycastTarget = true;  // it has to be clickable

        // The text and placeholder both need to sit inside the field's padding.
        RectTransform textArea = new GameObject("TextArea", typeof(RectTransform)).GetComponent<RectTransform>();
        textArea.SetParent(field.transform, false);
        RetroUI.Stretch(textArea, Vector2.zero);
        textArea.offsetMin = new Vector2(20f, 0f);
        textArea.offsetMax = new Vector2(-20f, 0f);

        TMP_Text placeholder = RetroUI.Label(textArea, "AAA", InputSize, RetroUI.Placeholder,
                                             TextAlignmentOptions.Center, displayFont);
        RetroUI.Stretch((RectTransform)placeholder.transform, Vector2.zero);
        placeholder.characterSpacing = 6f;

        TMP_Text text = RetroUI.Label(textArea, "", InputSize, RetroUI.TextPrimary,
                                      TextAlignmentOptions.Center, displayFont);
        RetroUI.Stretch((RectTransform)text.transform, Vector2.zero);
        text.characterSpacing = 6f;

        InputField = field.AddComponent<TMP_InputField>();
        InputField.textViewport = textArea;
        InputField.textComponent = text;
        InputField.placeholder = placeholder;
        InputField.characterLimit = MaxNameLength;
        InputField.lineType = TMP_InputField.LineType.SingleLine;

        // The artifact uppercases as you type; matching that keeps the letter-spacing
        // even and the leaderboard's rider names consistent.
        InputField.characterValidation = TMP_InputField.CharacterValidation.None;
        InputField.onValueChanged.AddListener(value =>
        {
            string upper = value.ToUpperInvariant();
            if (upper != value) InputField.text = upper;
        });
    }

    private void BuildPlayButton(RectTransform parent)
    {
        GameObject shadow = RetroUI.Block("ButtonShadow", parent, RetroUI.RibbonShadow);
        LayoutElement shadowLayout = shadow.AddComponent<LayoutElement>();
        shadowLayout.preferredHeight = 128f;

        // The artifact's 0 8px 0 hard shadow: the plate sits on a block offset downward,
        // so the button reads as a physical arcade key.
        GameObject face = RetroUI.Block("ButtonFace", (RectTransform)shadow.transform, RetroUI.RibbonBorder);
        RectTransform faceRect = (RectTransform)face.transform;
        RetroUI.Stretch(faceRect, Vector2.zero);
        faceRect.offsetMax = new Vector2(0f, 0f);
        faceRect.offsetMin = new Vector2(0f, 16f);   // reveals the shadow beneath

        RetroUI.Inset(RetroUI.Block("ButtonInner", (RectTransform)face.transform, RetroUI.RibbonRed), 12f);

        TMP_Text label = RetroUI.Label(face.transform, "PLAY GAME", ButtonSize, Color.white,
                                       TextAlignmentOptions.Center, displayFont);
        RetroUI.Stretch((RectTransform)label.transform, Vector2.zero);
        label.characterSpacing = 4f;

        PlayButton = face.AddComponent<Button>();
        face.GetComponent<Image>().raycastTarget = true;

        ColorBlock colours = PlayButton.colors;
        colours.normalColor = Color.white;
        colours.highlightedColor = Color.white;
        colours.pressedColor = new Color(0.82f, 0.82f, 0.82f, 1f);
        colours.selectedColor = Color.white;
        PlayButton.colors = colours;
    }
}
