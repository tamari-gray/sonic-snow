using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The finish-line results screen, built to the "Finish Line Score" design (Claude Design
/// project a496b48d-6afc-420a-bd9b-ea77cf691c95, file "Finish Line Score.dc.html"). Shown
/// once GameLogic confirms the rider crossed the finish line: name, elapsed time,
/// checkpoints collected, and score.
///
/// Its own blue/orange palette rather than RetroUI's cyan/magenta/gold — that's the design
/// as handed off, and it reads as a deliberately different "results" beat rather than
/// another arcade-cabinet screen. Layout helpers (Block, Label, Stretch, BorderedPanel) are
/// still shared with RetroUI; only the colours and text are local to this screen.
///
/// Background stays transparent — same reasoning as CountdownTimer: this overlays the live
/// AR view right at the finish line, and a solid backdrop would hide the run just ridden.
/// </summary>
public class FinishScoreUI : MonoBehaviour
{
    public static FinishScoreUI Instance;

    /// <summary>True while the results screen is on screen. Read by the automated flow test.</summary>
    public bool IsShowing => root != null && root.gameObject.activeSelf;

    /// <summary>The score last computed by Show(), for tests to sanity-check without
    /// re-parsing the label text.</summary>
    public long LastScore { get; private set; }

    [Header("Font")]
    [Tooltip("The design uses Press Start 2P for every piece of text on this screen, unlike " +
             "the display/numeral split elsewhere. The project's arcade face stands in until " +
             "Press Start 2P itself is imported.")]
    [SerializeField] private TMP_FontAsset font;

    [Header("Scoring")]
    [Tooltip("Points awarded per checkpoint collected. The dominant term in the score — see " +
             "ComputeScore's doc comment for why.")]
    [SerializeField] private int checkpointStep = 10_000;

    [Tooltip("Runs slower than this earn no time bonus at all, in seconds.")]
    [SerializeField] private float slowestScoringRun = 200f;

    [Tooltip("Bonus for collecting every checkpoint on the route, on top of the per-checkpoint award.")]
    [SerializeField] private int completionBonus = 5000;

    [Tooltip("How long one pulse (scale up, scale back) of the score number takes, in seconds. " +
             "Matches the design's scorePulse keyframe, stepped rather than eased — the whole " +
             "design animates in hard steps() flips, not smooth interpolation.")]
    [SerializeField] private float scorePulseSeconds = 1.2f;

    // --- design tokens, verbatim from the handoff -------------------------------
    private static readonly Color BgPanelDeep = Hex("05020b");
    private static readonly Color BgPanel     = Hex("0d0618");
    private static readonly Color BgRow       = Hex("150a26");
    private static readonly Color Blue        = Hex("2f6fe0");
    private static readonly Color Orange      = Hex("ff8a1e");

    private RectTransform root;
    private TMP_Text nameLabel;
    private TMP_Text timeValue;
    private TMP_Text checkpointsValue;
    private TMP_Text scoreLabel;
    private Coroutine pulseRoutine;

    private void Awake()
    {
        // Same reasoning as every other retro screen here: two copies would each build
        // their own full-screen overlay and race for the static instance.
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning($"[FinishScoreUI] '{name}' is a second FinishScoreUI — " +
                             $"'{Instance.name}' already claimed it. Disabling this one. " +
                             "Run Sonic Snow > Set Up Finish Score to clean the scene up.");
            enabled = false;
            return;
        }

        Instance = this;
        Build();
        root.gameObject.SetActive(false);
    }

    /// <summary>
    /// Shows the results for this run. Call once, right when the finish line triggers —
    /// see GameLogic.OnFinishLineReached().
    /// </summary>
    /// <param name="playerName">Shown verbatim, upper-cased — matches the design's all-caps style.</param>
    /// <param name="elapsedSeconds">From RaceTimer.ElapsedTime, already stopped.</param>
    /// <param name="checkpoints">Checkpoints actually collected this run.</param>
    /// <param name="totalCheckpoints">Checkpoints the route has. &lt;= 0 means the completion
    /// bonus never applies — a route with no checkpoints can't meaningfully be "completed".</param>
    public void Show(string playerName, float elapsedSeconds, int checkpoints, int totalCheckpoints)
    {
        if (root != null) root.gameObject.SetActive(true);

        if (nameLabel != null)
            nameLabel.text = string.IsNullOrEmpty(playerName) ? "PLAYER" : playerName.ToUpperInvariant();

        if (timeValue != null) timeValue.text = FormatTime(elapsedSeconds);
        if (checkpointsValue != null) checkpointsValue.text = $"{checkpoints}/{totalCheckpoints}";

        long score = ComputeScore(checkpoints, totalCheckpoints, elapsedSeconds);
        LastScore = score;
        if (scoreLabel != null) scoreLabel.text = score.ToString("N0");

        if (pulseRoutine != null) StopCoroutine(pulseRoutine);
        pulseRoutine = StartCoroutine(PulseScore());
    }

    public void Hide()
    {
        if (root != null) root.gameObject.SetActive(false);

        if (pulseRoutine != null)
        {
            StopCoroutine(pulseRoutine);
            pulseRoutine = null;
        }
    }

    /// <summary>
    /// Checkpoints dominate, time only breaks the tie between two runs that collected the
    /// same number. This is the same split classic score systems built around collectibles
    /// use — Sonic's ring bonus against its tiered time bonus, Crash Bandicoot's box count
    /// against its level-clear bonus, old checkpoint racers like Out Run or Excitebike
    /// stacking a speed bonus on top of a fixed per-gate award rather than letting speed
    /// alone carry the score. The time bonus here is capped strictly below one checkpoint's
    /// worth of points, so however fast a run was, it can never outscore a run that
    /// collected more checkpoints — it can only edge out another run tied on checkpoints.
    /// The completion bonus is additive on top and only reachable at the max checkpoint
    /// count anyway, so it can't break that guarantee either.
    /// </summary>
    private long ComputeScore(int checkpoints, int totalCheckpoints, float elapsedSeconds)
    {
        float speed = Mathf.Clamp01((slowestScoringRun - elapsedSeconds) / slowestScoringRun);
        long timeBonus = (long)(speed * (checkpointStep - 1));

        long score = (long)checkpoints * checkpointStep + timeBonus;

        if (totalCheckpoints > 0 && checkpoints >= totalCheckpoints)
            score += completionBonus;

        return score;
    }

    private static string FormatTime(float seconds)
    {
        int minutes = Mathf.FloorToInt(seconds / 60f);
        float remainder = Mathf.Max(0f, seconds - minutes * 60f);
        return $"{minutes}:{remainder:00.00}";
    }

    /// <summary>Hard two-state flip, not a smooth tween — matches the design's steps(1, end)
    /// animations, which run this way throughout rather than easing.</summary>
    private IEnumerator PulseScore()
    {
        RectTransform scoreRect = (RectTransform)scoreLabel.transform;
        float half = scorePulseSeconds * 0.5f;

        while (true)
        {
            scoreRect.localScale = Vector3.one;
            yield return new WaitForSeconds(half);

            scoreRect.localScale = Vector3.one * 1.07f;
            yield return new WaitForSeconds(half);
        }
    }

    // --- construction ------------------------------------------------------------

    private void Build()
    {
        GameObject rootObject = new GameObject("FinishScore", typeof(RectTransform));
        root = (RectTransform)rootObject.transform;
        root.SetParent(transform, false);
        RetroUI.Stretch(root, Vector2.zero);

        GameObject column = new GameObject("Column", typeof(RectTransform));
        RectTransform columnRect = (RectTransform)column.transform;
        columnRect.SetParent(root, false);
        columnRect.anchorMin = new Vector2(0.5f, 0.5f);
        columnRect.anchorMax = new Vector2(0.5f, 0.5f);
        columnRect.pivot = new Vector2(0.5f, 0.5f);
        columnRect.sizeDelta = Vector2.zero;

        VerticalLayoutGroup columnLayout = column.AddComponent<VerticalLayoutGroup>();
        columnLayout.spacing = 18f * RetroUI.Scale;
        columnLayout.childAlignment = TextAnchor.MiddleCenter;
        // Not force-expanded: the badge hugs its own text width in the handoff, only the
        // big panel below it is width:100%. Each child sets its own preferred width instead.
        columnLayout.childForceExpandWidth = false;
        columnLayout.childForceExpandHeight = false;
        // Width is set explicitly on Badge/Panel themselves (see BuildBadge/BuildPanel)
        // rather than left to childControlWidth. That flag was tried first — it's the usual
        // fix for "LayoutElement.preferredWidth being ignored" — but even with it on,
        // LayoutUtility.GetPreferredWidth kept returning 0 for these children despite their
        // LayoutElement correctly reporting 760/1240, for reasons that didn't resolve under
        // investigation 2026-08-17 (height computed correctly the whole time via the same
        // group, only width was wrong — an asymmetry that pointed at something specific to
        // this nesting rather than a simple missing flag). Explicit sizeDelta sidesteps it
        // entirely. Height still comes from the group normally.
        columnLayout.childControlWidth = false;
        columnLayout.childControlHeight = true;
        column.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        BuildBadge(columnRect);
        BuildPanel(columnRect);
    }

    private void BuildBadge(RectTransform parent)
    {
        GameObject outer = RetroUI.Block("Badge", parent, Blue);
        LayoutElement outerLayout = outer.AddComponent<LayoutElement>();
        outerLayout.preferredWidth = 380f * RetroUI.Scale;
        outerLayout.preferredHeight = 78f * RetroUI.Scale;
        // childControlWidth is off on the parent column now (see Build's comment) — set width
        // directly rather than trust the group to read it back off this LayoutElement.
        RectTransform outerRect = (RectTransform)outer.transform;
        outerRect.sizeDelta = new Vector2(outerLayout.preferredWidth, outerRect.sizeDelta.y);

        GameObject inner = RetroUI.Block("BadgeInner", (RectTransform)outer.transform, BgPanel);
        RetroUI.Inset(inner, 6f * RetroUI.Scale);

        // Hard drop shadow: a duplicate label behind the real one, offset, no blur — same
        // trick RetroUI.Title and CountdownTimer's numerals use.
        TMP_Text shadow = RetroUI.Label(inner.transform, "FINISH!", 26f * RetroUI.Scale, Orange,
            TextAlignmentOptions.Center, font);
        RetroUI.Stretch((RectTransform)shadow.transform, new Vector2(3f * RetroUI.Scale, -3f * RetroUI.Scale));

        TMP_Text title = RetroUI.Label(inner.transform, "FINISH!", 26f * RetroUI.Scale, Color.white,
            TextAlignmentOptions.Center, font);
        RetroUI.Stretch((RectTransform)title.transform, Vector2.zero);
    }

    private void BuildPanel(RectTransform parent)
    {
        RectOffset padding = new RectOffset(
            Mathf.RoundToInt(26f * RetroUI.Scale), Mathf.RoundToInt(26f * RetroUI.Scale),
            Mathf.RoundToInt(22f * RetroUI.Scale), Mathf.RoundToInt(24f * RetroUI.Scale));

        GameObject inner = RetroUI.BorderedPanel(parent, padding, 16f * RetroUI.Scale, Orange, BgPanel);
        RectTransform innerRect = (RectTransform)inner.transform;

        // Full column width — width:100% in the handoff, unlike the badge above it.
        GameObject outer = inner.transform.parent.gameObject;
        LayoutElement outerLayout = outer.AddComponent<LayoutElement>();
        outerLayout.preferredWidth = 620f * RetroUI.Scale;
        // See BuildBadge's comment — width set directly, not left to the parent group.
        RectTransform outerRect = (RectTransform)outer.transform;
        outerRect.sizeDelta = new Vector2(outerLayout.preferredWidth, outerRect.sizeDelta.y);

        nameLabel = RetroUI.Label(innerRect, "PLAYER", 17f * RetroUI.Scale, Color.white,
            TextAlignmentOptions.Center, font);
        nameLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 26f * RetroUI.Scale;

        BuildStatsRows(innerRect);
        BuildScoreBox(innerRect);
    }

    private void BuildStatsRows(RectTransform parent)
    {
        GameObject stats = new GameObject("Stats", typeof(RectTransform));
        RectTransform statsRect = (RectTransform)stats.transform;
        statsRect.SetParent(parent, false);

        VerticalLayoutGroup layout = stats.AddComponent<VerticalLayoutGroup>();
        layout.spacing = 10f * RetroUI.Scale;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        stats.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        timeValue = BuildStatRow(statsRect, "TIME", Blue);
        checkpointsValue = BuildStatRow(statsRect, "CHECKPOINTS", Orange);
    }

    /// <summary>One label-left/value-right stat row. Both labels stretch across the full
    /// row and rely on their own alignment to land on opposite sides — matches the design's
    /// justify-content: space-between without needing a flexible spacer element.</summary>
    private TMP_Text BuildStatRow(RectTransform parent, string label, Color accent)
    {
        GameObject outer = RetroUI.Block("StatRow", parent, accent);
        LayoutElement outerLayout = outer.AddComponent<LayoutElement>();
        outerLayout.preferredHeight = 52f * RetroUI.Scale;

        GameObject inner = RetroUI.Block("StatRowInner", (RectTransform)outer.transform, BgRow);
        RetroUI.Inset(inner, 4f * RetroUI.Scale);

        TMP_Text labelText = RetroUI.Label(inner.transform, label, 11f * RetroUI.Scale, accent,
            TextAlignmentOptions.Left, font);
        RectTransform labelRect = (RectTransform)labelText.transform;
        RetroUI.Stretch(labelRect, Vector2.zero);
        labelRect.offsetMin = new Vector2(18f * RetroUI.Scale, 0f);

        TMP_Text valueText = RetroUI.Label(inner.transform, "--", 16f * RetroUI.Scale, Color.white,
            TextAlignmentOptions.Right, font);
        RectTransform valueRect = (RectTransform)valueText.transform;
        RetroUI.Stretch(valueRect, Vector2.zero);
        valueRect.offsetMax = new Vector2(-18f * RetroUI.Scale, 0f);

        return valueText;
    }

    private void BuildScoreBox(RectTransform parent)
    {
        RectOffset padding = new RectOffset(
            Mathf.RoundToInt(18f * RetroUI.Scale), Mathf.RoundToInt(18f * RetroUI.Scale),
            Mathf.RoundToInt(14f * RetroUI.Scale), Mathf.RoundToInt(14f * RetroUI.Scale));

        GameObject inner = RetroUI.BorderedPanel(parent, padding, 6f * RetroUI.Scale, Blue, BgPanelDeep);
        RectTransform innerRect = (RectTransform)inner.transform;

        VerticalLayoutGroup innerLayout = inner.GetComponent<VerticalLayoutGroup>();
        innerLayout.childAlignment = TextAnchor.MiddleCenter;

        TMP_Text scoreCaption = RetroUI.Label(innerRect, "SCORE", 11f * RetroUI.Scale, Blue,
            TextAlignmentOptions.Center, font);
        scoreCaption.gameObject.AddComponent<LayoutElement>().preferredHeight = 18f * RetroUI.Scale;

        scoreLabel = RetroUI.Label(innerRect, "0", 32f * RetroUI.Scale, Color.white,
            TextAlignmentOptions.Center, font);
        scoreLabel.gameObject.AddComponent<LayoutElement>().preferredHeight = 40f * RetroUI.Scale;
    }

    private static Color Hex(string hex)
    {
        ColorUtility.TryParseHtmlString("#" + hex, out Color c);
        return c;
    }
}
