public enum SegmentBreakKind {
    Text,
    Space,
    Tab,
    Glue,
    ZeroWidthBreak,
    SoftHyphen,
    HardBreak,
}

public struct PretextOptions {
    public float maxWidth;
    public float fontSize;
    public TMPro.TMP_FontAsset fontAsset;
    public float tabWidth;
    public bool enableKinsoku;
    public bool enableCjkBreak;

    public static PretextOptions Default(TMPro.TMP_FontAsset fontAsset, float fontSize) {
        return new PretextOptions {
            fontAsset = fontAsset,
            fontSize = fontSize,
            tabWidth = 4,
            enableKinsoku = true,
            enableCjkBreak = true,
        };
    }
}

public struct PreparedText {
    public string[] graphemes;
    public float[] widths;
    public SegmentBreakKind[] kinds;
    public bool[] canBreakBefore;
    public int count;
}

public struct PretextLine {
    public int startIndex;
    public int endIndex;
    public float width;
}

public struct PretextLayoutResult {
    public PretextLine[] lines;
    public int lineCount;
}
