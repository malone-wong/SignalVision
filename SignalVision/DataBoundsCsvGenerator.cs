using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SignalVision;

/// <summary>
/// Extracts graph curves from an already-cropped databounds image and writes
/// the curve values in SignalVision's CSV format.
/// </summary>
/// <remarks>
/// The graphs are stacked waveform traces: a fixed number of evenly spaced
/// polylines, each spanning the full width of the plot, drawn in magenta
/// (history) or pale teal (live/baseline), and partly hidden behind cyan "+"
/// markers. Extraction therefore runs in four stages:
///
/// 1. Classify every pixel by hue ordering rather than distance to a reference
///    color. JPEG compression bleeds a lot of green into the thin magenta lines,
///    so the bright core of a curve can be far from its nominal RGB value while
///    still being unmistakably "the channel that is lowest is green".
/// 2. Reduce each column to vertical runs. Marker pixels are treated as
///    passable but value-less, which lets a trace be followed straight through
///    an occluding glyph instead of being cut into fragments.
/// 3. Follow every run in both directions. Adjacency decides which runs a walk
///    may step onto and the local slope only chooses between them, so a trace
///    is never lost because a slope estimate overshot a sharp turn.
/// 4. Keep the smallest set of walks that explains the detected ink. That
///    determines the number of curves from the image itself instead of relying
///    on a threshold, and it discards both duplicates and walks that drifted
///    from one trace onto another.
/// </remarks>
public static class DataBoundsCsvGenerator
{
    // --- Pixel classification -------------------------------------------------

    /// <summary>Darkest pixel that can still belong to a magenta trace.</summary>
    private const int MinimumCurveBrightness = 45;

    /// <summary>How dominant the magenta hue must be, relative to the brightest channel.</summary>
    private const double MinimumCurveHue = 0.16;

    /// <summary>The pale teal traces are drawn bright; dimmer teal is label text.</summary>
    private const int MinimumBaselineBrightness = 110;

    /// <summary>Keeps neutral gray gridline remnants out of the teal class.</summary>
    private const int MinimumBaselineChroma = 22;

    /// <summary>How dominant the teal hue must be, relative to the brightest channel.</summary>
    private const double MinimumBaselineHue = 0.10;

    /// <summary>
    /// Pale teal curves keep a strong red component (#a6ece1); the cyan marker
    /// glyphs (#2af2e8) do not. This is the red fraction that separates them.
    /// </summary>
    private const double MarkerRedFraction = 0.42;

    // --- Trace following ------------------------------------------------------

    /// <summary>Vertical slack allowed when deciding whether two runs touch.</summary>
    private const int LinkSlack = 2;

    /// <summary>Widest single jump a walk may make over columns holding no run.</summary>
    private const int MaximumGapColumns = 14;

    /// <summary>
    /// Longest stretch a walk may hold without any ink. Measured occlusions
    /// behind marker glyphs reach 21 columns, so anything sustained well past
    /// that is following label text rather than a curve.
    /// </summary>
    private const int MaximumDarkColumns = 22;

    /// <summary>Steepest predicted movement, in pixels per column.</summary>
    private const double MaximumSlope = 6.0;

    /// <summary>Columns used to estimate the local slope.</summary>
    private const int SlopeHistory = 4;

    // --- Curve selection ------------------------------------------------------

    /// <summary>Fraction of a walk's unoccluded columns that must sit on ink.</summary>
    private const double MinimumInkScore = 0.72;

    /// <summary>Fraction of a walk's columns that must sit on ink.</summary>
    private const double MinimumSupport = 0.55;

    /// <summary>Every trace is plotted across the whole graph, so a curve must span it.</summary>
    private const double MinimumSpanFraction = 0.90;

    /// <summary>Two walks this close on average are the same curve.</summary>
    private const double DuplicateTolerance = 3.0;

    /// <summary>
    /// A curve must explain at least this share of a full-width line's worth of
    /// otherwise unexplained ink to be worth adding.
    /// </summary>
    private const double MinimumNewCoverageFraction = 0.15;

    /// <summary>Vertical radius used when testing whether a value sits on ink.</summary>
    private const int InkTestRadius = 1;

    // --- In-graph text labels -------------------------------------------------

    /// <summary>
    /// Inside a text label the pale teal traces have to be this bright to count
    /// as curve ink. A label such as "(BL) 09:07:11" is drawn in the same pale
    /// teal as the baseline trace and sits directly on top of it, but the label
    /// is rendered dimmer (its brightest channel averages about 150 against the
    /// trace's 232), so this keeps the trace and demotes the glyphs to occluders.
    /// </summary>
    private const int MinimumLabelBaselineBrightness = 175;

    /// <summary>Shortest text an OCR box must hold to be treated as a label.</summary>
    private const int MinimumLabelCharacters = 3;

    /// <summary>Tallest an OCR box may be to be treated as a label.</summary>
    private const int MaximumLabelHeight = 40;

    /// <summary>How much wider than tall an OCR box must be to be a line of text.</summary>
    private const double MinimumLabelAspect = 1.5;

    /// <summary>
    /// Closest two pale teal curves may sit before they are treated as one
    /// curve found twice. Two traces never share a slot in the waveform stack —
    /// across the sample graphs the nearest genuine pair is about 60px apart —
    /// whereas a label read as a curve lands within a few pixels of the
    /// baseline trace it is printed over.
    /// </summary>
    private const double MinimumBaselineSeparation = 20.0;

    private enum PixelClass : byte
    {
        Background = 0,
        Curve,      // magenta history traces
        Baseline,   // pale teal live/baseline traces
        Marker,     // cyan "+" annotation glyphs
    }

    public static List<Curve> Generate(
        Image<Rgba32> image,
        string csvPath,
        IReadOnlyList<OcrTextRegion>? textRegions = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        return Generate(
            image,
            new Rectangle(0, 0, image.Width, image.Height),
            csvPath,
            textRegions);
    }

    internal static List<Curve> Generate(
        Image<Rgba32> image,
        Rectangle scanBounds,
        string csvPath,
        IReadOnlyList<OcrTextRegion>? textRegions = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(csvPath);

        scanBounds = Rectangle.Intersect(
            scanBounds,
            new Rectangle(0, 0, image.Width, image.Height));
        if (scanBounds.IsEmpty)
            throw new ArgumentException("The databounds image has no pixels to process.", nameof(image));

        List<Curve> curves = ExtractCurves(image, scanBounds, textRegions);
        WriteCurvesToCsv(curves, scanBounds, csvPath);
        return curves;
    }

    private static List<Curve> ExtractCurves(
        Image<Rgba32> image,
        Rectangle scanBounds,
        IReadOnlyList<OcrTextRegion>? textRegions)
    {
        int width = scanBounds.Width;
        int height = scanBounds.Height;

        bool[,] labels = BuildLabelMask(textRegions, scanBounds);
        PixelClass[,] classes = Classify(image, scanBounds, labels, out int[,] brightness);

        // Traces of one color are hidden by marker glyphs and by traces of the
        // other color, so both count as occluders when following a curve.
        bool[,] markers = Dilate(classes, PixelClass.Marker, width, height);
        bool[,] curveInk = MaskOf(classes, PixelClass.Curve, width, height);
        bool[,] baselineInk = MaskOf(classes, PixelClass.Baseline, width, height);

        List<Track> curveTracks = ExtractTracks(
            curveInk,
            Union(markers, Dilate(classes, PixelClass.Baseline, width, height), width, height),
            brightness,
            width,
            height);

        List<Track> baselineTracks = SeparateBaselines(ExtractTracks(
            baselineInk,
            Union(markers, Dilate(classes, PixelClass.Curve, width, height), width, height),
            brightness,
            width,
            height));

        List<Curve> curves = [];
        foreach (Track track in curveTracks)
            curves.Add(BuildCurve(track, image, scanBounds, PixelClass.Curve));
        foreach (Track track in baselineTracks)
            curves.Add(BuildCurve(track, image, scanBounds, PixelClass.Baseline));

        // Report the curves in the order they appear down the stack.
        curves.Sort((first, second) => MedianY(first).CompareTo(MedianY(second)));
        return curves;
    }

    // ---------------------------------------------------------------- classify

    /// <summary>
    /// Assigns each pixel to a palette class using hue ordering. Blending a
    /// color with the black background scales all three channels, so which
    /// channel is lowest survives antialiasing and compression far better than
    /// the absolute distance to a reference color does.
    /// </summary>
    /// <summary>
    /// Marks the pixels covered by an in-graph text label.
    /// </summary>
    /// <remarks>
    /// OCR also reports boxes for wiggles it mistook for characters, and those
    /// are usually either empty, a single character, or a tall block covering
    /// much of the graph. Requiring a short line of text that is wider than it
    /// is tall keeps the real labels and rejects the rest, which matters because
    /// a wrong box would suppress a real curve.
    /// </remarks>
    private static bool[,] BuildLabelMask(
        IReadOnlyList<OcrTextRegion>? textRegions,
        Rectangle scanBounds)
    {
        bool[,] mask = new bool[scanBounds.Width, scanBounds.Height];
        if (textRegions is null)
            return mask;

        foreach (OcrTextRegion region in textRegions)
        {
            if (region.Text.Count(character => !char.IsWhiteSpace(character)) < MinimumLabelCharacters)
                continue;
            if (region.Bounds.Height > MaximumLabelHeight)
                continue;
            if (region.Bounds.Width < MinimumLabelAspect * region.Bounds.Height)
                continue;

            Rectangle bounds = Rectangle.Intersect(region.Bounds, scanBounds);
            if (bounds.Width <= 0 || bounds.Height <= 0)
                continue;

            for (int x = bounds.Left; x < bounds.Right; x++)
                for (int y = bounds.Top; y < bounds.Bottom; y++)
                    mask[x - scanBounds.Left, y - scanBounds.Top] = true;
        }

        return mask;
    }

    private static PixelClass[,] Classify(
        Image<Rgba32> image,
        Rectangle scanBounds,
        bool[,] labels,
        out int[,] brightness)
    {
        int width = scanBounds.Width;
        int height = scanBounds.Height;
        PixelClass[,] classes = new PixelClass[width, height];
        brightness = new int[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                Rgba32 pixel = image[scanBounds.Left + x, scanBounds.Top + y];
                if (pixel.A == 0)
                    continue;

                int red = pixel.R;
                int green = pixel.G;
                int blue = pixel.B;
                int maximumChannel = Math.Max(red, Math.Max(green, blue));
                int minimumChannel = Math.Min(red, Math.Min(green, blue));
                brightness[x, y] = maximumChannel;
                if (maximumChannel == 0)
                    continue;

                // Magenta leaves green as the odd channel out; teal and cyan
                // leave red. Both measures are scaled by the brightest channel
                // so they stay comparable at any brightness.
                double magentaHue = (Math.Min(red, blue) - green) / (double)maximumChannel;
                double tealHue = (Math.Min(green, blue) - red) / (double)maximumChannel;

                if (maximumChannel >= MinimumCurveBrightness &&
                    magentaHue >= MinimumCurveHue &&
                    magentaHue >= tealHue)
                {
                    classes[x, y] = PixelClass.Curve;
                    continue;
                }

                if (tealHue <= magentaHue || tealHue < MinimumBaselineHue)
                    continue;

                double redFraction = red / (double)Math.Max(1, Math.Max(green, blue));
                if (redFraction < MarkerRedFraction)
                {
                    if (maximumChannel >= MinimumCurveBrightness)
                        classes[x, y] = PixelClass.Marker;
                }
                else if (maximumChannel >= MinimumBaselineBrightness &&
                         maximumChannel - minimumChannel >= MinimumBaselineChroma)
                {
                    // A label is the same pale teal as a baseline trace, so
                    // inside one only the brighter trace counts as ink. The dim
                    // glyph pixels become occluders, which lets a trace running
                    // behind a stroke be bridged instead of cut.
                    classes[x, y] = labels[x, y] &&
                                    maximumChannel < MinimumLabelBaselineBrightness
                        ? PixelClass.Marker
                        : PixelClass.Baseline;
                }
            }
        }

        return classes;
    }

    private static bool[,] MaskOf(PixelClass[,] classes, PixelClass wanted, int width, int height)
    {
        bool[,] mask = new bool[width, height];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                mask[x, y] = classes[x, y] == wanted;
        return mask;
    }

    /// <summary>Grows a class by one pixel so its antialiased halo also occludes.</summary>
    private static bool[,] Dilate(PixelClass[,] classes, PixelClass wanted, int width, int height)
    {
        bool[,] mask = new bool[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                if (classes[x, y] != wanted)
                    continue;

                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx;
                    if (nx < 0 || nx >= width)
                        continue;
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int ny = y + dy;
                        if (ny >= 0 && ny < height)
                            mask[nx, ny] = true;
                    }
                }
            }
        }

        return mask;
    }

    private static bool[,] Union(bool[,] first, bool[,] second, int width, int height)
    {
        bool[,] mask = new bool[width, height];
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                mask[x, y] = first[x, y] || second[x, y];
        return mask;
    }

    // -------------------------------------------------------------------- runs

    /// <summary>A vertical stretch of one column that a trace may occupy.</summary>
    private sealed class ColumnRun
    {
        public int FromY;
        public int ToY;

        /// <summary>Brightness-weighted center of the run's ink.</summary>
        public double Center;

        /// <summary>False when the run is made purely of occluding pixels.</summary>
        public bool HasInk;

        public int Height => ToY - FromY + 1;
    }

    /// <summary>
    /// Splits every column into runs of ink or occluder, bridging single-pixel
    /// antialiasing dropouts.
    /// </summary>
    private static List<ColumnRun>[] BuildColumnRuns(
        bool[,] ink,
        bool[,] occluders,
        int[,] brightness,
        int width,
        int height)
    {
        List<ColumnRun>[] runs = new List<ColumnRun>[width];
        for (int x = 0; x < width; x++)
        {
            List<ColumnRun> column = [];
            int y = 0;
            while (y < height)
            {
                if (!ink[x, y] && !occluders[x, y])
                {
                    y++;
                    continue;
                }

                int fromY = y;
                int toY = y;
                while (true)
                {
                    int next = toY + 1;
                    if (next < height && (ink[x, next] || occluders[x, next]))
                    {
                        toY = next;
                        continue;
                    }

                    // Step across a single blank pixel when ink resumes.
                    int afterGap = toY + 2;
                    if (afterGap < height && (ink[x, afterGap] || occluders[x, afterGap]))
                    {
                        toY = afterGap;
                        continue;
                    }

                    break;
                }

                column.Add(CreateRun(ink, brightness, x, fromY, toY));
                y = toY + 1;
            }

            runs[x] = column;
        }

        return runs;
    }

    private static ColumnRun CreateRun(bool[,] ink, int[,] brightness, int x, int fromY, int toY)
    {
        double weightSum = 0;
        double weightedY = 0;
        for (int y = fromY; y <= toY; y++)
        {
            if (!ink[x, y])
                continue;
            double weight = Math.Max(1, brightness[x, y]);
            weightSum += weight;
            weightedY += weight * y;
        }

        return new ColumnRun
        {
            FromY = fromY,
            ToY = toY,
            HasInk = weightSum > 0,
            Center = weightSum > 0 ? weightedY / weightSum : (fromY + toY) / 2.0,
        };
    }

    private static bool Touches(ColumnRun first, ColumnRun second) =>
        first.ToY + LinkSlack >= second.FromY && second.ToY + LinkSlack >= first.FromY;

    // ------------------------------------------------------------------- walks

    /// <summary>
    /// One candidate curve: a value per column, or <see langword="null"/> where
    /// the trace was hidden and the value had to be interpolated.
    /// </summary>
    private sealed class Track
    {
        /// <summary>Values indexed by column; <c>double.NaN</c> outside the span.</summary>
        public double[] Values = [];
        public int FirstColumn;
        public int LastColumn;
        public double InkScore;
        public double Support;

        public int Span => LastColumn - FirstColumn + 1;
    }

    /// <summary>
    /// Follows a trace from one run outwards in a single direction.
    /// </summary>
    /// <remarks>
    /// A rendered polyline always touches itself from one column to the next, so
    /// adjacency alone decides which runs are reachable and the slope estimate
    /// only breaks ties. An earlier version tested the predicted position
    /// against every run, which lost whole traces whenever the prediction
    /// overshot a sharp peak.
    /// </remarks>
    private static void Walk(
        List<ColumnRun>[] runs,
        int startColumn,
        int startRunIndex,
        int step,
        double?[] values)
    {
        int width = runs.Length;
        ColumnRun current = runs[startColumn][startRunIndex];
        values[startColumn] = current.HasInk ? current.Center : (current.FromY + current.ToY) / 2.0;

        int column = startColumn;
        int darkColumns = 0;
        while (true)
        {
            EstimateSlope(values, column, step, out int lastKnownColumn, out double lastKnownValue, out double slope);

            int foundColumn = -1;
            int foundDistance = 0;
            ColumnRun? found = null;
            for (int distance = 1; distance <= MaximumGapColumns; distance++)
            {
                int next = column + (step * distance);
                if (next < 0 || next >= width)
                    break;

                double predicted = lastKnownValue + (slope * (next - lastKnownColumn));
                ColumnRun? best = null;
                double bestKey = double.MaxValue;
                double bestTieBreak = double.MaxValue;

                foreach (ColumnRun candidate in runs[next])
                {
                    if (distance == 1)
                    {
                        // Must touch the run the walk is standing on.
                        if (!Touches(current, candidate))
                            continue;
                    }
                    else
                    {
                        double tolerance = LinkSlack + (1.5 * (distance - 1));
                        if (predicted < candidate.FromY - tolerance || predicted > candidate.ToY + tolerance)
                            continue;
                        if (Math.Abs(candidate.Center - lastKnownValue) > (MaximumSlope * distance) + LinkSlack)
                            continue;
                    }

                    double gap = predicted < candidate.FromY
                        ? candidate.FromY - predicted
                        : predicted > candidate.ToY
                            ? predicted - candidate.ToY
                            : 0.0;
                    double tieBreak = Math.Abs(candidate.Center - predicted);
                    if (best is null || gap < bestKey || (gap == bestKey && tieBreak < bestTieBreak))
                    {
                        best = candidate;
                        bestKey = gap;
                        bestTieBreak = tieBreak;
                    }
                }

                if (best is not null)
                {
                    found = best;
                    foundColumn = next;
                    foundDistance = distance;
                    break;
                }
            }

            if (found is null)
                break;

            // Nothing is known about a column whose run is pure occluder, so
            // leave it blank and interpolate it from real ink on both sides.
            // Snapping to the middle of a marker glyph used to drag walks off
            // their trace and lose the rest of it.
            double? value = null;
            if (found.HasInk)
            {
                double predicted = lastKnownValue + (slope * (foundColumn - lastKnownColumn));
                value = Resolve(found, predicted);
                darkColumns = 0;
            }
            else
            {
                darkColumns += foundDistance;
                if (darkColumns > MaximumDarkColumns)
                    break;
            }

            values[foundColumn] = value;
            current = found;
            column = foundColumn;
        }
    }

    /// <summary>
    /// Picks the value a run contributes. A run's weighted center is the right
    /// answer for a single trace, including steep segments where the run is
    /// tall. When the center contradicts the prediction the run is shared with
    /// another trace, so the walk keeps to its own trajectory instead.
    /// </summary>
    private static double Resolve(ColumnRun run, double predicted)
    {
        if (Math.Abs(predicted - run.Center) <= Math.Max(3.0, 0.6 * run.Height))
            return run.Center;
        return Math.Clamp(predicted, run.FromY, run.ToY);
    }

    private static void EstimateSlope(
        double?[] values,
        int column,
        int step,
        out int lastKnownColumn,
        out double lastKnownValue,
        out double slope)
    {
        Span<int> columns = stackalloc int[SlopeHistory];
        Span<double> known = stackalloc double[SlopeHistory];
        int count = 0;
        for (int x = column; x >= 0 && x < values.Length && count < SlopeHistory; x -= step)
        {
            if (values[x] is not double value)
                continue;
            columns[count] = x;
            known[count] = value;
            count++;
        }

        lastKnownColumn = count > 0 ? columns[0] : column;
        lastKnownValue = count > 0 ? known[0] : 0.0;
        slope = 0.0;
        if (count < 2)
            return;

        // Least-squares fit over the most recent known values.
        double sumX = 0, sumY = 0, sumXX = 0, sumXY = 0;
        for (int i = 0; i < count; i++)
        {
            sumX += columns[i];
            sumY += known[i];
            sumXX += (double)columns[i] * columns[i];
            sumXY += (double)columns[i] * known[i];
        }

        double denominator = (count * sumXX) - (sumX * sumX);
        if (Math.Abs(denominator) < 1e-9)
            return;

        slope = Math.Clamp(
            ((count * sumXY) - (sumX * sumY)) / denominator,
            -MaximumSlope,
            MaximumSlope);
    }

    /// <summary>
    /// Turns a walk into a dense value per column, interpolating the columns
    /// that were hidden behind an occluder.
    /// </summary>
    private static Track? BuildTrack(double?[] values, bool[,] ink, bool[,] occluders, int width, int height)
    {
        int first = -1;
        int last = -1;
        int knownCount = 0;
        for (int x = 0; x < width; x++)
        {
            if (values[x] is null)
                continue;
            if (first < 0)
                first = x;
            last = x;
            knownCount++;
        }

        if (first < 0 || knownCount == 0)
            return null;

        // The walk visits a contiguous span; anything it reached but could not
        // value is filled from its nearest valued neighbours.
        double[] dense = new double[width];
        Array.Fill(dense, double.NaN);
        int previous = -1;
        for (int x = first; x <= last; x++)
        {
            if (values[x] is not double value)
                continue;
            if (previous < 0)
            {
                dense[x] = value;
            }
            else
            {
                dense[previous] = values[previous]!.Value;
                for (int between = previous + 1; between < x; between++)
                {
                    double fraction = (between - previous) / (double)(x - previous);
                    dense[between] = values[previous]!.Value + (fraction * (value - values[previous]!.Value));
                }
                dense[x] = value;
            }

            previous = x;
        }

        Track track = new()
        {
            Values = dense,
            FirstColumn = first,
            LastColumn = last,
        };
        Measure(track, ink, occluders, height);
        return track;
    }

    /// <summary>
    /// Scores a walk against the ink. <see cref="Track.InkScore"/> ignores
    /// columns the trace was legitimately hidden in, so a correct curve scores
    /// near 1 regardless of how many markers cross it, while a walk that
    /// wandered onto empty background is penalised.
    /// </summary>
    private static void Measure(Track track, bool[,] ink, bool[,] occluders, int height)
    {
        int onInk = 0;
        int scored = 0;
        int columns = 0;
        for (int x = track.FirstColumn; x <= track.LastColumn; x++)
        {
            columns++;
            int y = (int)Math.Round(track.Values[x]);
            if (IsNear(ink, x, y, InkTestRadius, height))
            {
                onInk++;
                scored++;
            }
            else if (!IsNear(occluders, x, y, 3, height))
            {
                scored++;
            }
        }

        track.InkScore = scored == 0 ? 0.0 : onInk / (double)scored;
        track.Support = columns == 0 ? 0.0 : onInk / (double)columns;
    }

    private static bool IsNear(bool[,] mask, int x, int y, int radius, int height)
    {
        int from = Math.Max(0, y - radius);
        int to = Math.Min(height - 1, y + radius);
        for (int probe = from; probe <= to; probe++)
        {
            if (mask[x, probe])
                return true;
        }

        return false;
    }

    // --------------------------------------------------------------- selection

    private static List<Track> ExtractTracks(
        bool[,] ink,
        bool[,] occluders,
        int[,] brightness,
        int width,
        int height)
    {
        List<ColumnRun>[] runs = BuildColumnRuns(ink, occluders, brightness, width, height);

        // Seed a walk from every run that actually contains ink. Walks that
        // describe the same trace collapse together in the next step, and every
        // trace is guaranteed at least one seed however heavily it is occluded.
        List<Track> candidates = [];
        for (int x = 0; x < width; x++)
        {
            for (int index = 0; index < runs[x].Count; index++)
            {
                if (!runs[x][index].HasInk)
                    continue;

                double?[] values = new double?[width];
                Walk(runs, x, index, -1, values);
                Walk(runs, x, index, +1, values);

                Track? track = BuildTrack(values, ink, occluders, width, height);
                if (track is null || track.Span < MinimumSpanFraction * width)
                    continue;
                if (track.InkScore < MinimumInkScore || track.Support < MinimumSupport)
                    continue;
                candidates.Add(track);
            }
        }

        return SelectByCoverage(Deduplicate(candidates), ink, width, height);
    }

    /// <summary>
    /// Keeps one pale teal curve per slot in the waveform stack.
    /// </summary>
    /// <remarks>
    /// The "(BL) hh:mm:ss" label is printed in the same pale teal as the
    /// baseline trace and directly over it, so a second walk can follow the
    /// label's glyph strokes instead of the trace. Both land within a few pixels
    /// of each other, far closer than two real traces ever are, so the weaker
    /// one is dropped. Only ever discarding one of two near-coincident walks
    /// means this cannot cost a curve that stands on its own.
    /// </remarks>
    private static List<Track> SeparateBaselines(List<Track> tracks)
    {
        List<Track> ranked = [.. tracks];
        ranked.Sort((first, second) =>
        {
            int byScore = second.InkScore.CompareTo(first.InkScore);
            if (byScore != 0) return byScore;
            int bySupport = second.Support.CompareTo(first.Support);
            return bySupport != 0 ? bySupport : second.Span.CompareTo(first.Span);
        });

        List<Track> kept = [];
        foreach (Track track in ranked)
        {
            bool shadowed = false;
            foreach (Track other in kept)
            {
                double? separation = MedianSeparation(track, other);
                if (separation is not null && separation.Value < MinimumBaselineSeparation)
                {
                    shadowed = true;
                    break;
                }
            }

            if (!shadowed)
                kept.Add(track);
        }

        return kept;
    }

    /// <summary>Collapses walks that describe the same curve, keeping the best.</summary>
    private static List<Track> Deduplicate(List<Track> candidates)
    {
        candidates.Sort((first, second) =>
        {
            int byScore = second.InkScore.CompareTo(first.InkScore);
            return byScore != 0 ? byScore : second.Span.CompareTo(first.Span);
        });

        List<Track> kept = [];
        foreach (Track candidate in candidates)
        {
            bool duplicate = false;
            foreach (Track other in kept)
            {
                double? separation = MedianSeparation(candidate, other);
                if (separation is not null && separation.Value <= DuplicateTolerance)
                {
                    duplicate = true;
                    break;
                }
            }

            if (!duplicate)
                kept.Add(candidate);
        }

        return kept;
    }

    /// <summary>
    /// Median vertical distance between two walks over the columns they share,
    /// or <see langword="null"/> when they barely overlap. The median ignores
    /// the crossings where two genuinely different traces briefly coincide.
    /// </summary>
    private static double? MedianSeparation(Track first, Track second)
    {
        int from = Math.Max(first.FirstColumn, second.FirstColumn);
        int to = Math.Min(first.LastColumn, second.LastColumn);
        if (to < from)
            return null;

        int shorter = Math.Min(first.Span, second.Span);
        int overlap = to - from + 1;
        if (overlap < 0.4 * shorter)
            return null;

        List<double> distances = new(overlap);
        for (int x = from; x <= to; x++)
            distances.Add(Math.Abs(first.Values[x] - second.Values[x]));
        distances.Sort();
        return distances[distances.Count / 2];
    }

    /// <summary>
    /// Keeps the smallest set of walks that explains the ink. Because a curve is
    /// only added while it still accounts for ink nothing else does, the number
    /// of curves comes from the image rather than from a tuned threshold, and
    /// walks that merely retrace an already chosen curve are left out.
    /// </summary>
    private static List<Track> SelectByCoverage(List<Track> candidates, bool[,] ink, int width, int height)
    {
        List<Stamp> stamps = new(candidates.Count);
        foreach (Track candidate in candidates)
            stamps.Add(CreateStamp(candidate, ink, height));

        bool[,] covered = new bool[width, height];
        bool[] used = new bool[candidates.Count];
        List<Track> chosen = [];
        double minimumNew = Math.Max(3.0, MinimumNewCoverageFraction * width);

        while (true)
        {
            int bestIndex = -1;
            int bestNew = -1;
            double bestScore = -1;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (used[i])
                    continue;
                int fresh = stamps[i].CountUncovered(covered);
                if (fresh > bestNew || (fresh == bestNew && candidates[i].InkScore > bestScore))
                {
                    bestIndex = i;
                    bestNew = fresh;
                    bestScore = candidates[i].InkScore;
                }
            }

            if (bestIndex < 0 || bestNew < minimumNew)
                break;

            used[bestIndex] = true;
            chosen.Add(candidates[bestIndex]);
            stamps[bestIndex].MarkCovered(covered);
        }

        return chosen;
    }

    /// <summary>
    /// The ink pixels a walk accounts for, stored as one span of rows per column
    /// so coverage never has to sweep the whole graph.
    /// </summary>
    private sealed class Stamp
    {
        public required int FirstColumn;
        public required int[] FromY;
        public required int[] ToY;
        public required bool[,] Ink;

        public int CountUncovered(bool[,] covered)
        {
            int count = 0;
            for (int i = 0; i < FromY.Length; i++)
            {
                int x = FirstColumn + i;
                for (int y = FromY[i]; y <= ToY[i]; y++)
                    if (Ink[x, y] && !covered[x, y])
                        count++;
            }

            return count;
        }

        public void MarkCovered(bool[,] covered)
        {
            for (int i = 0; i < FromY.Length; i++)
            {
                int x = FirstColumn + i;
                for (int y = FromY[i]; y <= ToY[i]; y++)
                    if (Ink[x, y])
                        covered[x, y] = true;
            }
        }
    }

    private static Stamp CreateStamp(Track track, bool[,] ink, int height)
    {
        int columns = track.Span;
        int[] fromY = new int[columns];
        int[] toY = new int[columns];
        double? previous = null;

        for (int i = 0; i < columns; i++)
        {
            int x = track.FirstColumn + i;
            double value = track.Values[x];

            // Claim the whole vertical step between neighbouring samples so a
            // steeply drawn segment is not reported as unexplained ink.
            double from = previous is null ? value : Math.Min(previous.Value, value);
            double to = previous is null ? value : Math.Max(previous.Value, value);
            int lowest = Math.Max(0, (int)Math.Round(from) - InkTestRadius);
            int highest = Math.Min(height - 1, (int)Math.Round(to) + InkTestRadius);

            // Trim the span to the ink it actually covers so an empty range
            // reduces to a no-op rather than counting background pixels.
            while (lowest <= highest && !ink[x, lowest]) lowest++;
            while (highest >= lowest && !ink[x, highest]) highest--;

            fromY[i] = lowest;
            toY[i] = highest;
            previous = value;
        }

        return new Stamp { FirstColumn = track.FirstColumn, FromY = fromY, ToY = toY, Ink = ink };
    }

    // ------------------------------------------------------------------ output

    private static Curve BuildCurve(
        Track track,
        Image<Rgba32> image,
        Rectangle scanBounds,
        PixelClass category)
    {
        string colorName = category.ToString();
        Curve? curve = null;
        for (int x = track.FirstColumn; x <= track.LastColumn; x++)
        {
            int absoluteX = scanBounds.Left + x;
            int absoluteY = scanBounds.Top + (int)Math.Round(track.Values[x]);
            absoluteY = Math.Clamp(absoluteY, scanBounds.Top, scanBounds.Bottom - 1);

            GraphPixel pixel = new()
            {
                X = absoluteX,
                Y = absoluteY,
                Color = image[absoluteX, absoluteY],
                Distance = track.InkScore,
                ColorName = colorName,
            };

            GraphVerticalRange range = new(pixel);
            if (curve is null)
                curve = new Curve(range);
            else
                curve.VerticalRanges.Add(range);
        }

        return curve!;
    }

    private static double MedianY(Curve curve)
    {
        List<double> values = curve.VerticalRanges
            .Select(range => (double)range.Pixels[0].Y)
            .OrderBy(value => value)
            .ToList();
        return values.Count == 0 ? 0 : values[values.Count / 2];
    }

    private static void WriteCurvesToCsv(
        IEnumerable<Curve> curves,
        Rectangle scanBounds,
        string csvPath)
    {
        string? outputFolder = Path.GetDirectoryName(Path.GetFullPath(csvPath));
        if (!string.IsNullOrEmpty(outputFolder))
            Directory.CreateDirectory(outputFolder);

        using StreamWriter writer = new(csvPath);
        writer.Write("Color");
        for (int x = 0; x < scanBounds.Width; x++)
            writer.Write($",{x}");
        writer.WriteLine();

        foreach (Curve curve in curves)
        {
            Dictionary<int, int> yByX = [];
            foreach (GraphVerticalRange range in curve.VerticalRanges)
            {
                GraphPixel pixel = range.Pixels[0];
                yByX[pixel.X - scanBounds.Left] = pixel.Y - scanBounds.Top;
            }

            writer.Write(curve.Color);
            for (int x = 0; x < scanBounds.Width; x++)
            {
                writer.Write(',');
                if (yByX.TryGetValue(x, out int y))
                    writer.Write(y);
            }

            writer.WriteLine();
        }
    }
}
