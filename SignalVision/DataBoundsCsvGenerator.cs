using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SignalVision;

/// <summary>
/// Extracts graph curves from an already-cropped databounds image and writes
/// the curve values in SignalVision's CSV format.
/// </summary>
public static class DataBoundsCsvGenerator
{
    private const int MaximumReconnectXDifference = 5;
    private const int MaximumReconnectYDifference = 5;
    private const int CurveMatchLookaheadColumns = 5;

    public static List<Curve> Generate(Image<Rgba32> image, string csvPath)
    {
        ArgumentNullException.ThrowIfNull(image);
        return Generate(image, new Rectangle(0, 0, image.Width, image.Height), csvPath);
    }

    internal static List<Curve> Generate(
        Image<Rgba32> image,
        Rectangle scanBounds,
        string csvPath)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentException.ThrowIfNullOrWhiteSpace(csvPath);

        scanBounds = Rectangle.Intersect(
            scanBounds,
            new Rectangle(0, 0, image.Width, image.Height));
        if (scanBounds.IsEmpty)
            throw new ArgumentException("The databounds image has no pixels to process.", nameof(image));

        List<Curve> curves = ExtractCurves(image, scanBounds);
        WriteCurvesToCsv(curves, scanBounds, csvPath);
        return curves;
    }

    private static List<Curve> ExtractCurves(Image<Rgba32> image, Rectangle scanBounds)
    {
        List<Curve> curves = [];
        List<List<GraphVerticalRange>> barsByColumn = [];

        // Detect every bar before assigning any of them to a curve. This lets
        // matching use future columns to distinguish a real continuation from
        // a noise bar that immediately reaches a dead end.
        for (int x = scanBounds.Left; x < scanBounds.Right; x++)
        {
            List<GraphVerticalRange> columnBars = [];
            for (int y = scanBounds.Top; y < scanBounds.Bottom; y++)
            {
                Rgba32 color = image[x, y];
                string colorName = "Unknown";

                double baselineScore = IsBaselineColor(color);
                double curveScore = IsCurveColor(color);
                double markerScore = IsMarkerColor(color);
                if (baselineScore > 0.90 || (baselineScore > 0.80 && markerScore > 0.80))
                    colorName = "Baseline";
                else if (curveScore > 0.90 || (curveScore > 0.80 && markerScore > 0.80))
                    colorName = "Curve";
                else if (markerScore > 0.90)
                    colorName = "Marker";

                if (colorName == "Unknown")
                    continue;

                GraphPixel pixel = new()
                {
                    X = x,
                    Y = y,
                    Color = color,
                    Distance = baselineScore,
                    ColorName = colorName
                };

                if (columnBars.Count > 0)
                {
                    GraphVerticalRange lastBar = columnBars[^1];
                    GraphPixel lastPixel = lastBar.Pixels[^1];
                    if (lastPixel.Y + 1 == y && lastPixel.ColorName == pixel.ColorName)
                    {
                        lastBar.Add(pixel);
                        continue;
                    }
                }

                columnBars.Add(new GraphVerticalRange(pixel));
            }

            barsByColumn.Add(columnBars);
        }

        for (int columnIndex = 0; columnIndex < barsByColumn.Count; columnIndex++)
        {
            int x = scanBounds.Left + columnIndex;
            List<GraphVerticalRange> columnBars = barsByColumn[columnIndex];

            if (columnIndex == 0)
            {
                foreach (GraphVerticalRange bar in columnBars)
                    curves.Add(new Curve(bar));

                continue;
            }

            List<(
                Curve Curve,
                GraphVerticalRange Bar,
                int FutureContinuation,
                int Overlap,
                int YGap,
                int XGap,
                int CurrentBarLength,
                int PreviousBarLength)> possibleMatches = [];

            foreach (Curve curve in curves)
            {
                GraphVerticalRange lastBar = curve.VerticalRanges[^1];
                int xGap = x - lastBar.Pixels[0].X;

                // The most recent bar must be to the left. Besides limiting
                // reconnection distance, this guarantees one bar per X for a curve.
                if (xGap < 1 || xGap > MaximumReconnectXDifference)
                    continue;

                foreach (GraphVerticalRange bar in columnBars)
                {
                    int overlap = GetVerticalOverlap(lastBar, bar);
                    int yGap = GetVerticalGap(lastBar, bar);
                    if (overlap == 0 && yGap > MaximumReconnectYDifference)
                        continue;

                    int futureContinuation = GetFutureContinuationLength(
                        bar,
                        barsByColumn,
                        columnIndex,
                        CurveMatchLookaheadColumns);

                    possibleMatches.Add((
                        curve,
                        bar,
                        futureContinuation,
                        overlap,
                        yGap,
                        xGap,
                        bar.Pixels.Count,
                        lastBar.Pixels.Count));
                }
            }

            var orderedMatches = possibleMatches
                .OrderByDescending(match => match.FutureContinuation)
                .ThenByDescending(match => match.Overlap)
                .ThenBy(match => match.YGap)
                .ThenBy(match => match.XGap)
                .ThenByDescending(match => match.CurrentBarLength)
                .ThenByDescending(match => match.PreviousBarLength);

            HashSet<Curve> matchedCurves = [];
            HashSet<GraphVerticalRange> matchedBars = [];

            foreach (var match in orderedMatches)
            {
                if (matchedCurves.Contains(match.Curve) || matchedBars.Contains(match.Bar))
                    continue;

                match.Curve.VerticalRanges.Add(match.Bar);
                matchedCurves.Add(match.Curve);
                matchedBars.Add(match.Bar);
            }

            if (x - 5 >= scanBounds.Left)
                continue;

            foreach (GraphVerticalRange bar in columnBars)
            {
                if (!matchedBars.Contains(bar))
                    curves.Add(new Curve(bar));
            }
        }

        return curves;
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
            Dictionary<int, int> yByX = curve.VerticalRanges.ToDictionary(
                range => range.Pixels[0].X - scanBounds.Left,
                range =>
                    ((range.Pixels[0].Y + range.Pixels[^1].Y) / 2) -
                    scanBounds.Top);

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

    private static int GetFutureContinuationLength(
        GraphVerticalRange bar,
        IReadOnlyList<List<GraphVerticalRange>> barsByColumn,
        int columnIndex,
        int lookaheadColumns)
    {
        int lastColumnIndex = Math.Min(
            barsByColumn.Count - 1,
            columnIndex + lookaheadColumns);

        return GetFutureContinuationLengthCore(
            bar,
            barsByColumn,
            columnIndex + 1,
            lastColumnIndex);
    }

    private static int GetFutureContinuationLengthCore(
        GraphVerticalRange previousBar,
        IReadOnlyList<List<GraphVerticalRange>> barsByColumn,
        int nextColumnIndex,
        int lastColumnIndex)
    {
        int bestContinuation = 0;
        int previousX = previousBar.Pixels[0].X;

        for (int columnIndex = nextColumnIndex;
             columnIndex <= lastColumnIndex;
             columnIndex++)
        {
            foreach (GraphVerticalRange candidateBar in barsByColumn[columnIndex])
            {
                int xGap = candidateBar.Pixels[0].X - previousX;
                if (xGap < 1 || xGap > MaximumReconnectXDifference)
                    continue;

                int overlap = GetVerticalOverlap(previousBar, candidateBar);
                int yGap = GetVerticalGap(previousBar, candidateBar);
                if (overlap == 0 && yGap > MaximumReconnectYDifference)
                    continue;

                int continuation = 1 + GetFutureContinuationLengthCore(
                    candidateBar,
                    barsByColumn,
                    columnIndex + 1,
                    lastColumnIndex);
                bestContinuation = Math.Max(bestContinuation, continuation);
            }
        }

        return bestContinuation;
    }

    private static int GetVerticalOverlap(
        GraphVerticalRange first,
        GraphVerticalRange second)
    {
        return Math.Max(
            0,
            Math.Min(first.Pixels[^1].Y, second.Pixels[^1].Y) -
            Math.Max(first.Pixels[0].Y, second.Pixels[0].Y) + 1);
    }

    private static int GetVerticalGap(
        GraphVerticalRange first,
        GraphVerticalRange second)
    {
        int overlap = GetVerticalOverlap(first, second);
        if (overlap > 0)
            return 0;

        int firstTop = first.Pixels[0].Y;
        int firstBottom = first.Pixels[^1].Y;
        int secondTop = second.Pixels[0].Y;
        int secondBottom = second.Pixels[^1].Y;

        return firstBottom < secondTop
            ? secondTop - firstBottom
            : firstTop - secondBottom;
    }

    private static double MatchColor(Rgba32 color, Rgba32 target)
    {
        int targetSum = target.R + target.G + target.B;
        int pixelSum = color.R + color.G + color.B;

        if (targetSum == 0 || pixelSum == 0)
            return color.Equals(target) ? 1.0 : 0.0;

        double brightnessDelta = Math.Abs(pixelSum - targetSum) / (255.0 * 3.0);
        const double maxBrightnessDelta = 0.12;
        if (brightnessDelta > maxBrightnessDelta)
            return 0.0;

        int targetNeutral = Math.Min(target.R, Math.Min(target.G, target.B));
        int pixelNeutral = Math.Min(color.R, Math.Min(color.G, color.B));
        int targetChromaSum = targetSum - (targetNeutral * 3);
        int pixelChromaSum = pixelSum - (pixelNeutral * 3);

        if (targetChromaSum == 0 || pixelChromaSum == 0)
            return 0.0;

        double targetRRatio = (double)(target.R - targetNeutral) / targetChromaSum;
        double targetGRatio = (double)(target.G - targetNeutral) / targetChromaSum;
        double targetBRatio = (double)(target.B - targetNeutral) / targetChromaSum;
        double pixelRRatio = (double)(color.R - pixelNeutral) / pixelChromaSum;
        double pixelGRatio = (double)(color.G - pixelNeutral) / pixelChromaSum;
        double pixelBRatio = (double)(color.B - pixelNeutral) / pixelChromaSum;

        double distance = Graph.RatioDistance(
            pixelRRatio, pixelGRatio, pixelBRatio,
            targetRRatio, targetGRatio, targetBRatio);

        double maxDistance = Math.Sqrt(2.0);
        return Math.Clamp(1.0 - distance / maxDistance, 0.0, 1.0);
    }

    private static double IsBaselineColor(Rgba32 color) =>
        MatchColor(color, new Rgba32(127, 255, 255));

    private static double IsCurveColor(Rgba32 color) =>
        MatchColor(color, new Rgba32(189, 81, 167));

    private static double IsMarkerColor(Rgba32 color) =>
        MatchColor(color, new Rgba32(0, 255, 255));
}
