using TMPro;

public class GlyphMeasurer {
    private readonly TMP_FontAsset fontAsset;
    private readonly float scale;

    public float SpaceWidth { get; }

    public GlyphMeasurer(TMP_FontAsset fontAsset, float fontSize) {
        this.fontAsset = fontAsset;
        this.scale = fontSize / fontAsset.faceInfo.pointSize * fontAsset.faceInfo.scale;
        SpaceWidth = MeasureCodePoint(' ');
    }

    public float MeasureGrapheme(string grapheme) {
        if (string.IsNullOrEmpty(grapheme)) return 0;

        var total = 0f;
        for (var i = 0; i < grapheme.Length; i++) {
            int cp;
            if (char.IsHighSurrogate(grapheme[i]) && i + 1 < grapheme.Length) {
                cp = char.ConvertToUtf32(grapheme[i], grapheme[i + 1]);
                i++;
            } else {
                cp = grapheme[i];
            }

            // ZWJ, variation selectors, and other control characters have zero width
            if (IsZeroWidthCodePoint(cp)) {
                continue;
            }

            total += MeasureCodePoint(cp);
        }

        return total;
    }

    public float GetTabAdvance(float tabInterval) {
        return tabInterval * SpaceWidth;
    }

    private float MeasureCodePoint(int cp) {
        var unicode = (uint)cp;

        if (TryGetAdvance(fontAsset, unicode, out var advance)) {
            return advance * scale;
        }

        // Search fallback fonts
        if (fontAsset.fallbackFontAssetTable != null) {
            foreach (var fallback in fontAsset.fallbackFontAssetTable) {
                if (fallback != null && TryGetAdvance(fallback, unicode, out advance)) {
                    return advance * scale;
                }
            }
        }

        // If not found, return space width as fallback
        return SpaceWidth;
    }

    private static bool TryGetAdvance(TMP_FontAsset font, uint unicode, out float advance) {
        if (font.characterLookupTable != null &&
            font.characterLookupTable.TryGetValue(unicode, out var character)) {
            advance = character.glyph.metrics.horizontalAdvance;
            return true;
        }

        advance = 0;
        return false;
    }

    private static bool IsZeroWidthCodePoint(int cp) {
        // ZWJ
        if (cp == 0x200D) return true;
        // Variation Selectors
        if (cp >= 0xFE00 && cp <= 0xFE0F) return true;
        // Combining marks (general)
        if (cp >= 0x0300 && cp <= 0x036F) return true;
        return false;
    }
}
