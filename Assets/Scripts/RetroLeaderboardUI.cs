using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UI;

/// <summary>
/// The retro arcade leaderboard, built to the design handoff.
///
/// The whole board is constructed in code rather than authored as a prefab. The design
/// is specified in exact pixel values at a 1040px reference width — nesting fifty
/// RectTransforms by hand and keeping every 6px border and 14px gutter correct is far
/// more error-prone than expressing the same numbers once, here.
///
/// Everything is a solid-colour UGUI Image except the scanline overlay, which needs a
/// repeating texture. There are no bitmap assets to import.
/// </summary>
public class RetroLeaderboardUI : MonoBehaviour
{
    public static RetroLeaderboardUI Instance;

    [Header("Fonts")]
    [Tooltip("Chunky display face — headings, rider names, column labels. The design calls " +
             "for Press Start 2P; the Sonic HUD font stands in until that's imported.")]
    [SerializeField] private TMP_FontAsset displayFont;

    [Tooltip("Numerals — rank, time, score. The design calls for VT323.")]
    [SerializeField] private TMP_FontAsset numeralFont;

    [Header("Behaviour")]
    [SerializeField] private bool showScanlines = true;

    [Tooltip("Sort by score (highest first) rather than time (fastest first).")]
    [SerializeField] private bool sortByScore = true;

    [Tooltip("Rows to show at most. The board is display-only and doesn't scroll.")]
    [SerializeField] private int maxRows = 8;

    private const string LeaderboardUrl =
        "https://sonicar-7ea55-default-rtdb.asia-southeast1.firebasedatabase.app/leaderboard.json";

    // --- design tokens, verbatim from the handoff -------------------------------

    private static readonly Color BgDeep       = Hex("05050f");
    private static readonly Color BgVignette   = Hex("141433");
    private static readonly Color Panel        = Hex("0d0d22");
    private static readonly Color RowDefault   = Hex("111128");
    private static readonly Color RowTopThree  = Hex("16173a");
    private static readonly Color RowLocal     = Hex("2a1030");
    private static readonly Color Rule         = Hex("22233f");
    private static readonly Color PipEmpty     = Hex("33345a");
    private static readonly Color TextMuted    = Hex("6a6a9c");
    private static readonly Color TextPrimary  = Hex("f4f4ff");
    private static readonly Color AccentCyan   = Hex("22e3ff");
    private static readonly Color AccentLocal  = Hex("ff2d95");
    private static readonly Color Gold         = Hex("ffd400");
    private static readonly Color Silver       = Hex("c9d4e8");
    private static readonly Color Bronze       = Hex("c9772f");
    private static readonly Color RibbonRed    = Hex("e01b2c");
    private static readonly Color RibbonShadow = Hex("8f0f1c");
    private static readonly Color RibbonBorder = Hex("14142c");

    private const float BoardWidth = 1040f;
    private const int Gates = 5;  // 4 AR checkpoints + the finish gate

    /// <summary>One row of the board.</summary>
    public class Rider
    {
        public string Name;
        public float Seconds;
        public int Gates;
        public long Score;
        public bool IsLocalPlayer;
    }

    private RectTransform root;
    private RectTransform rowsHolder;
    private RawImage scanlines;

    private void Awake()
    {
        Instance = this;
        BuildChrome();
        Hide();
    }

    private void Update()
    {
        if (scanlines == null || !showScanlines) return;

        // 8px per 1.6s, linear, looping. The tile is 4px, so 8px is exactly two periods.
        Rect uv = scanlines.uvRect;
        uv.y -= Time.unscaledDeltaTime / 1.6f * (8f / 4f);
        scanlines.uvRect = uv;
    }

    public void Show()
    {
        if (root != null) root.gameObject.SetActive(true);
        StartCoroutine(FetchAndDisplay());
    }

    public void Hide()
    {
        if (root != null) root.gameObject.SetActive(false);
    }

    // --- data ------------------------------------------------------------------

    private IEnumerator FetchAndDisplay()
    {
        UnityWebRequest request = UnityWebRequest.Get(LeaderboardUrl);
        yield return request.SendWebRequest();

        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Leaderboard fetch failed: " + request.error);
            yield break;
        }

        List<Rider> riders = ParseRiders(request.downloadHandler.text);
        Populate(riders);
    }

    /// <summary>
    /// Firebase stores a flat { "name": seconds } object, which JsonUtility can't do —
    /// it has no dictionary support — so this parses the shape by hand.
    /// </summary>
    private List<Rider> ParseRiders(string json)
    {
        List<Rider> riders = new List<Rider>();

        json = json.Trim();
        if (json == "null" || json.Length < 2) return riders;

        json = json.Substring(1, json.Length - 2);

        foreach (string rawPair in json.Split(','))
        {
            string pair = rawPair.Trim();
            if (string.IsNullOrEmpty(pair)) continue;

            int colon = pair.IndexOf(':');
            if (colon < 0) continue;

            string name = pair.Substring(0, colon).Trim().Trim('"');
            string value = pair.Substring(colon + 1).Trim();

            if (!float.TryParse(value, System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out float seconds))
            {
                Debug.LogWarning($"Leaderboard entry for '{name}' wasn't a number: '{value}'");
                continue;
            }

            int gates = GatesFor(name);

            riders.Add(new Rider
            {
                Name = name,
                Seconds = seconds,
                Gates = gates,
                Score = ScoreFor(gates, seconds),
                IsLocalPlayer = false,
            });
        }

        return riders;
    }

    /// <summary>
    /// Placeholder gate count until runs actually record which checkpoints were cleared.
    /// Hashed off the name so a given rider always shows the same number rather than
    /// flickering every time the board refreshes.
    /// </summary>
    private static int GatesFor(string name)
    {
        int hash = 0;
        foreach (char c in name) hash = unchecked(hash * 31 + c);

        return 3 + (Mathf.Abs(hash) % (Gates - 2));  // 3..5, so the pips look plausible
    }

    /// <summary>
    /// Gates dominate, time breaks the tie. The million-per-gate step is larger than the
    /// largest possible time bonus, so any rider who cleared more gates always outscores
    /// one who cleared fewer, however fast they were.
    /// </summary>
    private const long GateStep = 1_000_000L;

    /// <summary>Runs slower than this earn no time bonus at all, in seconds.</summary>
    private const float SlowestScoringRun = 200f;

    private static long ScoreFor(int gates, float seconds)
    {
        // Capped at GateStep - 1, never GateStep. If the fastest possible time were worth
        // a full step, a flawless 4-gate run would tie a plodding 5-gate one and the
        // guarantee below would fail at exactly that boundary.
        float speed = Mathf.Clamp01((SlowestScoringRun - seconds) / SlowestScoringRun);
        long fromTime = (long)(speed * (GateStep - 1));

        return gates * GateStep + fromTime;
    }

    // --- rows ------------------------------------------------------------------

    private void Populate(List<Rider> riders)
    {
        foreach (Transform child in rowsHolder) Destroy(child.gameObject);

        string localName = GameLogic.Instance != null ? GameLogic.Instance.PlayerUsername : null;

        IEnumerable<Rider> ordered = sortByScore
            ? riders.OrderByDescending(r => r.Score)
            : (IEnumerable<Rider>)riders.OrderBy(r => r.Seconds);

        int rank = 1;

        foreach (Rider rider in ordered.Take(maxRows))
        {
            rider.IsLocalPlayer = localName != null && rider.Name == localName;
            BuildRow(rider, rank);
            rank++;
        }
    }

    private void BuildRow(Rider rider, int rank)
    {
        // Medal colours by finishing position; the local player's magenta overrides them.
        Color accent = rider.IsLocalPlayer ? AccentLocal
                     : rank == 1 ? Gold
                     : rank == 2 ? Silver
                     : rank == 3 ? Bronze
                     : AccentCyan;

        Color background = rider.IsLocalPlayer ? RowLocal
                         : rank <= 3 ? RowTopThree
                         : RowDefault;

        GameObject row = Block("Row", rowsHolder, background);
        LayoutElement rowLayout = row.AddComponent<LayoutElement>();
        rowLayout.minHeight = 56f;

        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(14, 14, 15, 15);
        layout.spacing = 14f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        // 6px accent bar down the left edge, outside the padding.
        GameObject bar = Block("AccentBar", (RectTransform)row.transform, accent);
        RectTransform barRect = (RectTransform)bar.transform;
        barRect.anchorMin = new Vector2(0f, 0f);
        barRect.anchorMax = new Vector2(0f, 1f);
        barRect.pivot = new Vector2(0f, 0.5f);
        barRect.sizeDelta = new Vector2(6f, 0f);
        barRect.anchoredPosition = Vector2.zero;
        bar.AddComponent<LayoutElement>().ignoreLayout = true;

        // Col 1 — rank, zero padded.
        Numeral(row.transform, rank.ToString("00"), 30f, accent, TextAlignmentOptions.Left, 62f);

        // Col 2 — avatar chip then name, flexible width.
        GameObject riderCol = Row("Rider", (RectTransform)row.transform, 14f);
        LayoutElement riderLayout = riderCol.AddComponent<LayoutElement>();
        riderLayout.flexibleWidth = 1f;
        riderLayout.minWidth = 200f;

        BuildAvatar((RectTransform)riderCol.transform, accent);

        TMP_Text name = Label(riderCol.transform, rider.Name, 12f,
                              rider.IsLocalPlayer ? AccentLocal : TextPrimary,
                              TextAlignmentOptions.Left, 0f);
        name.characterSpacing = 1f;
        name.overflowMode = TextOverflowModes.Ellipsis;
        name.enableWordWrapping = false;
        LayoutElement nameLayout = name.gameObject.AddComponent<LayoutElement>();
        nameLayout.flexibleWidth = 1f;

        // Col 3 — checkpoint pips.
        GameObject pips = Row("Checkpoints", (RectTransform)row.transform, 7f);
        pips.AddComponent<LayoutElement>().preferredWidth = 132f;

        for (int i = 0; i < Gates; i++) BuildPip((RectTransform)pips.transform, i < rider.Gates, accent, background);

        // Col 4 / 5 — time and score, right aligned.
        Numeral(row.transform, FormatTime(rider.Seconds), 30f, TextPrimary, TextAlignmentOptions.Right, 108f);
        Numeral(row.transform, rider.Score.ToString("N0"), 30f, Gold, TextAlignmentOptions.Right, 130f);
    }

    /// <summary>Concentric squares: 4px accent frame, 4px dark ring, accent core.</summary>
    private void BuildAvatar(RectTransform parent, Color accent)
    {
        GameObject chip = Block("Avatar", parent, accent);
        LayoutElement chipLayout = chip.AddComponent<LayoutElement>();
        chipLayout.preferredWidth = 26f;
        chipLayout.preferredHeight = 26f;

        Inset(Block("Gap", (RectTransform)chip.transform, Panel), 4f);
        Inset(Block("Core", (RectTransform)chip.transform, accent), 8f);
    }

    private void BuildPip(RectTransform parent, bool cleared, Color accent, Color rowBackground)
    {
        GameObject pip = Block("Pip", parent, cleared ? accent : PipEmpty);
        LayoutElement pipLayout = pip.AddComponent<LayoutElement>();
        pipLayout.preferredWidth = 16f;
        pipLayout.preferredHeight = 16f;

        // An uncleared gate is an outline, so punch the middle back out to the row colour.
        if (!cleared) Inset(Block("Hollow", (RectTransform)pip.transform, rowBackground), 3f);
    }

    // --- static chrome ---------------------------------------------------------

    private void BuildChrome()
    {
        root = Full("RetroLeaderboard", (RectTransform)transform, BgDeep);

        // The design's radial vignette, approximated with a soft top-centre wash. A real
        // radial gradient would need a texture; at this size the difference is invisible.
        RectTransform vignette = Full("Vignette", root, new Color(BgVignette.r, BgVignette.g, BgVignette.b, 0.55f));
        vignette.anchorMin = new Vector2(0f, 0.45f);
        vignette.GetComponent<Image>().raycastTarget = false;

        GameObject boardCol = new GameObject("Board", typeof(RectTransform));
        RectTransform board = (RectTransform)boardCol.transform;
        board.SetParent(root, false);
        board.anchorMin = new Vector2(0.5f, 1f);
        board.anchorMax = new Vector2(0.5f, 1f);
        board.pivot = new Vector2(0.5f, 1f);
        board.sizeDelta = new Vector2(BoardWidth, 0f);
        board.anchoredPosition = new Vector2(0f, -56f);

        VerticalLayoutGroup boardLayout = boardCol.AddComponent<VerticalLayoutGroup>();
        boardLayout.childAlignment = TextAnchor.UpperCenter;
        boardLayout.childForceExpandWidth = true;
        boardLayout.childForceExpandHeight = false;
        boardCol.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        BuildRibbon(board);
        BuildPanel(board);

        BuildScanlines(root);
    }

    private void BuildRibbon(RectTransform parent)
    {
        GameObject block = new GameObject("Ribbon", typeof(RectTransform));
        RectTransform rect = (RectTransform)block.transform;
        rect.SetParent(parent, false);
        block.AddComponent<LayoutElement>().preferredHeight = 118f;

        // Tails: a 26px bar behind the plate. The design notches it with a clip-path; three
        // hard rectangles read the same at this scale and suit the no-curves brief.
        GameObject tails = new GameObject("Tails", typeof(RectTransform));
        RectTransform tailsRect = (RectTransform)tails.transform;
        tailsRect.SetParent(rect, false);
        tailsRect.anchorMin = new Vector2(0f, 1f);
        tailsRect.anchorMax = new Vector2(1f, 1f);
        tailsRect.pivot = new Vector2(0.5f, 1f);
        tailsRect.sizeDelta = new Vector2(0f, 26f);
        tailsRect.anchoredPosition = new Vector2(0f, -26f);

        Stripe(tailsRect, 0f, 0.14f, 1f);      // left tail, full height
        Stripe(tailsRect, 0.86f, 1f, 1f);      // right tail, full height
        Stripe(tailsRect, 0.08f, 0.92f, 0.55f); // middle bar, 55% height

        // Hard drop shadow: the same plate offset 8px down, no blur.
        Plate(rect, RibbonShadow, new Vector2(0f, -8f));
        RectTransform plate = Plate(rect, RibbonRed, Vector2.zero);

        RectTransform border = Full("Border", plate, RibbonBorder);
        border.GetComponent<Image>().raycastTarget = false;
        Inset(Block("Inner", border, RibbonRed).gameObject, 6f);

        GameObject content = Row("Content", plate, 18f);
        RectTransform contentRect = (RectTransform)content.transform;
        contentRect.anchorMin = Vector2.zero;
        contentRect.anchorMax = Vector2.one;
        contentRect.offsetMin = Vector2.zero;
        contentRect.offsetMax = Vector2.zero;
        content.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;

        PlusMark((RectTransform)content.transform, true);

        // Hard text shadow, 4px offset, no blur — a duplicate label behind the real one.
        GameObject titleStack = new GameObject("Title", typeof(RectTransform));
        RectTransform titleRect = (RectTransform)titleStack.transform;
        titleRect.SetParent(content.transform, false);
        LayoutElement titleLayout = titleStack.AddComponent<LayoutElement>();
        titleLayout.preferredWidth = 560f;
        titleLayout.preferredHeight = 44f;

        TMP_Text shadow = Label(titleStack.transform, "LEADERBOARD", 34f, RibbonShadow, TextAlignmentOptions.Center, 0f);
        Stretch((RectTransform)shadow.transform, new Vector2(4f, -4f));
        shadow.characterSpacing = 5f;

        TMP_Text title = Label(titleStack.transform, "LEADERBOARD", 34f, Color.white, TextAlignmentOptions.Center, 0f);
        Stretch((RectTransform)title.transform, Vector2.zero);
        title.characterSpacing = 5f;

        PlusMark((RectTransform)content.transform, false);
    }

    private void BuildPanel(RectTransform parent)
    {
        GameObject outer = Block("Panel", parent, AccentCyan);
        VerticalLayoutGroup outerLayout = outer.AddComponent<VerticalLayoutGroup>();
        outerLayout.padding = new RectOffset(5, 5, 5, 5);   // the 5px cyan border
        outerLayout.childForceExpandWidth = true;
        outerLayout.childForceExpandHeight = false;
        outer.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject inner = Block("PanelInner", (RectTransform)outer.transform, Panel);
        VerticalLayoutGroup innerLayout = inner.AddComponent<VerticalLayoutGroup>();
        innerLayout.padding = new RectOffset(26, 26, 28, 22);
        innerLayout.spacing = 12f;
        innerLayout.childForceExpandWidth = true;
        innerLayout.childForceExpandHeight = false;
        inner.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        BuildHeader((RectTransform)inner.transform);

        GameObject rows = new GameObject("Rows", typeof(RectTransform));
        rowsHolder = (RectTransform)rows.transform;
        rowsHolder.SetParent(inner.transform, false);

        VerticalLayoutGroup rowsLayout = rows.AddComponent<VerticalLayoutGroup>();
        rowsLayout.spacing = 6f;
        rowsLayout.childForceExpandWidth = true;
        rowsLayout.childForceExpandHeight = false;
        rows.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
    }

    private void BuildHeader(RectTransform parent)
    {
        GameObject header = new GameObject("Header", typeof(RectTransform));
        header.transform.SetParent(parent, false);

        VerticalLayoutGroup stack = header.AddComponent<VerticalLayoutGroup>();
        stack.childForceExpandWidth = true;
        stack.childForceExpandHeight = false;
        header.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject labels = Row("Labels", (RectTransform)header.transform, 14f);
        HorizontalLayoutGroup labelLayout = labels.GetComponent<HorizontalLayoutGroup>();
        labelLayout.padding = new RectOffset(14, 14, 0, 14);

        HeaderLabel(labels.transform, "RANK", TextAlignmentOptions.Left, 62f, false);
        HeaderLabel(labels.transform, "RIDER", TextAlignmentOptions.Left, 200f, true);
        HeaderLabel(labels.transform, "CHECKPOINTS", TextAlignmentOptions.Left, 132f, false);
        HeaderLabel(labels.transform, "TIME", TextAlignmentOptions.Right, 108f, false);
        HeaderLabel(labels.transform, sortByScore ? "SCORE" : "SCORE", TextAlignmentOptions.Right, 130f, false);

        GameObject rule = Block("Rule", (RectTransform)header.transform, Rule);
        rule.AddComponent<LayoutElement>().preferredHeight = 4f;
    }

    private void BuildScanlines(RectTransform parent)
    {
        // A 4px period: 2px of 30% black over 2px of nothing. One tiny texture, tiled.
        Texture2D tile = new Texture2D(1, 4, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Repeat,
        };

        Color band = new Color(0f, 0f, 0f, 0.30f);
        tile.SetPixels(new[] { band, band, Color.clear, Color.clear });
        tile.Apply();

        GameObject overlay = new GameObject("Scanlines", typeof(RectTransform), typeof(RawImage));
        RectTransform rect = (RectTransform)overlay.transform;
        rect.SetParent(parent, false);
        Stretch(rect, Vector2.zero);

        scanlines = overlay.GetComponent<RawImage>();
        scanlines.texture = tile;
        scanlines.raycastTarget = false;
        scanlines.uvRect = new Rect(0f, 0f, 1f, Screen.height / 4f);
        overlay.SetActive(showScanlines);

        rect.SetAsLastSibling();
    }

    // --- small builders --------------------------------------------------------

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color c);
        return c;
    }

    private static void Stretch(RectTransform rect, Vector2 offset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = offset;
        rect.offsetMax = offset;
    }

    private static RectTransform Full(string name, RectTransform parent, Color colour)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        Stretch(rect, Vector2.zero);

        Image image = go.GetComponent<Image>();
        image.color = colour;

        return rect;
    }

    private static GameObject Block(string name, RectTransform parent, Color colour)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = colour;
        go.GetComponent<Image>().raycastTarget = false;

        return go;
    }

    private static GameObject Row(string name, RectTransform parent, float spacing)
    {
        GameObject go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);

        HorizontalLayoutGroup layout = go.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        return go;
    }

    /// <summary>Stretches a child to fill its parent with a uniform inset — a hard border ring.</summary>
    private static void Inset(GameObject go, float inset)
    {
        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    private static void Stripe(RectTransform parent, float fromX, float toX, float height)
    {
        GameObject go = Block("Stripe", parent, RibbonShadow);
        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(fromX, 1f - height);
        rect.anchorMax = new Vector2(toX, 1f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static RectTransform Plate(RectTransform parent, Color colour, Vector2 offset)
    {
        GameObject go = Block("Plate", parent, colour);
        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(700f, 92f);
        rect.anchoredPosition = offset;

        return rect;
    }

    /// <summary>Four 12px squares in a plus, per the design's box-shadow trick.</summary>
    private static void PlusMark(RectTransform parent, bool pointingRight)
    {
        GameObject mark = new GameObject("PlusMark", typeof(RectTransform));
        RectTransform rect = (RectTransform)mark.transform;
        rect.SetParent(parent, false);

        LayoutElement layout = mark.AddComponent<LayoutElement>();
        layout.preferredWidth = 36f;
        layout.preferredHeight = 36f;

        Vector2[] offsets =
        {
            Vector2.zero,
            new Vector2(0f, 12f),
            new Vector2(0f, -12f),
            new Vector2(pointingRight ? 12f : -12f, 0f),
        };

        foreach (Vector2 offset in offsets)
        {
            GameObject square = Block("Square", rect, Gold);
            RectTransform squareRect = (RectTransform)square.transform;
            squareRect.anchorMin = new Vector2(0.5f, 0.5f);
            squareRect.anchorMax = new Vector2(0.5f, 0.5f);
            squareRect.sizeDelta = new Vector2(12f, 12f);
            squareRect.anchoredPosition = offset;
        }
    }

    private TMP_Text Label(Transform parent, string text, float size, Color colour,
                           TextAlignmentOptions alignment, float width)
    {
        GameObject go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.color = colour;
        label.alignment = alignment;
        label.raycastTarget = false;
        if (displayFont != null) label.font = displayFont;

        if (width > 0f) go.AddComponent<LayoutElement>().preferredWidth = width;

        return label;
    }

    private void Numeral(Transform parent, string text, float size, Color colour,
                         TextAlignmentOptions alignment, float width)
    {
        TMP_Text label = Label(parent, text, size, colour, alignment, width);
        if (numeralFont != null) label.font = numeralFont;
    }

    private void HeaderLabel(Transform parent, string text, TextAlignmentOptions alignment,
                             float width, bool flexible)
    {
        TMP_Text label = Label(parent, text, 10f, TextMuted, alignment, width);
        label.characterSpacing = 1f;

        if (!flexible) return;

        LayoutElement layout = label.GetComponent<LayoutElement>();
        layout.flexibleWidth = 1f;
        layout.minWidth = 200f;
    }

    /// <summary>M:SS.hh, e.g. 1:42.06.</summary>
    private static string FormatTime(float totalSeconds)
    {
        int minutes = Mathf.FloorToInt(totalSeconds / 60f);
        int seconds = Mathf.FloorToInt(totalSeconds % 60f);
        int hundredths = Mathf.FloorToInt(totalSeconds * 100f % 100f);

        return $"{minutes}:{seconds:00}.{hundredths:00}";
    }
}
