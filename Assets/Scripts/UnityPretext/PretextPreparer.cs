public static class PretextPreparer {
    public static PreparedText Prepare(string text, PretextOptions options) {
        var graphemes = GraphemeUtil.GetGraphemeClusters(text);
        var count = graphemes.Length;

        if (count == 0) {
            return new PreparedText {
                graphemes = System.Array.Empty<string>(),
                widths = System.Array.Empty<float>(),
                kinds = System.Array.Empty<SegmentBreakKind>(),
                canBreakBefore = System.Array.Empty<bool>(),
                count = 0,
            };
        }

        var widths = new float[count];
        var kinds = new SegmentBreakKind[count];
        var canBreakBefore = new bool[count];

        // Classify
        SegmentClassifier.Classify(graphemes, kinds, canBreakBefore, options.enableKinsoku, options.enableCjkBreak);

        // Measure
        var measurer = new GlyphMeasurer(options.fontAsset, options.fontSize);
        for (var i = 0; i < count; i++) {
            switch (kinds[i]) {
                case SegmentBreakKind.HardBreak:
                case SegmentBreakKind.ZeroWidthBreak:
                    widths[i] = 0;
                    break;
                case SegmentBreakKind.Tab:
                    widths[i] = measurer.GetTabAdvance(options.tabWidth > 0 ? options.tabWidth : 4);
                    break;
                case SegmentBreakKind.Space:
                    widths[i] = measurer.SpaceWidth;
                    break;
                case SegmentBreakKind.SoftHyphen:
                    widths[i] = 0; // Invisible (only shown at line break)
                    break;
                default:
                    widths[i] = measurer.MeasureGrapheme(graphemes[i]);
                    break;
            }
        }

        return new PreparedText {
            graphemes = graphemes,
            widths = widths,
            kinds = kinds,
            canBreakBefore = canBreakBefore,
            count = count,
        };
    }
}
