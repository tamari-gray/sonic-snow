using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The race-start countdown, built to the design artifact: three starting lights that
/// fill one at a time, and a big number that pops in and changes colour each beat.
///
/// Background stays transparent — this overlays the live AR view, so a backdrop would
/// hide the run the rider is about to drop into.
///
/// The public surface is unchanged: <see cref="instance"/> and
/// <see cref="StartCountdown"/>, so GameLogic still drives it exactly as before.
/// </summary>
public class CountdownTimer : MonoBehaviour
{
    public static CountdownTimer instance;

    [Header("Fonts")]
    [Tooltip("Chunky face for GO!. The design calls for Press Start 2P.")]
    [SerializeField] private TMP_FontAsset displayFont;

    [Tooltip("Numerals for 3-2-1. The design calls for VT323.")]
    [SerializeField] private TMP_FontAsset numeralFont;

    [Header("Timing")]
    [Tooltip("Seconds each beat is held. The artifact ticks once a second.")]
    [SerializeField] private float stepDuration = 1f;

    [Tooltip("Length of the pop-in, in seconds.")]
    [SerializeField] private float popDuration = 0.42f;

    // Design tokens specific to this screen.
    private static readonly Color LightBoxBorder = RetroUI.Rule;
    private static readonly Color LightBoxFill   = RetroUI.Panel;
    private static readonly Color UnlitRing      = RetroUI.RibbonBorder;

    private const int Lights = 3;
    private const float LightBox = 74f;
    private const float LightDot = 44f;

    private RectTransform root;
    private RectTransform numberSlot;
    private Image[] dots;
    private Image[] rings;
    private Image[] glows;

    private void Awake()
    {
        // Each copy would build its own full-screen countdown and claim the static
        // instance, so several stack up and whichever woke last wins. Stand down loudly
        // instead of drawing on top of the real one.
        if (instance != null && instance != this)
        {
            Debug.LogWarning($"[CountdownTimer] '{name}' is a second CountdownTimer — " +
                             $"'{instance.name}' already claimed it. Disabling this one. " +
                             "Run Sonic Snow > Set Up Countdown to clean the scene up.");
            enabled = false;
            return;
        }

        instance = this;
        Build();
        root.gameObject.SetActive(false);
    }

    /// <summary>True while the countdown is on screen. Read by the automated flow test.</summary>
    public bool IsRunning => root != null && root.gameObject.activeSelf;

    public void StartCountdown(System.Action onComplete)
    {
        StartCoroutine(RunCountdown(onComplete));
    }

    /// <summary>
    /// Runs the countdown on demand so it can be checked without a GPS fix and a walk to
    /// the start line. Right-click the component header in the Inspector while in Play
    /// mode and pick "Preview Countdown".
    /// </summary>
    [ContextMenu("Preview Countdown")]
    private void PreviewCountdown()
    {
        if (!Application.isPlaying)
        {
            Debug.LogWarning("[CountdownTimer] Enter Play mode first — the countdown is a " +
                             "coroutine, and coroutines don't run in edit mode.");
            return;
        }

        StopAllCoroutines();
        StartCountdown(() => Debug.Log("[CountdownTimer] Preview finished."));
    }

    private IEnumerator RunCountdown(System.Action onComplete)
    {
        root.gameObject.SetActive(true);

        // step 0-2 count down from three; step 3 is GO.
        for (int step = 0; step <= Lights; step++)
        {
            bool go = step >= Lights;

            string label = go ? "GO!" : (Lights - step).ToString();
            Color colour = go ? RetroUI.Go
                         : step >= 2 ? RetroUI.AccentLocal
                         : step >= 1 ? RetroUI.Gold
                         : RetroUI.AccentCyan;

            SetLights(step, go);
            yield return StartCoroutine(PopNumber(label, colour, go));

            float remaining = stepDuration - popDuration;
            if (remaining > 0f) yield return new WaitForSeconds(remaining);
        }

        root.gameObject.SetActive(false);
        onComplete?.Invoke();
    }

    /// <summary>Fills one more light per beat; all three go green on GO.</summary>
    private void SetLights(int step, bool go)
    {
        Color lit = go ? RetroUI.Go : RetroUI.RibbonRed;

        for (int i = 0; i < Lights; i++)
        {
            bool on = go || i < step + 1;

            dots[i].color = on ? lit : RetroUI.LightOff;

            // The design's inset white ring on a lit light, dark ring when it's out.
            rings[i].color = on ? Color.Lerp(lit, Color.white, 0.22f) : UnlitRing;

            // Stand-in for the CSS 24px glow — UGUI has no blur, so this is a soft
            // square behind the dot. Reads the same at a glance on a phone.
            glows[i].color = new Color(lit.r, lit.g, lit.b, on ? 0.35f : 0f);
        }
    }

    /// <summary>
    /// The artifact's pop keyframe: scale 2.1 down to 1 with the opacity coming up,
    /// landing at 35% of the duration and holding.
    /// </summary>
    private IEnumerator PopNumber(string label, Color colour, bool go)
    {
        BuildNumber(label, colour, go);

        float landing = popDuration * 0.35f;
        float elapsed = 0f;

        while (elapsed < landing)
        {
            elapsed += Time.deltaTime;

            float t = Mathf.Clamp01(elapsed / landing);
            float eased = 1f - Mathf.Pow(1f - t, 3f);   // approximates cubic-bezier(.2,.9,.3,1)

            numberSlot.localScale = Vector3.one * Mathf.Lerp(2.1f, 1f, eased);
            SetNumberAlpha(eased);

            yield return null;
        }

        numberSlot.localScale = Vector3.one;
        SetNumberAlpha(1f);
    }

    private void SetNumberAlpha(float alpha)
    {
        foreach (TMP_Text text in numberSlot.GetComponentsInChildren<TMP_Text>())
        {
            Color c = text.color;
            c.a = text.name == "Shadow" ? alpha * 0.55f : alpha;
            text.color = c;
        }
    }

    // --- construction ----------------------------------------------------------

    private void Build()
    {
        GameObject rootObject = new GameObject("Countdown", typeof(RectTransform));
        root = (RectTransform)rootObject.transform;
        root.SetParent(transform, false);
        RetroUI.Stretch(root, Vector2.zero);

        GameObject column = new GameObject("Column", typeof(RectTransform));
        RectTransform columnRect = (RectTransform)column.transform;
        columnRect.SetParent(root, false);
        columnRect.anchorMin = new Vector2(0.5f, 0.5f);
        columnRect.anchorMax = new Vector2(0.5f, 0.5f);
        columnRect.pivot = new Vector2(0.5f, 0.5f);
        columnRect.sizeDelta = new Vector2(760f, 0f);

        VerticalLayoutGroup layout = column.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 26f;
        layout.childAlignment = TextAnchor.MiddleCenter;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;
        column.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        BuildLights(columnRect);

        GameObject slot = new GameObject("Number", typeof(RectTransform));
        numberSlot = (RectTransform)slot.transform;
        numberSlot.SetParent(columnRect, false);

        LayoutElement slotLayout = slot.AddComponent<LayoutElement>();
        slotLayout.preferredWidth = 760f;
        slotLayout.preferredHeight = 230f;
    }

    private void BuildLights(RectTransform parent)
    {
        GameObject row = RetroUI.Row("Lights", parent, 14f);
        row.GetComponent<HorizontalLayoutGroup>().childAlignment = TextAnchor.MiddleCenter;

        LayoutElement rowLayout = row.AddComponent<LayoutElement>();
        rowLayout.preferredHeight = LightDot + 28f + 10f;

        dots = new Image[Lights];
        rings = new Image[Lights];
        glows = new Image[Lights];

        for (int i = 0; i < Lights; i++)
        {
            GameObject box = RetroUI.Block("LightBox", (RectTransform)row.transform, LightBoxBorder);
            LayoutElement boxLayout = box.AddComponent<LayoutElement>();
            boxLayout.preferredWidth = LightBox;
            boxLayout.preferredHeight = LightDot + 28f;

            RetroUI.Inset(RetroUI.Block("BoxFill", (RectTransform)box.transform, LightBoxFill), 5f);

            // Glow sits behind the dot, so it's added first.
            glows[i] = Centred(box, "Glow", LightDot + 24f).GetComponent<Image>();

            GameObject ring = Centred(box, "Ring", LightDot);
            rings[i] = ring.GetComponent<Image>();

            GameObject dot = RetroUI.Block("Dot", (RectTransform)ring.transform, RetroUI.LightOff);
            RetroUI.Inset(dot, 6f);
            dots[i] = dot.GetComponent<Image>();
        }
    }

    private static GameObject Centred(GameObject parent, string name, float size)
    {
        GameObject go = RetroUI.Block(name, (RectTransform)parent.transform, Color.clear);
        RectTransform rect = (RectTransform)go.transform;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.sizeDelta = new Vector2(size, size);
        rect.anchoredPosition = Vector2.zero;

        return go;
    }

    private void BuildNumber(string label, Color colour, bool go)
    {
        foreach (Transform child in numberSlot) Destroy(child.gameObject);

        float size = go ? 96f : 200f;
        float spacing = go ? 8f : 0f;
        TMP_FontAsset font = go ? displayFont : numeralFont;

        // Hard 8px drop shadow, no blur — a duplicate label behind the real one.
        TMP_Text shadow = RetroUI.Label(numberSlot, label, size,
            new Color(0f, 0f, 0f, 0.55f), TextAlignmentOptions.Center, font);
        shadow.name = "Shadow";
        RetroUI.Stretch((RectTransform)shadow.transform, new Vector2(8f, -8f));
        shadow.characterSpacing = spacing;

        TMP_Text face = RetroUI.Label(numberSlot, label, size, colour, TextAlignmentOptions.Center, font);
        face.name = "Face";
        RetroUI.Stretch((RectTransform)face.transform, Vector2.zero);
        face.characterSpacing = spacing;
    }
}
