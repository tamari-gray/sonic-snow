using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared palette and builders for the retro arcade screens.
///
/// The leaderboard and the name-entry panel are the same design language — the same
/// notched red ribbon, the same cyan-bordered panel, the same hard zero-blur shadows.
/// Keeping that construction in one place means a colour or border change lands on both
/// rather than drifting apart, which is exactly how two screens stop matching.
///
/// Everything here is a solid-colour UGUI Image. No sprites, no bitmap assets.
/// </summary>
public static class RetroUI
{
    // --- design tokens, verbatim from the handoff -------------------------------

    public static readonly Color BgDeep       = Hex("05050f");
    public static readonly Color BgVignette   = Hex("141433");
    public static readonly Color Panel        = Hex("0d0d22");
    public static readonly Color Field        = Hex("111128");
    public static readonly Color RowDefault   = Hex("111128");
    public static readonly Color RowTopThree  = Hex("16173a");
    public static readonly Color RowLocal     = Hex("2a1030");
    public static readonly Color Rule         = Hex("22233f");
    public static readonly Color Placeholder  = Hex("33345a");
    public static readonly Color TextMuted    = Hex("6a6a9c");
    public static readonly Color TextPrimary  = Hex("f4f4ff");
    public static readonly Color AccentCyan   = Hex("22e3ff");
    public static readonly Color AccentLocal  = Hex("ff2d95");
    public static readonly Color Gold         = Hex("ffd400");
    public static readonly Color Silver       = Hex("c9d4e8");
    public static readonly Color Bronze       = Hex("c9772f");
    public static readonly Color Go           = Hex("7cff5a");
    public static readonly Color LightOff     = Hex("1b1b3a");
    public static readonly Color RibbonRed    = Hex("e01b2c");
    public static readonly Color RibbonShadow = Hex("8f0f1c");
    public static readonly Color RibbonBorder = Hex("14142c");

    /// <summary>
    /// Everything is drawn at twice the handoff's px sizes. The design is authored for a
    /// desktop viewport; at 1x on a Pixel 6 the body text came out under a millimetre.
    /// </summary>
    public const float Scale = 2f;

    public static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color c);
        return c;
    }

    // --- layout primitives -----------------------------------------------------

    public static void Stretch(RectTransform rect, Vector2 offset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = offset;
        rect.offsetMax = offset;
    }

    public static RectTransform Full(string name, RectTransform parent, Color colour)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        RectTransform rect = (RectTransform)go.transform;
        rect.SetParent(parent, false);
        Stretch(rect, Vector2.zero);

        go.GetComponent<Image>().color = colour;

        return rect;
    }

    public static GameObject Block(string name, RectTransform parent, Color colour)
    {
        GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);

        Image image = go.GetComponent<Image>();
        image.color = colour;
        image.raycastTarget = false;

        return go;
    }

    public static GameObject Row(string name, RectTransform parent, float spacing)
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

    /// <summary>Fills the parent with a uniform inset — the design's hard border rings.</summary>
    public static void Inset(GameObject go, float inset)
    {
        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    public static TMP_Text Label(Transform parent, string text, float size, Color colour,
                                 TextAlignmentOptions alignment, TMP_FontAsset font)
    {
        GameObject go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        TextMeshProUGUI label = go.AddComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.color = colour;
        label.alignment = alignment;
        label.raycastTarget = false;
        if (font != null) label.font = font;

        return label;
    }

    // --- the ribbon ------------------------------------------------------------

    /// <summary>
    /// The notched red title ribbon. Height is the block it reserves in a vertical
    /// layout; the plate floats over tails that stick out at both ends.
    /// </summary>
    public static void Ribbon(RectTransform parent, string text, TMP_FontAsset font,
                              float titleSize, float plateWidth, float plateHeight, float blockHeight)
    {
        GameObject block = new GameObject("Ribbon", typeof(RectTransform));
        RectTransform rect = (RectTransform)block.transform;
        rect.SetParent(parent, false);
        block.AddComponent<LayoutElement>().preferredHeight = blockHeight;

        // Tails. The design notches these with a clip-path polygon; three hard
        // rectangles read the same at this size and suit the no-curves brief.
        GameObject tails = new GameObject("Tails", typeof(RectTransform));
        RectTransform tailsRect = (RectTransform)tails.transform;
        tailsRect.SetParent(rect, false);
        tailsRect.anchorMin = new Vector2(0f, 1f);
        tailsRect.anchorMax = new Vector2(1f, 1f);
        tailsRect.pivot = new Vector2(0.5f, 1f);
        tailsRect.sizeDelta = new Vector2(0f, 26f * Scale);
        tailsRect.anchoredPosition = new Vector2(0f, -26f * Scale);

        Stripe(tailsRect, 0f, 0.14f, 1f);
        Stripe(tailsRect, 0.86f, 1f, 1f);
        Stripe(tailsRect, 0.08f, 0.92f, 0.55f);

        // Hard drop shadow: the same plate offset down, no blur.
        Plate(rect, RibbonShadow, new Vector2(0f, -8f * Scale), plateWidth, plateHeight);
        RectTransform plate = Plate(rect, RibbonRed, Vector2.zero, plateWidth, plateHeight);

        RectTransform border = Full("Border", plate, RibbonBorder);
        Inset(Block("Inner", border, RibbonRed), 6f * Scale);

        GameObject content = Row("Content", plate, 18f * Scale);
        RectTransform contentRect = (RectTransform)content.transform;
        Stretch(contentRect, Vector2.zero);
        content.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;

        PlusMark((RectTransform)content.transform, true);
        Title((RectTransform)content.transform, text, font, titleSize, plateWidth * 0.72f);
        PlusMark((RectTransform)content.transform, false);
    }

    private static void Title(RectTransform parent, string text, TMP_FontAsset font,
                              float size, float width)
    {
        GameObject stack = new GameObject("Title", typeof(RectTransform));
        RectTransform rect = (RectTransform)stack.transform;
        rect.SetParent(parent, false);

        LayoutElement layout = stack.AddComponent<LayoutElement>();
        layout.preferredWidth = width;
        layout.preferredHeight = size * 1.25f;

        // Hard text shadow: a duplicate label behind, offset, no blur.
        TMP_Text shadow = Label(stack.transform, text, size, RibbonShadow, TextAlignmentOptions.Center, font);
        Stretch((RectTransform)shadow.transform, new Vector2(4f * Scale, -4f * Scale));
        shadow.characterSpacing = 5f;

        TMP_Text title = Label(stack.transform, text, size, Color.white, TextAlignmentOptions.Center, font);
        Stretch((RectTransform)title.transform, Vector2.zero);
        title.characterSpacing = 5f;
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

    private static RectTransform Plate(RectTransform parent, Color colour, Vector2 offset,
                                       float width, float height)
    {
        GameObject go = Block("Plate", parent, colour);
        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = new Vector2(width, height);
        rect.anchoredPosition = offset;

        return rect;
    }

    /// <summary>Four squares in a plus, per the design's box-shadow trick.</summary>
    public static void PlusMark(RectTransform parent, bool pointingRight)
    {
        float unit = 12f * Scale;

        GameObject mark = new GameObject("PlusMark", typeof(RectTransform));
        RectTransform rect = (RectTransform)mark.transform;
        rect.SetParent(parent, false);

        LayoutElement layout = mark.AddComponent<LayoutElement>();
        layout.preferredWidth = unit * 3f;
        layout.preferredHeight = unit * 3f;

        Vector2[] offsets =
        {
            Vector2.zero,
            new Vector2(0f, unit),
            new Vector2(0f, -unit),
            new Vector2(pointingRight ? unit : -unit, 0f),
        };

        foreach (Vector2 offset in offsets)
        {
            GameObject square = Block("Square", rect, Gold);
            RectTransform squareRect = (RectTransform)square.transform;
            squareRect.anchorMin = new Vector2(0.5f, 0.5f);
            squareRect.anchorMax = new Vector2(0.5f, 0.5f);
            squareRect.sizeDelta = new Vector2(unit, unit);
            squareRect.anchoredPosition = offset;
        }
    }

    /// <summary>The cyan-bordered panel — see the full overload below. Kept for existing callers.</summary>
    public static GameObject BorderedPanel(RectTransform parent, RectOffset padding, float spacing)
        => BorderedPanel(parent, padding, spacing, AccentCyan, Panel);

    /// <summary>Border colour only — see the full overload below. Fill stays RetroUI's own Panel tone.</summary>
    public static GameObject BorderedPanel(RectTransform parent, RectOffset padding, float spacing,
                                           Color borderColour)
        => BorderedPanel(parent, padding, spacing, borderColour, Panel);

    /// <summary>
    /// A border-coloured block with the fill colour inset, sized by its own content. Both
    /// colours are parameters rather than always AccentCyan/Panel so a screen with its own
    /// palette (see FinishScoreUI) isn't forced into the arcade-cabinet cyan/gold scheme.
    /// </summary>
    public static GameObject BorderedPanel(RectTransform parent, RectOffset padding, float spacing,
                                           Color borderColour, Color fillColour)
    {
        GameObject outer = Block("Panel", parent, borderColour);
        VerticalLayoutGroup outerLayout = outer.AddComponent<VerticalLayoutGroup>();
        outerLayout.padding = new RectOffset(5, 5, 5, 5);
        outerLayout.childForceExpandWidth = true;
        outerLayout.childForceExpandHeight = false;
        outer.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        GameObject inner = Block("PanelInner", (RectTransform)outer.transform, fillColour);
        VerticalLayoutGroup innerLayout = inner.AddComponent<VerticalLayoutGroup>();
        innerLayout.padding = padding;
        innerLayout.spacing = spacing;
        innerLayout.childForceExpandWidth = true;
        innerLayout.childForceExpandHeight = false;
        inner.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        return inner;
    }
}
