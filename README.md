# UnityPretext

Unity port of [Pretext](https://github.com/chenglou/pretext) — a text measurement & layout engine that works without triggering the rendering engine's internal layout passes. Uses **TextMesh Pro** for font metrics instead of the original's Canvas `measureText()`.

UnityPretext implements the same two-phase architecture as the original: an expensive one-time `Prepare()` pass (grapheme clustering, segment classification, glyph measurement) followed by a cheap `Layout()` pass (pure arithmetic over cached widths). This makes it suitable for real-time text reflow — the included sample flows text around bouncing obstacles every frame.

## Installation

**Unity 6000.0+** required. TextMesh Pro must be installed (included in Unity by default).

Open **Window → Package Manager → + → Add package from git URL** and enter:

```
https://github.com/mjshin82/UnityPretext.git
```

To pin a specific version, append a tag:

```
https://github.com/mjshin82/UnityPretext.git#v0.1.0
```

## API

UnityPretext serves two use cases:

### 1. Measure a paragraph's height without layout passes

```csharp
using TMPro;

var options = PretextOptions.Default(fontAsset, fontSize);
var prepared = PretextPreparer.Prepare("Hello world. 春天到了。🚀", options);

int lineCount = PretextLayout.CountLines(prepared, maxWidth);
float height = PretextLayout.CalcHeight(prepared, maxWidth, lineHeight);
```

`Prepare()` does the one-time work: split text into grapheme clusters, classify each segment's break behavior, and measure glyph widths via TMP's font atlas. The result is a `PreparedText` handle. `CountLines()` / `CalcHeight()` are pure arithmetic over cached widths — no rendering queries.

### 2. Lay out lines manually

#### Get all lines at a fixed width

```csharp
var result = PretextLayout.Layout(prepared, maxWidth);
// or with a line limit:
var result = PretextLayout.LayoutWithLines(prepared, maxWidth, maxLines: 20);

foreach (var line in result.lines) {
    // line.startIndex, line.endIndex, line.width
    var text = BuildText(prepared, line.startIndex, line.endIndex);
}
```

#### Route text one line at a time with variable widths

```csharp
int cursor = 0;
float y = 0f;

while (cursor < prepared.count) {
    // Width can change per line — flow text around obstacles
    float lineWidth = GetAvailableWidth(y);
    var line = PretextLayout.LayoutNextLine(prepared, lineWidth, cursor);
    PlaceText(line, x, y);
    cursor = line.endIndex;
    y += lineHeight;
}
```

This is how the included `EditorialEngine` sample works — it calls `LayoutNextLine()` with different widths per line to flow text around moving circles in real time.

### API Reference

**Preparation:**
```csharp
// One-time text analysis + measurement. Returns an opaque handle for layout.
PreparedText PretextPreparer.Prepare(string text, PretextOptions options)

// Options with defaults (kinsoku and CJK breaking enabled by default)
PretextOptions.Default(TMP_FontAsset fontAsset, float fontSize)
```

**Layout:**
```csharp
// Full layout at a fixed width
PretextLayoutResult PretextLayout.Layout(PreparedText prepared, float maxWidth)

// Full layout with a line count limit
PretextLayoutResult PretextLayout.LayoutWithLines(PreparedText prepared, float maxWidth, int maxLines)

// Single-line iterator for variable-width layout
PretextLine PretextLayout.LayoutNextLine(PreparedText prepared, float maxWidth, int startIndex)

// Utilities
int PretextLayout.CountLines(PreparedText prepared, float maxWidth)
float PretextLayout.CalcHeight(PreparedText prepared, float maxWidth, float lineHeight)
```

**Types:**
```csharp
struct PreparedText {
    string[] graphemes;      // Grapheme clusters
    float[] widths;          // Pre-measured width of each grapheme
    SegmentBreakKind[] kinds; // Break behavior per segment
    bool[] canBreakBefore;   // Whether a line break is allowed before each segment
    int count;
}

struct PretextLine {
    int startIndex;   // Start index in prepared arrays (inclusive)
    int endIndex;     // End index (exclusive)
    float width;      // Actual rendered width of this line
}

enum SegmentBreakKind {
    Text,            // Regular text
    Space,           // ASCII space
    Tab,             // Tab character
    Glue,            // Non-breaking space (U+00A0)
    ZeroWidthBreak,  // Zero-width space (U+200B) — break opportunity
    SoftHyphen,      // U+00AD — invisible unless used as a break point
    HardBreak,       // \n, \r\n
}
```

## Differences from the Original Pretext

| Area | Original (TypeScript) | UnityPretext (C#) |
|------|----------------------|-------------------|
| **Measurement** | Canvas `measureText()` + emoji size correction | TMP `characterLookupTable` direct lookup |
| **Segmentation** | `Intl.Segmenter` (browser API) for word-level segments | Custom `GraphemeUtil` — grapheme-level from the start |
| **Segment granularity** | Word-level segments, grapheme fallback for overflow | Grapheme-level throughout |
| **Bidi** | `bidi.ts` provides segment-level direction metadata | Not ported — TMP has limited RTL support |
| **White-space modes** | `normal` + `pre-wrap` | `normal` only |
| **Fast path** | Optimized simple path for text with only spaces | Single code path |
| **Punctuation merging** | `"better."` merged into one segment | No merging — each grapheme is independent |
| **URL merging** | URL-like runs kept as single segments | Not implemented |
| **Measurement cache** | Per-font, per-segment cache across texts | No cache — measures fresh each `Prepare()` call |
| **Soft hyphen rendering** | Trailing `-` exposed in line text at break | Width set to 0, no visible hyphen at break |
| **Tab stops** | Position-based: `position % (8 * spaceWidth)` | Fixed interval: `tabWidth * spaceWidth` |
| **Browser quirks** | Safari/Chrome/Firefox epsilon, emoji inflation | Not needed — TMP provides consistent metrics |

## Multilingual Support

The original Pretext relies on the browser's `Intl.Segmenter` for word/grapheme segmentation — a mature ICU-backed API that handles complex scripts automatically. Unity has no equivalent, so UnityPretext implements its own grapheme clustering and line-break rules. The coverage is practical but not exhaustive.

### Grapheme Clustering

`GraphemeUtil` implements grapheme cluster boundary detection based on Unicode General Category properties:

**Supported:**
- CR+LF pairs treated as a single cluster
- Combining marks (Category M) — Latin diacritics, Devanagari, Arabic, Hebrew, Myanmar, Korean Jamo
- Surrogate pairs (astral plane characters)
- Emoji — single code point emoji, variation selectors (VS1–VS256), skin tone modifiers, ZWJ sequences, enclosing keycap
- Regional indicator pairs (flag emoji)
- CJK characters — Han, Hiragana, Katakana, Hangul, small kana

**Not supported:**
- Thai grapheme clustering — Thai combining marks are partially handled, but word segmentation requires dictionary-based analysis (`Intl.Segmenter` handles this; we do not)
- Indic conjuncts beyond basic combining marks
- Extended Grapheme Cluster rules from UAX #29 in full

### Line Breaking Rules (Kinsoku)

CJK line-break prohibition rules are implemented for Japanese and Chinese. These prevent aesthetically incorrect breaks around punctuation.

**Japanese:**
- Line-start prohibited (~40 chars): `、。！？…‥〕）】〉》」』～` + small kana (`ぁぃぅぇぉっゃゅょゎァィゥェォッャュョヮヵヶ`)
- Line-end prohibited (~10 chars): `〔（【〈《「『`
- Pushback: if a break would place a prohibited character at line start, the break point moves back up to 5 characters

**Chinese:**
- Line-start prohibited: `！？。，、：；）】」』…`
- Line-end prohibited: `（【「『`

**CJK per-character breaking:**
When enabled (default), any CJK character boundary is a valid break point — matching how Chinese and Japanese text naturally wraps without spaces.

**Thai:**
Not implemented. Thai requires dictionary-based word segmentation for correct line breaking, which is beyond the scope of this port.

**Bidi / RTL:**
Not implemented. Arabic, Hebrew, and other right-to-left scripts are not supported for correct display ordering. TMP itself has limited RTL support.

## Sample

The package includes an **Editorial Engine** sample — a two-column editorial layout where text reflows in real time around bouncing circles. It demonstrates `LayoutNextLine()` with per-line variable widths for obstacle avoidance.

To import: **Package Manager → UnityPretext → Samples → Import**.

**Live demo:** [Play in browser](https://play.unity.com/en/games/42c8e3f0-e136-48df-894b-b901d4cf5aad/web)

## Credits

Ported from [@chenglou/pretext](https://github.com/chenglou/pretext). Original architecture by Cheng Lou, with the initial seed from Sebastian Markbage's [text-layout](https://github.com/chenglou/text-layout).
