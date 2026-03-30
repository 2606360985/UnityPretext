using System.Collections.Generic;

public static class GraphemeUtil {
    public static string[] GetGraphemeClusters(string text) {
        if (string.IsNullOrEmpty(text)) {
            return System.Array.Empty<string>();
        }

        var list = new List<string>();
        var len = text.Length;
        var start = 0;

        while (start < len) {
            var end = GetGraphemeClusterEnd(text, start);
            list.Add(text.Substring(start, end - start));
            start = end;
        }

        return list.ToArray();
    }

    private static int GetGraphemeClusterEnd(string text, int index) {
        var len = text.Length;
        if (index >= len) return len;

        var ch = text[index];

        // Handle CR+LF pair
        if (ch == '\r') {
            if (index + 1 < len && text[index + 1] == '\n') {
                return index + 2;
            }
            return index + 1;
        }

        // Control characters are standalone
        if (ch == '\n' || ch == '\t') {
            return index + 1;
        }

        // Surrogate pair → treat as a single code point
        int cp;
        int next;
        if (char.IsHighSurrogate(ch) && index + 1 < len && char.IsLowSurrogate(text[index + 1])) {
            cp = char.ConvertToUtf32(ch, text[index + 1]);
            next = index + 2;
        } else {
            cp = ch;
            next = index + 1;
        }

        // Regional Indicator (flag emoji): pair two together
        if (IsRegionalIndicator(cp)) {
            if (next < len) {
                int cp2;
                int next2;
                if (char.IsHighSurrogate(text[next]) && next + 1 < len && char.IsLowSurrogate(text[next + 1])) {
                    cp2 = char.ConvertToUtf32(text[next], text[next + 1]);
                    next2 = next + 2;
                } else {
                    cp2 = text[next];
                    next2 = next + 1;
                }

                if (IsRegionalIndicator(cp2)) {
                    next = next2;
                }
            }
            // VS16 etc. may follow the flag, so keep consuming
            return ConsumeExtenders(text, next);
        }

        // Base emoji (single code point) + emoji sequence
        if (IsEmojiBase(cp)) {
            return ConsumeEmojiSequence(text, next);
        }

        // Normal character + consume combining marks
        return ConsumeExtenders(text, next);
    }

    /// <summary>
    /// Consumes combining marks, variation selectors, enclosing keycap, etc.
    /// ZWJ sequences are also handled here.
    /// </summary>
    private static int ConsumeExtenders(string text, int index) {
        var len = text.Length;

        while (index < len) {
            int cp;
            int size;
            if (char.IsHighSurrogate(text[index]) && index + 1 < len && char.IsLowSurrogate(text[index + 1])) {
                cp = char.ConvertToUtf32(text[index], text[index + 1]);
                size = 2;
            } else {
                cp = text[index];
                size = 1;
            }

            // Combining Mark (M category)
            if (IsCombiningMark(cp)) {
                index += size;
                continue;
            }

            // Variation Selector (VS1-VS16, VS17-VS256)
            if (IsVariationSelector(cp)) {
                index += size;
                continue;
            }

            // Enclosing Keycap (⃣ U+20E3)
            if (cp == 0x20E3) {
                index += size;
                continue;
            }

            // ZWJ → include the next character in this cluster
            if (cp == 0x200D) {
                index += size;
                // Consume one code point after ZWJ
                if (index < len) {
                    if (char.IsHighSurrogate(text[index]) && index + 1 < len && char.IsLowSurrogate(text[index + 1])) {
                        index += 2;
                    } else {
                        index += 1;
                    }
                }
                continue;
            }

            break;
        }

        return index;
    }

    /// <summary>
    /// Consumes emoji sequences: VS16, Keycap, ZWJ chain, Skin Tone Modifier, etc.
    /// </summary>
    private static int ConsumeEmojiSequence(string text, int index) {
        var len = text.Length;

        while (index < len) {
            int cp;
            int size;
            if (char.IsHighSurrogate(text[index]) && index + 1 < len && char.IsLowSurrogate(text[index + 1])) {
                cp = char.ConvertToUtf32(text[index], text[index + 1]);
                size = 2;
            } else {
                cp = text[index];
                size = 1;
            }

            // Variation Selector
            if (IsVariationSelector(cp)) {
                index += size;
                continue;
            }

            // Combining Mark
            if (IsCombiningMark(cp)) {
                index += size;
                continue;
            }

            // Enclosing Keycap
            if (cp == 0x20E3) {
                index += size;
                continue;
            }

            // Skin Tone Modifier (U+1F3FB-1F3FF)
            if (cp >= 0x1F3FB && cp <= 0x1F3FF) {
                index += size;
                continue;
            }

            // ZWJ → include the next emoji/character in this cluster
            if (cp == 0x200D) {
                index += size;
                // Consume one code point after ZWJ
                if (index < len) {
                    if (char.IsHighSurrogate(text[index]) && index + 1 < len && char.IsLowSurrogate(text[index + 1])) {
                        index += 2;
                    } else {
                        index += 1;
                    }
                }
                continue;
            }

            break;
        }

        return index;
    }

    // ─── Unicode Category Checks ───

    /// <summary>
    /// Combining Mark: General Category M (Mn, Mc, Me)
    /// </summary>
    private static bool IsCombiningMark(int cp) {
        // Combining Diacritical Marks
        if (cp >= 0x0300 && cp <= 0x036F) return true;
        // Combining Diacritical Marks Extended
        if (cp >= 0x1AB0 && cp <= 0x1AFF) return true;
        // Combining Diacritical Marks Supplement
        if (cp >= 0x1DC0 && cp <= 0x1DFF) return true;
        // Combining Half Marks
        if (cp >= 0xFE20 && cp <= 0xFE2F) return true;
        // Combining Marks for Symbols
        if (cp >= 0x20D0 && cp <= 0x20FF) return true;

        // Thai combining marks
        if (cp >= 0x0E31 && cp <= 0x0E3A) return true;
        if (cp >= 0x0E47 && cp <= 0x0E4E) return true;

        // Korean Hangul Jungseong (vowels) + Jongseong (final consonants)
        // (Hangul Jamo combining: handled by StringInfo normally, but for safety)
        if (cp >= 0x1160 && cp <= 0x11FF) return true;

        // CJK: Ideographic Description Characters are not combining

        // Japanese small kana are independent characters, not combining, so not included here

        // Myanmar combining marks
        if (cp >= 0x102B && cp <= 0x103E) return true;
        if (cp >= 0x1056 && cp <= 0x1059) return true;

        // Devanagari/Hindi combining marks
        if (cp >= 0x0901 && cp <= 0x0903) return true;
        if (cp >= 0x093A && cp <= 0x094F) return true;
        if (cp >= 0x0962 && cp <= 0x0963) return true;

        // Arabic combining marks
        if (cp >= 0x064B && cp <= 0x065F) return true;
        if (cp >= 0x0610 && cp <= 0x061A) return true;
        if (cp == 0x0670) return true;

        // Hebrew combining marks
        if (cp >= 0x0591 && cp <= 0x05BD) return true;
        if (cp == 0x05BF) return true;
        if (cp >= 0x05C1 && cp <= 0x05C2) return true;
        if (cp >= 0x05C4 && cp <= 0x05C5) return true;
        if (cp == 0x05C7) return true;

        return false;
    }

    /// <summary>
    /// Variation Selector: VS1-VS16 (U+FE00-FE0F), VS17-VS256 (U+E0100-E01EF)
    /// </summary>
    private static bool IsVariationSelector(int cp) {
        if (cp >= 0xFE00 && cp <= 0xFE0F) return true;
        if (cp >= 0xE0100 && cp <= 0xE01EF) return true;
        return false;
    }

    /// <summary>
    /// Regional Indicator Symbol (U+1F1E6-1F1FF) — for flag emoji
    /// </summary>
    private static bool IsRegionalIndicator(int cp) {
        return cp >= 0x1F1E6 && cp <= 0x1F1FF;
    }

    /// <summary>
    /// Determines if the code point is an emoji base (single code point emoji + emoji sequence start)
    /// </summary>
    private static bool IsEmojiBase(int cp) {
        // Emoticons
        if (cp >= 0x1F600 && cp <= 0x1F64F) return true;
        // Miscellaneous Symbols and Pictographs
        if (cp >= 0x1F300 && cp <= 0x1F5FF) return true;
        // Transport and Map Symbols
        if (cp >= 0x1F680 && cp <= 0x1F6FF) return true;
        // Supplemental Symbols and Pictographs
        if (cp >= 0x1F900 && cp <= 0x1F9FF) return true;
        // Symbols and Pictographs Extended-A
        if (cp >= 0x1FA00 && cp <= 0x1FA6F) return true;
        // Symbols and Pictographs Extended-B
        if (cp >= 0x1FA70 && cp <= 0x1FAFF) return true;

        // Dingbats (some emoji)
        if (cp >= 0x2702 && cp <= 0x27B0) return true;
        // Miscellaneous Symbols
        if (cp >= 0x2600 && cp <= 0x26FF) return true;

        // Keycap base: 0-9, #, *
        if (cp >= 0x0030 && cp <= 0x0039) return false; // Digits alone are not emoji (only VS16+Keycap combos)
        if (cp == 0x0023 || cp == 0x002A) return false;  // Same for # and * alone

        // Playing Cards, Mahjong
        if (cp >= 0x1F000 && cp <= 0x1F02F) return true;

        // Chess symbols
        // Chess symbols (0x2654-0x2660) — practically ignored

        return false;
    }
}
