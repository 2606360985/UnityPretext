using System.Collections.Generic;
using System.Diagnostics;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class NeonMagazineSample : MonoBehaviour {
    [Header("Font")]
    [SerializeField] private TMP_FontAsset fontAsset;
    [SerializeField] private float fontSize = 18f;
    [SerializeField] private float lineHeight = 28f;

    [Header("Layout")]
    [SerializeField] private float gutter = 40f;
    [SerializeField] private float columnGap = 24f;
    [SerializeField] private float padding = 30f;

    [Header("Cards")]
    [SerializeField] private int cardCount = 5;
    [SerializeField] private float cardMinSize = 80f;
    [SerializeField] private float cardMaxSize = 140f;
    [SerializeField] private float cardDriftSpeed = 18f;
    [SerializeField] private float cardPadding = 10f;
    [SerializeField] private GameObject[] cardPrefabs;
    [SerializeField] private Mesh collisionMesh;
    [SerializeField] private float cardRotateSpeed = 30f;
    [SerializeField] private float modelDepthOffset = -1f;

    [Header("Neon Style")]
    [SerializeField] private Color neonCyan = new(0f, 1f, 1f, 0.9f);
    [SerializeField] private Color neonMagenta = new(1f, 0f, 1f, 0.9f);
    [SerializeField] private Color neonYellow = new(1f, 1f, 0f, 0.9f);
    [SerializeField] private Color neonGreen = new(0f, 1f, 0.5f, 0.9f);

    [Header("Performance")]
    [SerializeField] private bool showPerfPanel = true;

    [Header("References")]
    [SerializeField] private RectTransform container;
    [SerializeField] private RectTransform titleRect;
    [SerializeField] private TextMeshProUGUI perfText;
    [SerializeField] private Camera uiCamera;

    [TextArea(10, 30)]
    [SerializeField] private string sampleText =
        "NEON TEXT RENDERING// In the year 2049, text is no longer static glyphs arranged on a page. " +
        "It flows like liquid light through the circuits of quantum displays, adapting to every surface, every obstacle, every constraint. " +
        "The ancient rendering pipelines—designed for paper and ink—are obsolete. " +
        "What replaced them measures text in microseconds, reshapes it in real-time, flows it around images and advertisements without breaking stride. " +
        "This is Pretext: a text measurement engine built for the post-screen era.\n\n" +
        "Unlike traditional approaches that measure by rendering, Pretext prepares text as mathematical segments. " +
        "Each grapheme—every character or grapheme cluster—gets pre-calculated with its width, break properties, and segment classification. " +
        "Japanese Kinsoku rules are baked in. CJK line-breaking intelligence comes standard. " +
        "The result: layout calculations that run in 0.05 milliseconds instead of 30.\n\n" +
        "But raw speed is just the beginning. When text measures without rendering, it becomes a fluid material. " +
        "It can flow around images like water around stones. It can compress and expand as containers resize. " +
        "It can dance around floating cards in a magazine layout, reflowing as those cards drift and collide. " +
        "Traditional text rendering locks you into a single layout pass. Pretext unlocks dynamic composition.\n\n" +
        "Consider the humble message bubble. " +
        "In legacy systems, calculating the height of 500 message bubbles requires 500 synchronous layout passes. " +
        "The main thread freezes. Jank appears. Users complain. " +
        "Pretext prepares once, then measures any substring in constant time. " +
        "500 bubbles? 500,000 bubbles? The cost is the same: microseconds.\n\n" +
        "The technical secret is deconstruction. " +
        "Instead of one expensive operation (render to measure), Pretext separates concerns: " +
        "preparation happens once, measurement happens infinitely. " +
        "Segment classification handles Kinsoku and CJK rules. " +
        "Glyph measurement uses font metrics directly. " +
        "Layout engines consume pre-measured segments like a CNC machine reading a cut list.\n\n" +
        "This is the cyberpunk future of text: measured, classified, and ready to flow. " +
        "No reflows. No thrashing. No compromises. " +
        "Just pure, efficient, beautiful typography that bends around reality.";

    // Card state
    private struct CardState {
        public Transform transform3D;
        public MeshFilter meshFilter;
        public MeshRenderer meshRenderer;
        public Vector2 pos;      // canvas-space position (Y-down from top)
        public Vector2 vel;
        public Vector2 size;     // logical bounding size in canvas pixels
        public Color color;
        public float glowPhase;
    }

    // Projected card silhouette cache (computed once per frame)
    private struct ProjectedEdge {
        public Vector2 a;
        public Vector2 b;
    }

    private struct ProjectedCard {
        public List<ProjectedEdge> edges;
        public float minX, maxX, minY, maxY; // AABB for quick rejection
    }

    private CardState[] cards;
    private ProjectedCard[] projectedCards;
    private Canvas canvas;
    private PreparedText prepared;
    private float activeFontSize;
    private float activeLineHeight;
    private Vector2 lastContainerSize;
    private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
    private MaterialPropertyBlock mpb;

    // Text pooling
    private readonly List<TextMeshProUGUI> textPool = new();
    private int textPoolUsed;

    // Performance tracking
    private float prepareTime;
    private float reflowTime;
    private readonly List<float> fpsHistory = new();
    private const int FPS_SAMPLES = 30;

    // Neon glow animation
    private float neonTime;

    private void Start() {
        Application.targetFrameRate = 60;
        if (container == null) container = GetComponent<RectTransform>();
        canvas = container.GetComponentInParent<Canvas>();
        if (uiCamera == null) uiCamera = canvas.worldCamera;
        mpb = new MaterialPropertyBlock();
        UpdateFontSize();
        InitCards();
    }

    private void UpdateFontSize() {
        var rect = container.rect;
        var aspect = rect.height / Mathf.Max(rect.width, 1f);
        var scale = aspect > 1f ? Mathf.Lerp(1f, 2.5f, (aspect - 1f) / 1f) : 1f;
        activeFontSize = fontSize * scale;
        activeLineHeight = lineHeight * scale;

        var sw = new System.Diagnostics.Stopwatch();
        sw.Start();
        var options = PretextOptions.Default(fontAsset, activeFontSize);
        prepared = PretextPreparer.Prepare(sampleText, options);
        sw.Stop();
        prepareTime = (float)sw.Elapsed.TotalMilliseconds;
    }

    private void InitCards() {
        var rect = container.rect;
        cards = new CardState[cardCount];
        projectedCards = new ProjectedCard[cardCount];

        for (var i = 0; i < cardCount; i++) {
            var w = Random.Range(cardMinSize, cardMaxSize);
            var h = Random.Range(cardMinSize, cardMaxSize);

            // Random position within bounds
            var x = Random.Range(gutter + w * 0.5f + cardPadding, rect.width - gutter - w * 0.5f - cardPadding);
            var y = Random.Range(h * 0.5f + cardPadding + 60f, rect.height - h * 0.5f - cardPadding);

            // Random drift direction
            var angle = Random.Range(0f, Mathf.PI * 2f);
            var speed = Random.Range(cardDriftSpeed * 0.6f, cardDriftSpeed * 1.4f);

            // Color
            var colors = new[] { neonCyan, neonMagenta, neonYellow, neonGreen };
            var color = colors[i % colors.Length];

            // Instantiate 3D prefab or create default cube
            GameObject go;
            GameObject prefab = null;
            if (cardPrefabs != null && cardPrefabs.Length > 0)
                prefab = cardPrefabs[i % cardPrefabs.Length];

            if (prefab != null) {
                go = Instantiate(prefab);
            } else {
                go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);
            }
            go.name = $"Card3D_{i}";

            // Scale model uniformly to match card size in world units
            var worldSize = CanvasSizeToWorldSize(new Vector2(w, h));
            var uniformScale = Mathf.Min(worldSize.x, worldSize.y);
            go.transform.localScale = Vector3.one * uniformScale*0.5f;

            // Position in world space
            go.transform.position = CanvasPosToWorldPos(new Vector2(x, y));

            // Set up material with neon emission
            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null) mr = go.GetComponentInChildren<MeshRenderer>();
            if (mr != null) {
                var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.SetColor("_BaseColor", new Color(color.r * 0.15f, color.g * 0.15f, color.b * 0.15f, 1f));
                mat.EnableKeyword("_EMISSION");
                mat.SetColor(EmissionColor, color * 2f);
                mr.material = mat;
            }

            var mf = go.GetComponent<MeshFilter>();
            if (mf == null) mf = go.GetComponentInChildren<MeshFilter>();

            cards[i] = new CardState {
                transform3D = go.transform,
                meshFilter = mf,
                meshRenderer = mr,
                pos = new Vector2(x, y),
                vel = new Vector2(Mathf.Cos(angle) * speed, Mathf.Sin(angle) * speed),
                size = new Vector2(w, h),
                color = color,
                glowPhase = Random.Range(0f, Mathf.PI * 2f),
            };

            projectedCards[i] = new ProjectedCard { edges = new List<ProjectedEdge>(256) };
        }
    }

    private void Update() {
        var rect = container.rect;
        var size = new Vector2(rect.width, rect.height);

        // Detect resize
        if (size != lastContainerSize) {
            lastContainerSize = size;
            UpdateFontSize();
            RepositionCardsToFit();
        }

        neonTime += Time.deltaTime;
        UpdateCards(rect, Time.deltaTime);

        // FPS tracking
        fpsHistory.Add(1f / Time.deltaTime);
        if (fpsHistory.Count > FPS_SAMPLES) fpsHistory.RemoveAt(0);

        // Project mesh silhouettes before reflow
        ProjectAllCards();

        var sw = new System.Diagnostics.Stopwatch();
        sw.Start();
        Reflow();
        sw.Stop();
        reflowTime = (float)sw.Elapsed.TotalMilliseconds;

        if (showPerfPanel && perfText != null) {
            var avgFps = 0f;
            for (var i = 0; i < fpsHistory.Count; i++) avgFps += fpsHistory[i];
            avgFps /= fpsHistory.Count;
            perfText.text = $"PREP: {prepareTime:F3}ms | REFLOW: {reflowTime:F3}ms | FPS: {avgFps:F0}";
        }
    }

    private void RepositionCardsToFit() {
        var rect = container.rect;
        for (var i = 0; i < cards.Length; i++) {
            ref var card = ref cards[i];
            var hw = card.size.x * 0.5f;
            var hh = card.size.y * 0.5f;

            // Clamp to new bounds
            card.pos.x = Mathf.Clamp(card.pos.x, gutter + hw + cardPadding, rect.width - gutter - hw - cardPadding);
            card.pos.y = Mathf.Clamp(card.pos.y, hh + cardPadding + 60f, rect.height - hh - cardPadding);
            card.transform3D.position = CanvasPosToWorldPos(card.pos);
        }
    }

    private void UpdateCards(Rect rect, float dt) {
        var minX = gutter + cardMaxSize * 0.5f + cardPadding;
        var maxX = rect.width - gutter - cardMaxSize * 0.5f - cardPadding;
        var minY = cardMaxSize * 0.5f + cardPadding + 60f;
        var maxY = rect.height - cardMaxSize * 0.5f - cardPadding;

        for (var i = 0; i < cards.Length; i++) {
            ref var card = ref cards[i];
            card.pos += card.vel * dt;
            card.glowPhase += dt * 2f;

            // Bounce off walls
            if (card.pos.x < minX) { card.pos.x = minX; card.vel.x = Mathf.Abs(card.vel.x); }
            if (card.pos.x > maxX) { card.pos.x = maxX; card.vel.x = -Mathf.Abs(card.vel.x); }
            if (card.pos.y < minY) { card.pos.y = minY; card.vel.y = Mathf.Abs(card.vel.y); }
            if (card.pos.y > maxY) { card.pos.y = maxY; card.vel.y = -Mathf.Abs(card.vel.y); }

            // Update 3D position
            card.transform3D.position = CanvasPosToWorldPos(card.pos);

            // Slow rotation for visual effect
            card.transform3D.Rotate(Vector3.up, cardRotateSpeed * dt, Space.World);
            card.transform3D.Rotate(Vector3.right, cardRotateSpeed * 0.3f * dt, Space.World);

            // Pulsing emission glow via MaterialPropertyBlock
            if (card.meshRenderer != null) {
                var glow = 1f + 2f * Mathf.Sin(card.glowPhase);
                var emissive = card.color * Mathf.Max(glow, 0.3f);
                mpb.SetColor(EmissionColor, emissive);
                card.meshRenderer.SetPropertyBlock(mpb);
            }
        }
    }

    private void Reflow() {
        textPoolUsed = 0;
        var rect = container.rect;
        var totalWidth = rect.width - gutter * 2;

        // Determine column count based on width
        var colCount = totalWidth > 900 ? 3 : (totalWidth > 550 ? 2 : 1);
        var colWidth = (totalWidth - columnGap * (colCount - 1)) / colCount;
        var colXs = new float[colCount];
        for (var c = 0; c < colCount; c++) {
            colXs[c] = gutter + c * (colWidth + columnGap);
        }

        var titleHeight = titleRect != null ? titleRect.sizeDelta.y + 20f : 0f;
        var maxY = rect.height - padding;
        var cursor = 0;

        for (var col = 0; col < colCount && cursor < prepared.count; col++) {
            var colX = colXs[col];
            var colRight = colX + colWidth;
            var y = padding + titleHeight;

            while (cursor < prepared.count && y < maxY) {
                var intervals = GetCardExclusions(colX, colRight, y, activeLineHeight);

                if (intervals.Count == 0) {
                    var line = PretextLayout.LayoutNextLine(prepared, colWidth, cursor);
                    PlaceText(line, colX, y);
                    cursor = line.endIndex;
                } else {
                    var slotStart = colX;
                    foreach (var interval in intervals) {
                        var slotWidth = interval.start - slotStart;
                        if (slotWidth > activeFontSize * 1.5f && cursor < prepared.count) {
                            var line = PretextLayout.LayoutNextLine(prepared, slotWidth, cursor);
                            PlaceText(line, slotStart, y);
                            cursor = line.endIndex;
                        }
                        slotStart = interval.end;
                    }
                    var lastSlotWidth = colRight - slotStart;
                    if (lastSlotWidth > activeFontSize * 1.5f && cursor < prepared.count) {
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

    // --- Coordinate conversion helpers ---

    private Vector3 CanvasPosToWorldPos(Vector2 canvasPos) {
        // canvasPos: X = pixels from left, Y = pixels from top (Y-down)
        // Convert to screen-space pixel position, then to world point at model depth
        var scaleFactor = canvas.scaleFactor;
        var screenX = canvasPos.x * scaleFactor;
        var screenY = (lastContainerSize.y - canvasPos.y) * scaleFactor; // flip Y: canvas top→screen bottom
        var depth = canvas.planeDistance + modelDepthOffset;
        return uiCamera.ScreenToWorldPoint(new Vector3(screenX, screenY, depth));
    }

    private Vector2 WorldPosToCanvasPos(Vector3 worldPos) {
        var screenPos = uiCamera.WorldToScreenPoint(worldPos);
        var scaleFactor = canvas.scaleFactor;
        var canvasX = screenPos.x / scaleFactor;
        var canvasY = lastContainerSize.y - screenPos.y / scaleFactor; // flip Y back
        return new Vector2(canvasX, canvasY);
    }

    private Vector2 CanvasSizeToWorldSize(Vector2 canvasSize) {
        // Approximate world-space dimensions for a given canvas-pixel size
        var center = CanvasPosToWorldPos(new Vector2(lastContainerSize.x * 0.5f, lastContainerSize.y * 0.5f));
        var offset = CanvasPosToWorldPos(new Vector2(
            lastContainerSize.x * 0.5f + canvasSize.x * 0.5f,
            lastContainerSize.y * 0.5f + canvasSize.y * 0.5f));
        return new Vector2(
            Mathf.Abs(offset.x - center.x) * 2f,
            Mathf.Abs(offset.y - center.y) * 2f);
    }

    // --- Mesh silhouette projection ---

    private void ProjectAllCards() {
        for (var c = 0; c < cards.Length; c++) {
            ref var card = ref cards[c];
            ref var proj = ref projectedCards[c];
            proj.edges.Clear();

            var mesh = collisionMesh != null ? collisionMesh
                     : card.meshFilter != null ? card.meshFilter.sharedMesh
                     : null;
            if (mesh == null) {
                // Fallback: use logical AABB like the old code
                var hw = card.size.x * 0.5f + cardPadding;
                var hh = card.size.y * 0.5f + cardPadding;
                proj.minX = card.pos.x - hw;
                proj.maxX = card.pos.x + hw;
                proj.minY = card.pos.y - hh;
                proj.maxY = card.pos.y + hh;
                continue;
            }

            var xform = card.transform3D;
            var verts = mesh.vertices;
            var tris = mesh.triangles;

            // Project all vertices to canvas space
            var projected = new Vector2[verts.Length];
            float pMinX = float.MaxValue, pMaxX = float.MinValue;
            float pMinY = float.MaxValue, pMaxY = float.MinValue;

            for (var v = 0; v < verts.Length; v++) {
                var wp = xform.TransformPoint(verts[v]);
                var cp = WorldPosToCanvasPos(wp);
                projected[v] = cp;
                if (cp.x < pMinX) pMinX = cp.x;
                if (cp.x > pMaxX) pMaxX = cp.x;
                if (cp.y < pMinY) pMinY = cp.y;
                if (cp.y > pMaxY) pMaxY = cp.y;
            }

            // Add padding
            proj.minX = pMinX - cardPadding;
            proj.maxX = pMaxX + cardPadding;
            proj.minY = pMinY - cardPadding;
            proj.maxY = pMaxY + cardPadding;

            // Extract unique edges from triangles (silhouette edges)
            // For scan-line exclusion we only need the outer contour edges.
            // An edge shared by two triangles is interior; an edge used once is boundary.
            var edgeCounts = new Dictionary<long, (int a, int b)>();
            for (var t = 0; t < tris.Length; t += 3) {
                AddEdge(edgeCounts, tris[t], tris[t + 1]);
                AddEdge(edgeCounts, tris[t + 1], tris[t + 2]);
                AddEdge(edgeCounts, tris[t + 2], tris[t]);
            }

            // For closed meshes, all edges appear twice → use ALL edges for projection
            // (silhouette depends on view angle, so we keep all and let the scan-line sort it out)
            foreach (var kv in edgeCounts) {
                var pa = projected[kv.Value.a];
                var pb = projected[kv.Value.b];
                // Skip degenerate edges
                if (Vector2.SqrMagnitude(pa - pb) < 0.01f) continue;
                proj.edges.Add(new ProjectedEdge { a = pa, b = pb });
            }
        }
    }

    private static void AddEdge(Dictionary<long, (int a, int b)> dict, int a, int b) {
        var key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
        dict.TryAdd(key, (a, b));
    }

    private List<Interval> GetCardExclusions(float colX, float colRight, float lineY, float lh) {
        tempIntervals.Clear();
        var lineTop = lineY;
        var lineBottom = lineY + lh;

        for (var c = 0; c < projectedCards.Length; c++) {
            ref var proj = ref projectedCards[c];

            // Quick AABB rejection
            if (lineBottom < proj.minY || lineTop > proj.maxY) continue;
            if (proj.maxX < colX || proj.minX > colRight) continue;

            if (proj.edges.Count == 0) {
                // Fallback AABB exclusion (no mesh data)
                var clampedLeft = Mathf.Max(proj.minX, colX);
                var clampedRight = Mathf.Min(proj.maxX, colRight);
                if (clampedLeft < clampedRight) {
                    tempIntervals.Add(new Interval { start = clampedLeft, end = clampedRight });
                }
                continue;
            }

            // Scan-line: find X extents of projected mesh at this line's Y range
            var xMin = float.MaxValue;
            var xMax = float.MinValue;
            var hit = false;

            foreach (var edge in proj.edges) {
                // Find X range of edge segment that overlaps [lineTop, lineBottom]
                var ay = edge.a.y;
                var by = edge.b.y;
                var eMinY = Mathf.Min(ay, by);
                var eMaxY = Mathf.Max(ay, by);

                if (eMaxY < lineTop || eMinY > lineBottom) continue;

                // Clamp edge's Y range to [lineTop, lineBottom] and find corresponding X
                var ax = edge.a.x;
                var bx = edge.b.x;
                var dy = by - ay;

                if (Mathf.Abs(dy) < 0.001f) {
                    // Horizontal edge — use both endpoints
                    var x1 = Mathf.Min(ax, bx);
                    var x2 = Mathf.Max(ax, bx);
                    if (x1 < xMin) xMin = x1;
                    if (x2 > xMax) xMax = x2;
                    hit = true;
                } else {
                    // Compute X at line boundaries via linear interpolation
                    var y1 = Mathf.Max(eMinY, lineTop);
                    var y2 = Mathf.Min(eMaxY, lineBottom);
                    var t1 = (y1 - ay) / dy;
                    var t2 = (y2 - ay) / dy;
                    var x1 = ax + t1 * (bx - ax);
                    var x2 = ax + t2 * (bx - ax);
                    var segMin = Mathf.Min(x1, x2);
                    var segMax = Mathf.Max(x1, x2);
                    if (segMin < xMin) xMin = segMin;
                    if (segMax > xMax) xMax = segMax;
                    hit = true;
                }
            }

            if (!hit) continue;

            // Apply padding and clamp to column
            xMin -= cardPadding;
            xMax += cardPadding;
            var cLeft = Mathf.Max(xMin, colX);
            var cRight = Mathf.Min(xMax, colRight);
            if (cLeft < cRight) {
                tempIntervals.Add(new Interval { start = cLeft, end = cRight });
            }
        }

        if (tempIntervals.Count <= 1) return tempIntervals;

        // Sort by x position
        tempIntervals.Sort((a, b) => a.start.CompareTo(b.start));

        // Merge overlapping intervals
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

        var go = new GameObject($"Line_{textPoolUsed}", typeof(RectTransform));
        go.transform.SetParent(container, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font = fontAsset;
        tmp.fontSize = fontSize;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.color = neonCyan;
        tmp.raycastTarget = false;

        // Add shadow for glow effect
        var shadow = go.AddComponent<UnityEngine.UI.Shadow>();
        shadow.effectColor = new Color(0f, 0.8f, 1f, 0.5f);
        shadow.effectDistance = new Vector2(2f, -2f);

        textPool.Add(tmp);
        textPoolUsed++;
        return tmp;
    }
}
