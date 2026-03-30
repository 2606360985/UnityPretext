using System.Collections.Generic;

public static class SegmentClassifier {
    // Japanese line-start prohibited characters (cannot start a line)
    private static readonly HashSet<char> JpLineStartProhibited = new() {
        '、', '。', '！', '？', '…', '‥', '〕', '）', '】', '〉', '》', '」', '』', '～',
        'ぁ', 'ぃ', 'ぅ', 'ぇ', 'ぉ', 'っ', 'ゃ', 'ゅ', 'ょ', 'ゎ',
        'ァ', 'ィ', 'ゥ', 'ェ', 'ォ', 'ッ', 'ャ', 'ュ', 'ョ', 'ヮ', 'ヵ', 'ヶ',
        ')', ']', '}', '.', ',', '!', '?', ':', ';',
    };

    // Japanese line-end prohibited characters (cannot end a line)
    private static readonly HashSet<char> JpLineEndProhibited = new() {
        '〔', '（', '【', '〈', '《', '「', '『',
        '(', '[', '{',
    };

    // Chinese line-start prohibited characters
    private static readonly HashSet<char> CnLineStartProhibited = new() {
        '！', '？', '。', '，', '、', '：', '；', '）', '】', '」', '』', '…',
        ')', ']', '}', '.', ',', '!', '?', ':', ';',
    };

    // Chinese line-end prohibited characters
    private static readonly HashSet<char> CnLineEndProhibited = new() {
        '（', '【', '「', '『',
        '(', '[', '{',
    };

    public static void Classify(
        string[] graphemes,
        SegmentBreakKind[] outKinds,
        bool[] outCanBreakBefore,
        bool enableKinsoku,
        bool enableCjkBreak
    ) {
        var count = graphemes.Length;

        for (var i = 0; i < count; i++) {
            var g = graphemes[i];
            var ch = g[0];

            // Classify segment kind
            if (ch == '\n' || (ch == '\r' && g.Length > 1 && g[1] == '\n')) {
                outKinds[i] = SegmentBreakKind.HardBreak;
            } else if (ch == '\r') {
                outKinds[i] = SegmentBreakKind.HardBreak;
            } else if (ch == '\t') {
                outKinds[i] = SegmentBreakKind.Tab;
            } else if (ch == '\u00A0') { // NBSP
                outKinds[i] = SegmentBreakKind.Glue;
            } else if (ch == '\u200B') { // ZWSP
                outKinds[i] = SegmentBreakKind.ZeroWidthBreak;
            } else if (ch == '\u00AD') { // Soft hyphen
                outKinds[i] = SegmentBreakKind.SoftHyphen;
            } else if (ch == ' ') {
                outKinds[i] = SegmentBreakKind.Space;
            } else {
                outKinds[i] = SegmentBreakKind.Text;
            }

            // Determine if line break is allowed
            outCanBreakBefore[i] = false;

            if (i == 0) {
                continue;
            }

            var kind = outKinds[i];
            var prevKind = outKinds[i - 1];

            // Line break is allowed before spaces (spaces attach to the end of the previous line)
            if (kind == SegmentBreakKind.Space || kind == SegmentBreakKind.Tab) {
                continue; // Don't break before the space itself
            }

            // Can break here if previous segment is space/tab
            if (prevKind == SegmentBreakKind.Space || prevKind == SegmentBreakKind.Tab) {
                outCanBreakBefore[i] = true;
            }

            // Can break after ZWSP
            if (prevKind == SegmentBreakKind.ZeroWidthBreak) {
                outCanBreakBefore[i] = true;
            }

            // Can break after soft hyphen
            if (prevKind == SegmentBreakKind.SoftHyphen) {
                outCanBreakBefore[i] = true;
            }

            // Hard break
            if (kind == SegmentBreakKind.HardBreak) {
                outCanBreakBefore[i] = true;
                continue;
            }

            // CJK per-character line breaking
            if (enableCjkBreak && kind == SegmentBreakKind.Text) {
                var cp = GetFirstCodePoint(g);
                var prevCp = GetFirstCodePoint(graphemes[i - 1]);

                if (IsCjk(cp) || IsCjk(prevCp)) {
                    outCanBreakBefore[i] = true;
                }
            }

            // Kinsoku (line break prohibition) handling
            if (enableKinsoku && outCanBreakBefore[i]) {
                var cp = GetFirstCodePoint(g);
                var prevCp = GetFirstCodePoint(graphemes[i - 1]);

                // Don't break here if current char is line-start prohibited
                if (IsLineStartProhibited(cp)) {
                    outCanBreakBefore[i] = false;
                }

                // Don't break here if previous char is line-end prohibited
                if (IsLineEndProhibited(prevCp)) {
                    outCanBreakBefore[i] = false;
                }
            }
        }
    }

    private static int GetFirstCodePoint(string grapheme) {
        if (string.IsNullOrEmpty(grapheme)) return 0;
        if (char.IsHighSurrogate(grapheme[0]) && grapheme.Length > 1) {
            return char.ConvertToUtf32(grapheme[0], grapheme[1]);
        }
        return grapheme[0];
    }

    public static bool IsCjk(int cp) {
        // CJK Unified Ideographs
        if (cp >= 0x4E00 && cp <= 0x9FFF) return true;
        // CJK Extension A
        if (cp >= 0x3400 && cp <= 0x4DBF) return true;
        // CJK Extension B
        if (cp >= 0x20000 && cp <= 0x2A6DF) return true;
        // CJK Compatibility Ideographs
        if (cp >= 0xF900 && cp <= 0xFAFF) return true;
        // Hiragana
        if (cp >= 0x3040 && cp <= 0x309F) return true;
        // Katakana
        if (cp >= 0x30A0 && cp <= 0x30FF) return true;
        // Katakana Phonetic Extensions
        if (cp >= 0x31F0 && cp <= 0x31FF) return true;
        // Hangul Syllables
        if (cp >= 0xAC00 && cp <= 0xD7AF) return true;
        // Hangul Jamo
        if (cp >= 0x1100 && cp <= 0x11FF) return true;
        // Hangul Compatibility Jamo
        if (cp >= 0x3130 && cp <= 0x318F) return true;
        // CJK Symbols and Punctuation
        if (cp >= 0x3000 && cp <= 0x303F) return true;
        // Fullwidth Latin / Halfwidth Katakana
        if (cp >= 0xFF00 && cp <= 0xFFEF) return true;
        return false;
    }

    private static bool IsLineStartProhibited(int cp) {
        if (cp <= 0xFFFF) {
            return JpLineStartProhibited.Contains((char)cp)
                || CnLineStartProhibited.Contains((char)cp);
        }
        return false;
    }

    private static bool IsLineEndProhibited(int cp) {
        if (cp <= 0xFFFF) {
            return JpLineEndProhibited.Contains((char)cp)
                || CnLineEndProhibited.Contains((char)cp);
        }
        return false;
    }
}
