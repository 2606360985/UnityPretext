using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class EditorialEngineSample : MonoBehaviour {
    [Header("Font")]
    [SerializeField] private TMP_FontAsset fontAsset;
    [SerializeField] private float fontSize = 18f;
    [SerializeField] private float lineHeight = 28f;

    [Header("Layout")]
    [SerializeField] private float gutter = 40f;
    [SerializeField] private float columnGap = 30f;
    [SerializeField] private float padding = 30f;

    [Header("Orb")]
    [SerializeField] private int orbCount = 4;
    [SerializeField] private float orbRadius = 60f;
    [SerializeField] private float orbSpeedMin = 25f;
    [SerializeField] private float orbSpeedMax = 55f;
    [SerializeField] private float orbPadding = 12f;
    [SerializeField] private Sprite orbSprite;

    [Header("References")]
    [SerializeField] private RectTransform container;

    [TextArea(10, 30)]
    [SerializeField] private string sampleText =
        "The web renders text through a pipeline that was designed thirty years ago for static documents. " +
        "A browser loads a font, shapes the text into glyphs, measures their combined width, determines where lines break, and positions each line vertically. " +
        "Every step depends on the previous one. Every step requires the rendering engine to consult its internal layout tree — a structure so expensive to maintain that browsers guard access to it behind synchronous reflow barriers that can freeze the main thread for tens of milliseconds at a time. " +
        "For a paragraph in a blog post, this pipeline is invisible. The browser loads, lays out, and paints before the reader's eye has traveled from the address bar to the first word. " +
        "But the web is no longer a collection of static documents. It is a platform for applications, and those applications need to know about text in ways the original pipeline never anticipated. " +
        "A messaging application needs to know the exact height of every message bubble before rendering a virtualized list. " +
        "A masonry layout needs the height of every card to position them without overlap. " +
        "An editorial page needs text to flow around images, advertisements, and interactive elements. " +
        "A responsive dashboard needs to resize and reflow text in real time as the user drags a panel divider. " +
        "Every one of these operations requires text measurement. And every text measurement on the web today requires a synchronous layout reflow. " +
        "The cost is devastating. Measuring the height of a single text block forces the browser to recalculate the position of every element on the page. " +
        "When you measure five hundred text blocks in sequence, you trigger five hundred full layout passes. " +
        "This pattern, known as layout thrashing, is the single largest source of jank on the modern web. " +
        "Chrome DevTools will flag it with angry red bars. Lighthouse will dock your performance score. " +
        "But the developer has no alternative — CSS provides no API for computing text height without rendering it. " +
        "The information is locked behind the DOM, and the DOM makes you pay for every answer. " +
        "Developers have invented increasingly desperate workarounds. Estimated heights replace real measurements with guesses, causing content to visibly jump when the guess is wrong. " +
        "ResizeObserver watches elements for size changes, but it fires asynchronously and always at least one frame too late. " +
        "The performance improvement is not incremental — it is categorical. 0.05ms versus 30ms. Zero reflows versus five hundred. " +
        "Text becomes a first-class participant in the visual composition — not a static block, but a fluid material that adapts in real time.";

    private struct OrbState {
        public Vector2 pos;
        public Vector2 vel;
        public RectTransform visual;
        public Color color;
    }

    private OrbState[] orbs;
    private PreparedText prepared;
    private float activeFontSize;
    private float activeLineHeight;

    private readonly List<TextMeshProUGUI> textPool = new();
    private int textPoolUsed;

    private static readonly Color[] OrbColors = {
        new(0.3f, 0.6f, 1f, 0.35f),
        new(1f, 0.4f, 0.5f, 0.35f),
        new(0.4f, 1f, 0.6f, 0.35f),
        new(1f, 0.8f, 0.3f, 0.35f),
    };

    private void Start() {
        UpdateFontSize();
        InitOrbs();
    }

    private void UpdateFontSize() {
        var rect = container.rect;
        var aspect = rect.height / Mathf.Max(rect.width, 1f);
        var scale = aspect > 1f ? Mathf.Lerp(1f, 3f, (aspect - 1f) / 1f) : 1f;
        activeFontSize = fontSize * scale;
        activeLineHeight = lineHeight * scale;
        var options = PretextOptions.Default(fontAsset, activeFontSize);
        prepared = PretextPreparer.Prepare(sampleText, options);
    }

    private void InitOrbs() {
        var rect = container.rect;
        orbs = new OrbState[orbCount];

        for (var i = 0; i < orbCount; i++) {
            // Random position
            var x = Random.Range(gutter + orbRadius + orbPadding, rect.width - gutter - orbRadius - orbPadding);
            var y = Random.Range(orbRadius + orbPadding, rect.height - orbRadius - orbPadding);

            // Random direction + speed
            var angle = Random.Range(0f, Mathf.PI * 2f);
            var speed = Random.Range(orbSpeedMin, orbSpeedMax);

            // Create visual
            var go = new GameObject($"Orb_{i}", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(container, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(orbRadius * 2, orbRadius * 2);

            var img = go.GetComponent<Image>();
            img.sprite = orbSprite;
            img.type = orbSprite != null ? Image.Type.Simple : Image.Type.Filled;
            img.color = OrbColors[i % OrbColors.Length];
            img.raycastTarget = false;

            orbs[i] = new OrbState {
                pos = new Vector2(x, y),
                vel = new Vector2(Mathf.Cos(angle) * speed, Mathf.Sin(angle) * speed),
                visual = rt,
                color = OrbColors[i % OrbColors.Length],
            };
        }
    }

    private Vector2 lastContainerSize;

    private void Update() {
        var rect = container.rect;
        var size = new Vector2(rect.width, rect.height);
        if (size != lastContainerSize) {
            lastContainerSize = size;
            UpdateFontSize();
        }
        UpdateOrbs();
        Reflow();
    }

    private void UpdateOrbs() {
        var rect = container.rect;
        var dt = Time.deltaTime;

        var minX = gutter + orbRadius + orbPadding;
        var maxX = rect.width - gutter - orbRadius - orbPadding;
        var minY = orbRadius + orbPadding;
        var maxY = rect.height - orbRadius - orbPadding;

        for (var i = 0; i < orbs.Length; i++) {
            ref var orb = ref orbs[i];
            orb.pos += orb.vel * dt;

            if (orb.pos.x < minX) { orb.pos.x = minX; orb.vel.x = Mathf.Abs(orb.vel.x); }
            if (orb.pos.x > maxX) { orb.pos.x = maxX; orb.vel.x = -Mathf.Abs(orb.vel.x); }
            if (orb.pos.y < minY) { orb.pos.y = minY; orb.vel.y = Mathf.Abs(orb.vel.y); }
            if (orb.pos.y > maxY) { orb.pos.y = maxY; orb.vel.y = -Mathf.Abs(orb.vel.y); }

            orb.visual.anchoredPosition = new Vector2(orb.pos.x, -orb.pos.y);
        }
    }

    private void Reflow() {
        textPoolUsed = 0;
        var rect = container.rect;
        var totalWidth = rect.width - gutter * 2;
        var colWidth = (totalWidth - columnGap) / 2f;
        var colXs = new[] { gutter, gutter + colWidth + columnGap };
        var maxY = rect.height - padding;

        var cursor = 0;

        for (var col = 0; col < 2 && cursor < prepared.count; col++) {
            var colX = colXs[col];
            var colRight = colX + colWidth;
            var y = padding;

            while (cursor < prepared.count && y < maxY) {
                var intervals = GetOrbExclusions(colX, colRight, y, activeLineHeight);

                if (intervals.Count == 0) {
                    var line = PretextLayout.LayoutNextLine(prepared, colWidth, cursor);
                    PlaceText(line, colX, y);
                    cursor = line.endIndex;
                } else {
                    var slotStart = colX;
                    foreach (var interval in intervals) {
                        var slotWidth = interval.start - slotStart;
                        if (slotWidth > activeFontSize * 2 && cursor < prepared.count) {
                            var line = PretextLayout.LayoutNextLine(prepared, slotWidth, cursor);
                            PlaceText(line, slotStart, y);
                            cursor = line.endIndex;
                        }
                        slotStart = interval.end;
                    }
                    var lastSlotWidth = colRight - slotStart;
                    if (lastSlotWidth > activeFontSize * 2 && cursor < prepared.count) {
                        var line = PretextLayout.LayoutNextLine(prepared, lastSlotWidth, cursor);
                        PlaceText(line, slotStart, y);
                        cursor = line.endIndex;
                    }
                }

                y += activeLineHeight;
            }
        }

        for (var i = textPoolUsed; i < textPool.Count; i++) {
            textPool[i].gameObject.SetActive(false);
        }
    }

    private struct Interval {
        public float start;
        public float end;
    }

    private readonly List<Interval> tempIntervals = new();

    private List<Interval> GetOrbExclusions(float colX, float colRight, float lineY, float lh) {
        tempIntervals.Clear();
        var lineMidY = lineY + lh * 0.5f;

        for (var i = 0; i < orbs.Length; i++) {
            var orb = orbs[i];
            var dy = Mathf.Abs(orb.pos.y - lineMidY);
            var totalR = orbRadius + orbPadding;
            if (dy >= totalR) continue;

            var halfChord = Mathf.Sqrt(totalR * totalR - dy * dy);
            var orbLeft = orb.pos.x - halfChord;
            var orbRight = orb.pos.x + halfChord;

            // Intersect with column bounds
            var clampedLeft = Mathf.Max(orbLeft, colX);
            var clampedRight = Mathf.Min(orbRight, colRight);
            if (clampedLeft >= clampedRight) continue;

            tempIntervals.Add(new Interval { start = clampedLeft, end = clampedRight });
        }

        // Sort by x position
        tempIntervals.Sort((a, b) => a.start.CompareTo(b.start));

        // Merge overlapping intervals
        if (tempIntervals.Count <= 1) return tempIntervals;

        var merged = new List<Interval> { tempIntervals[0] };
        for (var i = 1; i < tempIntervals.Count; i++) {
            var last = merged[merged.Count - 1];
            var curr = tempIntervals[i];
            if (curr.start <= last.end) {
                merged[merged.Count - 1] = new Interval { start = last.start, end = Mathf.Max(last.end, curr.end) };
            } else {
                merged.Add(curr);
            }
        }

        tempIntervals.Clear();
        tempIntervals.AddRange(merged);
        return tempIntervals;
    }

    private void PlaceText(PretextLine line, float x, float y) {
        if (line.endIndex <= line.startIndex) return;

        var text = BuildLineText(line);
        if (string.IsNullOrEmpty(text)) return;

        var tmp = GetPooledText();
        tmp.text = text;
        tmp.fontSize = activeFontSize;
        tmp.rectTransform.anchoredPosition = new Vector2(x, -y);
        tmp.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, line.width + activeFontSize);
        tmp.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, activeLineHeight);
    }

    private string BuildLineText(PretextLine line) {
        var sb = new System.Text.StringBuilder();
        for (var i = line.startIndex; i < line.endIndex && i < prepared.count; i++) {
            sb.Append(prepared.graphemes[i]);
        }
        return sb.ToString().TrimEnd();
    }

    private TextMeshProUGUI GetPooledText() {
        if (textPoolUsed < textPool.Count) {
            var existing = textPool[textPoolUsed];
            existing.gameObject.SetActive(true);
            textPoolUsed++;
            return existing;
        }

        var go = new GameObject($"Line_{textPoolUsed}", typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(container, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);

        var tmp = go.GetComponent<TextMeshProUGUI>();
        tmp.font = fontAsset;
        tmp.fontSize = fontSize;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.color = Color.white;

        textPool.Add(tmp);
        textPoolUsed++;
        return tmp;
    }
}
