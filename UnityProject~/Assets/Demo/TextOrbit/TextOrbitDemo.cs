using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class TextOrbitDemo : MonoBehaviour {
    [Header("Font")]
    [SerializeField] private TMP_FontAsset fontAsset;
    [SerializeField] private float fontSize = 20f;
    [SerializeField] private float lineHeight = 30f;

    [Header("Layout")]
    [SerializeField] private float gutter = 60f;
    [SerializeField] private float padding = 40f;

    [Header("Model")]
    [SerializeField] private GameObject modelPrefab;
    [SerializeField] private Mesh collisionMesh;
    [SerializeField] private float modelScale = 0.35f;
    [SerializeField] private float modelRotateSpeed = 20f;
    [SerializeField] private float modelDepthOffset = -1f;
    [SerializeField] private float hullPadding = 20f;

    [Header("Animation")]
    [SerializeField] private float ropeSpeed = 400f;
    [SerializeField] private float settleDuration = 0.6f;
    [SerializeField] private float charSpacingScale = 1f;

    [Header("Neon Style")]
    [SerializeField] private Color neonCyan = new(0f, 1f, 1f, 0.9f);
    [SerializeField] private Color neonMagenta = new(1f, 0f, 1f, 0.9f);
    [SerializeField] private Color textColor = new(0.9f, 0.95f, 1f, 1f);

    [Header("Performance")]
    [SerializeField] private bool showPerfPanel = true;

    [Header("References")]
    [SerializeField] private RectTransform container;
    [SerializeField] private TextMeshProUGUI perfText;
    [SerializeField] private Camera uiCamera;

    [TextArea(10, 30)]
    [SerializeField] private string sampleText =
        "In the year 2049, text is no longer static glyphs arranged on a page. " +
        "It flows like liquid light through the circuits of quantum displays, " +
        "adapting to every surface, every obstacle, every constraint. " +
        "The ancient rendering pipelines are obsolete. " +
        "What replaced them measures text in microseconds, reshapes it in real-time, " +
        "flows it around images and advertisements without breaking stride. " +
        "This is Pretext: a text measurement engine built for the post-screen era. " +
        "Unlike traditional approaches that measure by rendering, " +
        "Pretext prepares text as mathematical segments. " +
        "Each grapheme gets pre-calculated with its width, break properties, " +
        "and segment classification. " +
        "The result: layout calculations that run in microseconds.";

    // --- Internal state ---
    private Canvas canvas;
    private Transform modelTransform;
    private MeshFilter modelMeshFilter;
    private MeshRenderer modelMeshRenderer;
    private MaterialPropertyBlock mpb;
    private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

    private PreparedText prepared;
    private float activeFontSize;
    private float activeLineHeight;
    private Vector2 lastContainerSize;

    // Per-grapheme final positions (canvas coords, Y-down)
    private Vector2[] finalPositions;

    // Cumulative width along the rope: cumWidths[i] = total width of chars [0..i-1]
    private float[] cumWidths;

    // Rope animation state
    private float ropeHeadDist;   // total distance the rope head has traveled along the path
    private bool animDone;

    // Path definition (updated each frame from hull)
    private Vector2 entryStart;   // off-screen bottom center
    private Vector2 entryEnd;     // hull bottom point (entry/exit anchor)
    private float entryLength;    // distance(entryStart, entryEnd)
    private float pathLength;     // entryLength + hullPerimeter

    // Convex hull (recomputed each frame while animating)
    private readonly List<Vector2> hullPoints = new();
    private readonly List<float> hullArcLengths = new();
    private float hullPerimeter;
    private int hullBottomIndex;

    // TMP object pools
    private readonly List<TextMeshProUGUI> glyphPool = new();
    private readonly List<TextMeshProUGUI> linePool = new();
    private int glyphPoolUsed;
    private int linePoolUsed;

    // Performance
    private float prepareTimeMs;
    private readonly List<float> fpsHistory = new();
    private const int FPS_SAMPLES = 30;

    // =====================================================================
    //  LIFECYCLE
    // =====================================================================

    private void Start() {
        Application.targetFrameRate = 60;
        if (container == null) container = GetComponent<RectTransform>();
        canvas = container.GetComponentInParent<Canvas>();
        if (uiCamera == null) uiCamera = canvas.worldCamera;
        mpb = new MaterialPropertyBlock();

        var rect = container.rect;
        lastContainerSize = new Vector2(rect.width, rect.height);

        PrepareText();
        ComputeFinalLayout();
        ComputeCumulativeWidths();
        InitModel();
    }

    private void Update() {
        var rect = container.rect;
        var size = new Vector2(rect.width, rect.height);
        if (size != lastContainerSize) {
            lastContainerSize = size;
            PrepareText();
            ComputeFinalLayout();
            ComputeCumulativeWidths();
            RepositionModel();
            animDone = false;
            ropeHeadDist = 0f;
        }

        // FPS tracking
        fpsHistory.Add(1f / Time.deltaTime);
        if (fpsHistory.Count > FPS_SAMPLES) fpsHistory.RemoveAt(0);

        if (!animDone) {
            // Rotate model
            modelTransform.Rotate(Vector3.up, modelRotateSpeed * Time.deltaTime, Space.World);
            modelTransform.Rotate(Vector3.right, modelRotateSpeed * 0.2f * Time.deltaTime, Space.World);

            // Pulsing model glow
            if (modelMeshRenderer != null) {
                var glow = 1f + 1.5f * Mathf.Sin(ropeHeadDist * 0.01f);
                mpb.SetColor(EmissionColor, neonMagenta * Mathf.Max(glow, 0.3f));
                modelMeshRenderer.SetPropertyBlock(mpb);
            }

            // Recompute hull & path metrics
            ComputeConvexHull();
            UpdatePathMetrics();

            // Advance rope head
            ropeHeadDist += ropeSpeed * Time.deltaTime;

            // Animate all characters along the rope
            AnimateRope();

            // Check if last character has fully settled
            var settleRange = settleDuration * ropeSpeed;
            var lastPathDist = ropeHeadDist - cumWidths[prepared.count - 1];
            if (lastPathDist >= pathLength + settleRange) {
                animDone = true;
                ConsolidateToLines();
            }
        }

        if (showPerfPanel && perfText != null) {
            var avgFps = 0f;
            for (var i = 0; i < fpsHistory.Count; i++) avgFps += fpsHistory[i];
            avgFps /= Mathf.Max(fpsHistory.Count, 1);
            var phase = animDone ? "DONE" : "ANIM";
            perfText.text = $"PREP: {prepareTimeMs:F3}ms | {phase} | FPS: {avgFps:F0}";
        }
    }

    // =====================================================================
    //  TEXT PREPARATION & FINAL LAYOUT
    // =====================================================================

    private void PrepareText() {
        var rect = container.rect;
        var aspect = rect.height / Mathf.Max(rect.width, 1f);
        var scale = aspect > 1f ? Mathf.Lerp(1f, 2.5f, (aspect - 1f) / 1f) : 1f;
        activeFontSize = fontSize * scale;
        activeLineHeight = lineHeight * scale;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var options = PretextOptions.Default(fontAsset, activeFontSize);
        prepared = PretextPreparer.Prepare(sampleText, options);
        sw.Stop();
        prepareTimeMs = (float)sw.Elapsed.TotalMilliseconds;
    }

    private void ComputeFinalLayout() {
        finalPositions = new Vector2[prepared.count];
        var layoutWidth = lastContainerSize.x - gutter * 2f;
        var cursor = 0;
        var y = padding;

        while (cursor < prepared.count) {
            var line = PretextLayout.LayoutNextLine(prepared, layoutWidth, cursor);
            var xOff = 0f;
            for (var i = line.startIndex; i < line.endIndex && i < prepared.count; i++) {
                finalPositions[i] = new Vector2(gutter + xOff, y);
                xOff += prepared.widths[i];
            }
            cursor = line.endIndex;
            y += activeLineHeight;
        }
    }

    private void ComputeCumulativeWidths() {
        cumWidths = new float[prepared.count];
        var acc = 0f;
        for (var i = 0; i < prepared.count; i++) {
            cumWidths[i] = acc;
            acc += prepared.widths[i] * charSpacingScale;
        }
    }

    // =====================================================================
    //  3D MODEL
    // =====================================================================

    private void InitModel() {
        GameObject go;
        if (modelPrefab != null) {
            go = Instantiate(modelPrefab);
        } else {
            go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
        }
        go.name = "OrbitModel";

        var canvasSize = new Vector2(
            lastContainerSize.x * modelScale,
            lastContainerSize.y * modelScale);
        var worldSize = CanvasSizeToWorldSize(canvasSize);
        var uniformScale = Mathf.Min(worldSize.x, worldSize.y);
        go.transform.localScale = Vector3.one * uniformScale * 0.5f;

        var center = new Vector2(lastContainerSize.x * 0.5f, lastContainerSize.y * 0.5f);
        go.transform.position = CanvasPosToWorldPos(center);

        var mr = go.GetComponent<MeshRenderer>();
        if (mr == null) mr = go.GetComponentInChildren<MeshRenderer>();
        if (mr != null) {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", new Color(neonMagenta.r * 0.1f, neonMagenta.g * 0.1f, neonMagenta.b * 0.1f, 1f));
            mat.EnableKeyword("_EMISSION");
            mat.SetColor(EmissionColor, neonMagenta * 2f);
            mr.material = mat;
        }

        var mf = go.GetComponent<MeshFilter>();
        if (mf == null) mf = go.GetComponentInChildren<MeshFilter>();

        modelTransform = go.transform;
        modelMeshFilter = mf;
        modelMeshRenderer = mr;
    }

    private void RepositionModel() {
        if (modelTransform == null) return;
        var center = new Vector2(lastContainerSize.x * 0.5f, lastContainerSize.y * 0.5f);
        modelTransform.position = CanvasPosToWorldPos(center);

        var canvasSize = new Vector2(
            lastContainerSize.x * modelScale,
            lastContainerSize.y * modelScale);
        var worldSize = CanvasSizeToWorldSize(canvasSize);
        var uniformScale = Mathf.Min(worldSize.x, worldSize.y);
        modelTransform.localScale = Vector3.one * uniformScale * 0.5f;
    }

    // =====================================================================
    //  CONVEX HULL
    // =====================================================================

    private void ComputeConvexHull() {
        hullPoints.Clear();
        hullArcLengths.Clear();
        hullPerimeter = 0f;

        var mesh = collisionMesh != null ? collisionMesh
                 : modelMeshFilter != null ? modelMeshFilter.sharedMesh
                 : null;

        if (mesh == null || modelTransform == null) {
            var center = new Vector2(lastContainerSize.x * 0.5f, lastContainerSize.y * 0.5f);
            var radius = Mathf.Min(lastContainerSize.x, lastContainerSize.y) * modelScale * 0.3f + hullPadding;
            const int segments = 32;
            for (var i = 0; i < segments; i++) {
                var angle = Mathf.PI * 2f * i / segments;
                hullPoints.Add(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
            }
        } else {
            var verts = mesh.vertices;
            var projected = new List<Vector2>(verts.Length);
            for (var i = 0; i < verts.Length; i++) {
                var wp = modelTransform.TransformPoint(verts[i]);
                projected.Add(WorldPosToCanvasPos(wp));
            }

            var hull = AndrewMonotoneChain(projected);
            if (hull.Count < 3) {
                var center = new Vector2(lastContainerSize.x * 0.5f, lastContainerSize.y * 0.5f);
                var radius = 80f + hullPadding;
                for (var i = 0; i < 32; i++) {
                    var angle = Mathf.PI * 2f * i / 32;
                    hullPoints.Add(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
                }
            } else {
                ExpandHull(hull, hullPadding);
            }
        }

        // Cumulative arc lengths and find bottom-most point (max Y in canvas-down coords)
        hullArcLengths.Add(0f);
        hullBottomIndex = 0;
        var maxY = hullPoints[0].y;

        for (var i = 1; i < hullPoints.Count; i++) {
            var dist = Vector2.Distance(hullPoints[i], hullPoints[i - 1]);
            hullArcLengths.Add(hullArcLengths[i - 1] + dist);
            if (hullPoints[i].y > maxY) {
                maxY = hullPoints[i].y;
                hullBottomIndex = i;
            }
        }

        hullPerimeter = hullArcLengths[hullArcLengths.Count - 1]
                      + Vector2.Distance(hullPoints[hullPoints.Count - 1], hullPoints[0]);
    }

    private static List<Vector2> AndrewMonotoneChain(List<Vector2> points) {
        var n = points.Count;
        if (n < 3) return new List<Vector2>(points);

        points.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

        var unique = new List<Vector2>(n) { points[0] };
        for (var i = 1; i < n; i++) {
            if (Vector2.SqrMagnitude(points[i] - unique[unique.Count - 1]) > 0.001f)
                unique.Add(points[i]);
        }
        n = unique.Count;
        if (n < 3) return unique;

        var hull = new List<Vector2>(n * 2);

        for (var i = 0; i < n; i++) {
            while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], unique[i]) <= 0)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(unique[i]);
        }

        var lower = hull.Count + 1;
        for (var i = n - 2; i >= 0; i--) {
            while (hull.Count >= lower && Cross(hull[hull.Count - 2], hull[hull.Count - 1], unique[i]) <= 0)
                hull.RemoveAt(hull.Count - 1);
            hull.Add(unique[i]);
        }

        hull.RemoveAt(hull.Count - 1);
        return hull;
    }

    private static float Cross(Vector2 o, Vector2 a, Vector2 b) {
        return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
    }

    private void ExpandHull(List<Vector2> hull, float offset) {
        var count = hull.Count;
        var expanded = new List<Vector2>(count);

        for (var i = 0; i < count; i++) {
            var prev = hull[(i - 1 + count) % count];
            var curr = hull[i];
            var next = hull[(i + 1) % count];

            var e1 = (curr - prev).normalized;
            var e2 = (next - curr).normalized;
            var n1 = new Vector2(-e1.y, e1.x);
            var n2 = new Vector2(-e2.y, e2.x);

            var avgNormal = (n1 + n2).normalized;
            if (avgNormal.sqrMagnitude < 0.001f) avgNormal = n1;

            var dot = Vector2.Dot(avgNormal, n1);
            var scale = dot > 0.1f ? offset / dot : offset;
            scale = Mathf.Min(scale, offset * 3f);

            expanded.Add(curr + avgNormal * scale);
        }

        hullPoints.AddRange(expanded);
    }

    // =====================================================================
    //  PATH SAMPLING: entry line + hull contour
    // =====================================================================

    private void UpdatePathMetrics() {
        entryStart = new Vector2(lastContainerSize.x * 0.5f, lastContainerSize.y + activeFontSize * 2f);
        entryEnd = hullPoints.Count > 0
            ? hullPoints[hullBottomIndex]
            : new Vector2(lastContainerSize.x * 0.5f, lastContainerSize.y * 0.5f);
        entryLength = Vector2.Distance(entryStart, entryEnd);
        pathLength = entryLength + hullPerimeter;
    }

    /// <summary>
    /// Sample position along the full path at a given distance from path start.
    /// Path = [entry line from off-screen bottom to hull bottom] + [hull contour one full loop].
    /// </summary>
    private Vector2 SamplePathPosition(float dist) {
        if (dist <= 0f) return entryStart;

        if (dist < entryLength) {
            return Vector2.Lerp(entryStart, entryEnd, dist / Mathf.Max(entryLength, 0.001f));
        }

        // On hull contour
        var hullDist = dist - entryLength;
        if (hullPerimeter <= 0f) return entryEnd;
        var t = Mathf.Repeat(hullDist / hullPerimeter, 1f);
        return SampleHullPosition(t);
    }

    /// <summary>
    /// Sample tangent along the full path at a given distance.
    /// </summary>
    private Vector2 SamplePathTangent(float dist) {
        if (dist < entryLength) {
            var dir = entryEnd - entryStart;
            return dir.sqrMagnitude > 0.001f ? dir.normalized : Vector2.up;
        }

        var hullDist = dist - entryLength;
        if (hullPerimeter <= 0f) return Vector2.up;
        var t = Mathf.Repeat(hullDist / hullPerimeter, 1f);
        return SampleHullTangent(t);
    }

    private Vector2 SampleHullPosition(float t) {
        if (hullPoints.Count == 0) return new Vector2(lastContainerSize.x * 0.5f, lastContainerSize.y);

        t = Mathf.Repeat(t, 1f);
        var targetDist = t * hullPerimeter;
        var count = hullPoints.Count;
        var walked = 0f;

        for (var step = 0; step < count; step++) {
            var i = (hullBottomIndex + step) % count;
            var j = (hullBottomIndex + step + 1) % count;
            var segLen = Vector2.Distance(hullPoints[i], hullPoints[j]);

            if (walked + segLen >= targetDist) {
                var localT = (targetDist - walked) / Mathf.Max(segLen, 0.001f);
                return Vector2.Lerp(hullPoints[i], hullPoints[j], localT);
            }
            walked += segLen;
        }

        return hullPoints[hullBottomIndex];
    }

    private Vector2 SampleHullTangent(float t) {
        if (hullPoints.Count < 2) return Vector2.right;

        t = Mathf.Repeat(t, 1f);
        var targetDist = t * hullPerimeter;
        var count = hullPoints.Count;
        var walked = 0f;

        for (var step = 0; step < count; step++) {
            var i = (hullBottomIndex + step) % count;
            var j = (hullBottomIndex + step + 1) % count;
            var seg = hullPoints[j] - hullPoints[i];
            var segLen = seg.magnitude;

            if (walked + segLen >= targetDist)
                return segLen > 0.001f ? seg / segLen : Vector2.right;

            walked += segLen;
        }

        return Vector2.right;
    }

    // =====================================================================
    //  ROPE ANIMATION
    // =====================================================================

    private void AnimateRope() {
        glyphPoolUsed = 0;
        var settleRange = settleDuration * ropeSpeed;

        for (var i = 0; i < prepared.count; i++) {
            var kind = prepared.kinds[i];

            // Skip non-renderable characters (they still occupy rope space via cumWidths)
            if (kind == SegmentBreakKind.HardBreak || kind == SegmentBreakKind.Space) continue;

            // Character's distance along the path = ropeHead - its cumulative offset
            var pathDist = ropeHeadDist - cumWidths[i];
            if (pathDist < 0f) continue; // not yet on path

            Vector2 pos;
            float rotation = 0f;
            Color color;
            float alpha;

            if (pathDist < pathLength) {
                // === ON PATH (entry line or hull contour) ===
                pos = SamplePathPosition(pathDist);
                var tangent = SamplePathTangent(pathDist);

                // Subtle tilt along path direction
                rotation = Mathf.Atan2(tangent.y, tangent.x) * Mathf.Rad2Deg;
                if (rotation > 90f) rotation -= 360f;
                if (rotation < -90f) rotation += 360f;
                rotation *= 0.35f;

                // Color: magenta during entry, cyan on hull with color cycling
                if (pathDist < entryLength) {
                    var entryT = pathDist / Mathf.Max(entryLength, 1f);
                    color = Color.Lerp(neonMagenta, neonCyan, entryT);
                } else {
                    var hullT = (pathDist - entryLength) / Mathf.Max(hullPerimeter, 1f);
                    color = Color.Lerp(neonCyan, neonMagenta, Mathf.PingPong(hullT * 2f, 1f));
                }

                // Fade in at the very start of entry
                alpha = Mathf.Clamp01(pathDist / Mathf.Max(activeFontSize * 3f, 1f));
            } else {
                // === SETTLING: peel off hull and fly to final text position ===
                var settleT = Mathf.Clamp01((pathDist - pathLength) / settleRange);
                settleT = EaseInOutCubic(settleT);

                pos = Vector2.Lerp(entryEnd, finalPositions[i], settleT);
                rotation = 0f;
                color = Color.Lerp(neonCyan, textColor, settleT);
                alpha = 1f;
            }

            var tmp = GetPooledGlyph();
            tmp.text = prepared.graphemes[i];
            tmp.fontSize = activeFontSize;
            tmp.color = new Color(color.r, color.g, color.b, alpha);
            tmp.rectTransform.anchoredPosition = new Vector2(pos.x, -pos.y);
            tmp.rectTransform.localEulerAngles = new Vector3(0f, 0f, rotation);
        }

        // Hide unused pool items
        for (var i = glyphPoolUsed; i < glyphPool.Count; i++) {
            glyphPool[i].gameObject.SetActive(false);
        }
    }

    // =====================================================================
    //  POST-ANIMATION: CONSOLIDATE TO LINES
    // =====================================================================

    private void ConsolidateToLines() {
        for (var i = 0; i < glyphPool.Count; i++) {
            glyphPool[i].gameObject.SetActive(false);
        }

        linePoolUsed = 0;
        var layoutWidth = lastContainerSize.x - gutter * 2f;
        var cursor = 0;
        var y = padding;

        while (cursor < prepared.count) {
            var line = PretextLayout.LayoutNextLine(prepared, layoutWidth, cursor);
            if (line.endIndex <= line.startIndex) { cursor = line.endIndex; continue; }

            var text = BuildLineText(line);
            if (!string.IsNullOrEmpty(text)) {
                var tmp = GetPooledLine();
                tmp.text = text;
                tmp.fontSize = activeFontSize;
                tmp.color = textColor;
                tmp.rectTransform.anchoredPosition = new Vector2(gutter, -y);
                tmp.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, line.width + activeFontSize);
                tmp.rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, activeLineHeight);
                tmp.rectTransform.localEulerAngles = Vector3.zero;
            }

            cursor = line.endIndex;
            y += activeLineHeight;
        }

        for (var i = linePoolUsed; i < linePool.Count; i++) {
            linePool[i].gameObject.SetActive(false);
        }
    }

    private string BuildLineText(PretextLine line) {
        var sb = new System.Text.StringBuilder();
        for (var i = line.startIndex; i < line.endIndex && i < prepared.count; i++) {
            sb.Append(prepared.graphemes[i]);
        }
        return sb.ToString().TrimEnd();
    }

    // =====================================================================
    //  EASING
    // =====================================================================

    private static float EaseInOutCubic(float t) {
        return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
    }

    // =====================================================================
    //  COORDINATE CONVERSION
    // =====================================================================

    private Vector3 CanvasPosToWorldPos(Vector2 canvasPos) {
        var scaleFactor = canvas.scaleFactor;
        var screenX = canvasPos.x * scaleFactor;
        var screenY = (lastContainerSize.y - canvasPos.y) * scaleFactor;
        var depth = canvas.planeDistance + modelDepthOffset;
        return uiCamera.ScreenToWorldPoint(new Vector3(screenX, screenY, depth));
    }

    private Vector2 WorldPosToCanvasPos(Vector3 worldPos) {
        var screenPos = uiCamera.WorldToScreenPoint(worldPos);
        var scaleFactor = canvas.scaleFactor;
        var canvasX = screenPos.x / scaleFactor;
        var canvasY = lastContainerSize.y - screenPos.y / scaleFactor;
        return new Vector2(canvasX, canvasY);
    }

    private Vector2 CanvasSizeToWorldSize(Vector2 canvasSize) {
        var center = CanvasPosToWorldPos(new Vector2(lastContainerSize.x * 0.5f, lastContainerSize.y * 0.5f));
        var offset = CanvasPosToWorldPos(new Vector2(
            lastContainerSize.x * 0.5f + canvasSize.x * 0.5f,
            lastContainerSize.y * 0.5f + canvasSize.y * 0.5f));
        return new Vector2(
            Mathf.Abs(offset.x - center.x) * 2f,
            Mathf.Abs(offset.y - center.y) * 2f);
    }

    // =====================================================================
    //  OBJECT POOLING
    // =====================================================================

    private TextMeshProUGUI GetPooledGlyph() {
        if (glyphPoolUsed < glyphPool.Count) {
            var existing = glyphPool[glyphPoolUsed];
            existing.gameObject.SetActive(true);
            glyphPoolUsed++;
            return existing;
        }

        var go = new GameObject($"Glyph_{glyphPoolUsed}", typeof(RectTransform));
        go.transform.SetParent(container, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, activeFontSize * 2f);
        rt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, activeLineHeight);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font = fontAsset;
        tmp.fontSize = activeFontSize;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = neonCyan;
        tmp.raycastTarget = false;

        var shadow = go.AddComponent<Shadow>();
        shadow.effectColor = new Color(0f, 0.8f, 1f, 0.4f);
        shadow.effectDistance = new Vector2(1.5f, -1.5f);

        glyphPool.Add(tmp);
        glyphPoolUsed++;
        return tmp;
    }

    private TextMeshProUGUI GetPooledLine() {
        if (linePoolUsed < linePool.Count) {
            var existing = linePool[linePoolUsed];
            existing.gameObject.SetActive(true);
            linePoolUsed++;
            return existing;
        }

        var go = new GameObject($"Line_{linePoolUsed}", typeof(RectTransform));
        go.transform.SetParent(container, false);

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(0, 1);
        rt.pivot = new Vector2(0, 1);

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font = fontAsset;
        tmp.fontSize = activeFontSize;
        tmp.enableWordWrapping = false;
        tmp.overflowMode = TextOverflowModes.Overflow;
        tmp.alignment = TextAlignmentOptions.TopLeft;
        tmp.color = textColor;
        tmp.raycastTarget = false;

        linePool.Add(tmp);
        linePoolUsed++;
        return tmp;
    }
}
