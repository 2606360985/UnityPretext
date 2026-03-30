using System.Collections.Generic;

public static class PretextLayout {
    public static PretextLayoutResult Layout(PreparedText prepared, float maxWidth) {
        return LayoutWithLines(prepared, maxWidth, int.MaxValue);
    }

    public static PretextLayoutResult LayoutWithLines(PreparedText prepared, float maxWidth, int maxLines) {
        if (prepared.count == 0) {
            return new PretextLayoutResult {
                lines = System.Array.Empty<PretextLine>(),
                lineCount = 0,
            };
        }

        var lines = new List<PretextLine>();
        var startIndex = 0;

        while (startIndex < prepared.count && lines.Count < maxLines) {
            var line = LayoutNextLine(prepared, maxWidth, startIndex);
            lines.Add(line);
            startIndex = line.endIndex;
        }

        return new PretextLayoutResult {
            lines = lines.ToArray(),
            lineCount = lines.Count,
        };
    }

    public static PretextLine LayoutNextLine(PreparedText prepared, float maxWidth, int startIndex) {
        // Consume leading spaces/line breaks
        startIndex = ConsumeLeadingSpaces(prepared, startIndex);

        if (startIndex >= prepared.count) {
            return new PretextLine {
                startIndex = startIndex,
                endIndex = prepared.count,
                width = 0,
            };
        }

        // Handle hard break immediately
        if (prepared.kinds[startIndex] == SegmentBreakKind.HardBreak) {
            return new PretextLine {
                startIndex = startIndex,
                endIndex = startIndex + 1,
                width = 0,
            };
        }

        var lineWidth = 0f;
        var lastBreakIndex = -1;
        var lastBreakWidth = 0f;
        var i = startIndex;

        while (i < prepared.count) {
            var kind = prepared.kinds[i];

            // Hard break → end line here
            if (kind == SegmentBreakKind.HardBreak) {
                return new PretextLine {
                    startIndex = startIndex,
                    endIndex = i + 1, // consume hard break
                    width = lineWidth,
                };
            }

            var segWidth = prepared.widths[i];

            // Record possible break point
            if (prepared.canBreakBefore[i] && i > startIndex) {
                lastBreakIndex = i;
                lastBreakWidth = lineWidth;
            }

            // Overflow check
            if (lineWidth + segWidth > maxWidth && i > startIndex) {
                // Break at the last possible break point if available
                if (lastBreakIndex > startIndex) {
                    // Kinsoku pushback: move back if break point is a line-start prohibited char
                    var breakAt = PushBackKinsoku(prepared, lastBreakIndex, startIndex);
                    var breakWidth = RecalcWidth(prepared, startIndex, breakAt);

                    return new PretextLine {
                        startIndex = startIndex,
                        endIndex = breakAt,
                        width = breakWidth,
                    };
                }

                // No break point available — force break (long word)
                return new PretextLine {
                    startIndex = startIndex,
                    endIndex = i,
                    width = lineWidth,
                };
            }

            lineWidth += segWidth;
            i++;
        }

        // End of text
        return new PretextLine {
            startIndex = startIndex,
            endIndex = prepared.count,
            width = TrimTrailingSpaceWidth(prepared, startIndex, prepared.count, lineWidth),
        };
    }

    public static int CountLines(PreparedText prepared, float maxWidth) {
        if (prepared.count == 0) return 0;

        var count = 0;
        var startIndex = 0;

        while (startIndex < prepared.count) {
            var line = LayoutNextLine(prepared, maxWidth, startIndex);
            count++;
            startIndex = line.endIndex;
        }

        return count;
    }

    public static float CalcHeight(PreparedText prepared, float maxWidth, float lineHeight) {
        return CountLines(prepared, maxWidth) * lineHeight;
    }

    private static int ConsumeLeadingSpaces(PreparedText prepared, int index) {
        while (index < prepared.count) {
            var kind = prepared.kinds[index];
            if (kind != SegmentBreakKind.Space && kind != SegmentBreakKind.Tab) {
                break;
            }
            index++;
        }
        return index;
    }

    private static int PushBackKinsoku(PreparedText prepared, int breakIndex, int minIndex) {
        var idx = breakIndex;
        var maxPushBack = 5;

        while (idx > minIndex + 1 && maxPushBack > 0) {
            var cp = GetFirstCodePoint(prepared.graphemes[idx]);
            if (!IsLineStartProhibited(cp)) {
                break;
            }
            idx--;
            maxPushBack--;
        }

        // If pushback couldn't resolve it, return original position
        if (idx <= minIndex) {
            return breakIndex;
        }

        return idx;
    }

    private static float RecalcWidth(PreparedText prepared, int start, int end) {
        var w = 0f;
        for (var i = start; i < end; i++) {
            w += prepared.widths[i];
        }
        return TrimTrailingSpaceWidth(prepared, start, end, w);
    }

    private static float TrimTrailingSpaceWidth(PreparedText prepared, int start, int end, float totalWidth) {
        var w = totalWidth;
        for (var i = end - 1; i >= start; i--) {
            var kind = prepared.kinds[i];
            if (kind == SegmentBreakKind.Space || kind == SegmentBreakKind.Tab) {
                w -= prepared.widths[i];
            } else {
                break;
            }
        }
        return w;
    }

    private static int GetFirstCodePoint(string grapheme) {
        if (string.IsNullOrEmpty(grapheme)) return 0;
        if (char.IsHighSurrogate(grapheme[0]) && grapheme.Length > 1) {
            return char.ConvertToUtf32(grapheme[0], grapheme[1]);
        }
        return grapheme[0];
    }

    private static bool IsLineStartProhibited(int cp) {
        // Same line-start prohibited character check as SegmentClassifier
        if (cp > 0xFFFF) return false;
        var ch = (char)cp;
        return ch is '、' or '。' or '！' or '？' or '…' or '‥' or '〕' or '）' or '】'
            or '〉' or '》' or '」' or '』' or '～'
            or 'ぁ' or 'ぃ' or 'ぅ' or 'ぇ' or 'ぉ' or 'っ' or 'ゃ' or 'ゅ' or 'ょ' or 'ゎ'
            or 'ァ' or 'ィ' or 'ゥ' or 'ェ' or 'ォ' or 'ッ' or 'ャ' or 'ュ' or 'ョ' or 'ヮ' or 'ヵ' or 'ヶ'
            or ')' or ']' or '}' or '.' or ',' or ':' or ';'
            or '，' or '：' or '；';
    }
}
