using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System;
using System.Collections.Generic;
using System.Text;
using Windows.Devices.Enumeration;
using YamlDotNet.Core;

namespace SignalVision
{
    public class GraphVerticalRange
    {
        public int X { get; set; } = -1;
        public int FromY { get; set; } = -1;
        public int ToY { get; set; } = -1;

        public GraphVerticalRange(int x, int y) { 
            X = x;
            FromY = y;
            ToY = y;
        }

        public GraphVerticalRange(int x, int fromY, int toY)
        {
            X = x;
            FromY = fromY;
            ToY = toY;
        }

        public void Add(int y)
        {
            if (y < FromY)
            {
                FromY = y;
            }
            else
            {
                ToY = y;
            }
        }
    }

    public class Curve
    {
        public string Color { get; set; } = string.Empty;
        public List<GraphVerticalRange> VerticalRanges { get; set; } = new List<GraphVerticalRange>();

        public Curve(int x, int y)
        {
            VerticalRanges.Add(new GraphVerticalRange(x, y));
        }
        public bool IsNeighbor(int x, int y)
        {
            foreach (GraphVerticalRange range in VerticalRanges)
            {
                if (range.X == x || range.X == x - 1)
                {
                    return (range.FromY - 1 <= y) || (range.ToY + 1 >= y);
                }
            }
            return false;
        }

        public void Add(int x, int y)
        {
            foreach (GraphVerticalRange range in VerticalRanges)
            {
                if (range.X == x)
                {
                    range.Add(y);
                    break;
                }
            }
        }

        public void Add(GraphVerticalRange range)
        {
            VerticalRanges.Add(range);
        }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append($"[Color: {Color}] ");
            sb.Append($"[Count: {VerticalRanges.Count}] ");
            if (VerticalRanges.Count > 0)
            {
                sb.Append($"[First FromY: {VerticalRanges[0].FromY}] ");
                sb.Append($"[First x: {VerticalRanges[0].X}] ");
            }
            return sb.ToString();
        }
    }

    public class Graph
    {
        private static readonly Rgba32 TealCurveColor = new(0xa6, 0xec, 0xe1);
        private static readonly Rgba32 TealCurveAlternateColor = new(0x2a, 0xf2, 0xe8);
        private static readonly Rgba32 PurpleCurveColor = new(0xd9, 0x33, 0xc3);
        private const int CurveColorTolerance = 100;
        private const int MaximumCurveStep = 10;
        private const int MaximumReconnectYDifference = 20;
        private const int MaximumCurveStartX = 2;
        private const int GridColorChannelTolerance = 85;
        private const int MinimumGridColor = 85;
        private const int MaximumGridColor = 200;
        private const double MinimumGridLineCoverage = 0.50;

        public int Index { get; }
        public string Title { get; set; } = string.Empty;
        public Rectangle Bounds { get; set; }
        public WindowsPanel Parent { get; }
        public List<Curve> Curves { get; } = [];
        public Rectangle DataBounds { get; }
        public Image<Rgba32>? SanitizedImage { get; private set; }
        public string OutputFolder => Parent.Parent.Parent.Parent.OutputFolder;
        public int PageNumber=>Parent.Parent.Parent.PageNumber;
        public int ImageIndex => Parent.Parent.ImageIndex;
        public int PanelIndex => Parent.Index;

        public Graph(int index, string title, Rectangle bounds, WindowsPanel parent)
        {
            Index = index;
            Title = title;
            Bounds = bounds;
            Parent = parent;
            DataBounds = GetDataBounds();
            Sanitize();
            SaveDataBounds(Path.Combine(OutputFolder, $"databounds_page_{PageNumber}_image_{ImageIndex}_panel_{PanelIndex}_Data_{Index}.png"));//TODO:
            GetCurves();
            //Console.WriteLine("done");
        }

        private void Sanitize()
        {
            SanitizedImage?.Dispose();
            SanitizedImage = Parent.Image?.Clone();
            if (SanitizedImage is null || DataBounds.IsEmpty)
                return;

            Rectangle sanitizeBounds = Rectangle.Intersect(
                DataBounds,
                new Rectangle(0, 0, SanitizedImage.Width, SanitizedImage.Height));
            if (sanitizeBounds.IsEmpty)
                return;

            bool IsGridPixel(Rgba32 pixel)
            {
                int minimumChannel = Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));
                int maximumChannel = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B));

                return pixel.A > 0 &&
                       minimumChannel > MinimumGridColor &&
                       maximumChannel < MaximumGridColor &&
                       maximumChannel - minimumChannel <= GridColorChannelTolerance;
            }

            HashSet<int> gridColumns = [];
            HashSet<int> gridRows = [];

            //SanitizedImage.SaveAsPng("c:/temp/malone.png");//TODO
            for (int x = sanitizeBounds.Left; x < sanitizeBounds.Right; x++)
            {
                int gridPixelCount = 0;
                for (int y = sanitizeBounds.Top; y < sanitizeBounds.Bottom; y++)
                {
                    if (IsGridPixel(SanitizedImage[x, y]))
                        gridPixelCount++;
                }

                if (gridPixelCount >
                    sanitizeBounds.Height * MinimumGridLineCoverage)
                {
                    gridColumns.Add(x);
                }
            }

            for (int y = sanitizeBounds.Top; y < sanitizeBounds.Bottom; y++)
            {
                int gridPixelCount = 0;
                for (int x = sanitizeBounds.Left; x < sanitizeBounds.Right; x++)
                {
                    if (IsGridPixel(SanitizedImage[x, y]))
                        gridPixelCount++;
                }

                if (gridPixelCount >
                    sanitizeBounds.Width * MinimumGridLineCoverage)
                {
                    gridRows.Add(y);
                }
            }

            Rgba32 background = new(0, 0, 0, 255);
            foreach (int x in gridColumns)
            {
                for (int y = sanitizeBounds.Top; y < sanitizeBounds.Bottom; y++)
                {
                    if (IsGridPixel(SanitizedImage[x, y]))
                        SanitizedImage[x, y] = background;
                }
            }

            foreach (int y in gridRows)
            {
                for (int x = sanitizeBounds.Left; x < sanitizeBounds.Right; x++)
                {
                    if (IsGridPixel(SanitizedImage[x, y]))
                        SanitizedImage[x, y] = background;
                }
            }
        }

        public void SaveDataBounds(string path)
        {
            var image = SanitizedImage;
            if (image is null || DataBounds.Width <= 0 || DataBounds.Height <= 0)
                return;
            var bounds = Rectangle.Intersect(
                DataBounds,
                new Rectangle(0, 0, image.Width, image.Height));
            if (bounds.IsEmpty)
                return;
            using var croppedImage = image.Clone(ctx => ctx.Crop(bounds));
            croppedImage.SaveAsPng(path);
        }

        private Rectangle GetDataBounds()
        {
            Image<Rgba32>? image = Parent.VerticalBurredImage;
            if (image is null || Bounds.Width <= 0 || Bounds.Height <= 0)
                return Rectangle.Empty;

            Rectangle graphBounds = Rectangle.Intersect(
                Bounds,
                new Rectangle(0, 0, image.Width, image.Height));
            if (graphBounds.IsEmpty)
                return Rectangle.Empty;

            int left = graphBounds.Left;
            int right = graphBounds.Right - 1;
            int top = graphBounds.Top;
            int bottom = graphBounds.Bottom - 1;

            bool IsDark(Rgba32 pixel)
            {
                return pixel.A > 0 &&
                       pixel.R < 50 &&
                       pixel.G < 50 &&
                       pixel.B < 50;
            }

            bool IsMostlyDarkRow(int y)
            {
                int darkPixels = 0;
                for (int x = graphBounds.Left; x < graphBounds.Right; x++)
                {
                    if (IsDark(image[x, y]))
                        darkPixels++;
                }

                return darkPixels * 2 > graphBounds.Width;
            }

            bool IsMostlyDarkColumn(int x)
            {
                int darkPixels = 0;
                for (int y = graphBounds.Top; y < graphBounds.Bottom; y++)
                {
                    if (IsDark(image[x, y]))
                        darkPixels++;
                }

                return darkPixels * 2 > graphBounds.Height;
            }

            for (int y = graphBounds.Top; y < graphBounds.Bottom; y++)
            {
                if (IsMostlyDarkRow(y))
                {
                    top = y;
                    break;
                }
            }

            for (int y = graphBounds.Bottom - 1; y >= graphBounds.Top; y--)
            {
                if (IsMostlyDarkRow(y))
                {
                    bottom = y;
                    break;
                }
            }

            for (int x = graphBounds.Left; x < graphBounds.Right; x++)
            {
                if (IsMostlyDarkColumn(x))
                {
                    left = x;
                    break;
                }
            }

            for (int x = graphBounds.Right - 1; x >= graphBounds.Left; x--)
            {
                if (IsMostlyDarkColumn(x))
                {
                    right = x;
                    break;
                }
            }

            return Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
        }

        private void GetCurves()
        {
            Curves.Clear();
            Image<Rgba32>? image = SanitizedImage;
            if (image is null || DataBounds.IsEmpty)
                return;

            Rectangle scanBounds = Rectangle.Intersect(
                DataBounds,
                new Rectangle(0, 0, image.Width, image.Height));
            if (scanBounds.IsEmpty)
                return;

            bool IsCurvePixel(Rgba32 pixel)
            {
                bool isTealCurveColor = IsColorWithinTolerance(pixel, TealCurveColor);
                bool isTealCurveAlternateColor = IsColorWithinTolerance(pixel, TealCurveAlternateColor);
                bool isPurpleCurveColor = IsPurpleCurveColor(pixel);// IsColorWithinTolerance(pixel, PurpleCurveColor);

                return isTealCurveColor || isTealCurveAlternateColor || isPurpleCurveColor;
            }

            bool IsPurpleCurveColor(Rgba32 pixel)
            {
                bool flag = IsColorWithinTolerance(pixel, PurpleCurveColor);
                if (flag) return flag;

                int purpleCurveColorSum=PurpleCurveColor.R + PurpleCurveColor.G + PurpleCurveColor.B;
                int pixelSum = pixel.R + pixel.G + pixel.B;
                if (pixelSum < 100 || pixelSum > 400) return false;
                float purpleR= (float)PurpleCurveColor.R / purpleCurveColorSum;
                float purpleG = (float)PurpleCurveColor.G / purpleCurveColorSum;
                float purpleB = (float)PurpleCurveColor.B / purpleCurveColorSum;
                float pixelR = (float)pixel.R / pixelSum;
                float pixelG = (float)pixel.G / pixelSum;
                float pixelB = (float)pixel.B / pixelSum;
                float deltaR = Math.Abs(purpleR - pixelR);
                float deltaG = Math.Abs(purpleG - pixelG);
                float deltaB = Math.Abs(purpleB - pixelB);
                float tolerance = 0.2f; // Adjust this value as needed

                return deltaR < tolerance && deltaG < tolerance && deltaB < tolerance;
            }

            // First retain every complete vertical bar of curve-colored pixels.
            // Keeping the top and bottom is important: a midpoint alone cannot tell
            // us how much of two bars in neighboring columns covers the same Y range.
            List<List<GraphVerticalRange>> barsByColumn = [];
            Parent.Logger.Info($"Base x:{scanBounds.Left}, Base y: {scanBounds.Top}");
            for (int x = scanBounds.Left; x < scanBounds.Right; x++)
            {
                List<GraphVerticalRange> columnBars = [];
                int y = scanBounds.Top;

                while (y < scanBounds.Bottom)
                {
                    Rgba32 color = image[x, y];
                    if (!IsCurvePixel(color))
                    {
                        y++;
                        continue;
                    }

                    int fromY = y;
                    while (y + 1 < scanBounds.Bottom &&
                           IsCurvePixel(image[x, y + 1]))
                    {
                        y++;
                    }

                    columnBars.Add(new GraphVerticalRange(
                        x - scanBounds.Left,
                        fromY - scanBounds.Top,
                        y - scanBounds.Top));
                    y++;
                }

                barsByColumn.Add(columnBars);
            }

            // Walk from the graph's first X column to its last. A curve ending at X
            // competes for the bars at X+1; the pair with the greatest overlapping
            // Y coverage wins first. This prevents two nearby curves from being
            // joined merely because their midpoints happen to be close.
            for (int relativeX = 0; relativeX < barsByColumn.Count; relativeX++)
            {
                List<GraphVerticalRange> columnBars = barsByColumn[relativeX];
                List<Curve> previousColumnCurves = Curves
                    .Where(curve => curve.VerticalRanges[^1].X == relativeX - 1)
                    .ToList();
                HashSet<Curve> matchedCurves = [];
                HashSet<GraphVerticalRange> matchedBars = [];

                var possibleMatches =
                    from curve in previousColumnCurves
                    let previousBar = curve.VerticalRanges[^1]
                    from bar in columnBars
                    let overlap = GetOverlap(previousBar, bar)
                    let gap = GetGap(previousBar, bar)
                    where gap <= MaximumCurveStep
                    orderby overlap descending, gap
                    select new { Curve = curve, Bar = bar };

                foreach (var match in possibleMatches)
                {
                    if (matchedCurves.Contains(match.Curve) ||
                        matchedBars.Contains(match.Bar))
                    {
                        continue;
                    }

                    match.Curve.Add(match.Bar);
                    matchedCurves.Add(match.Curve);
                    matchedBars.Add(match.Bar);
                }

                // If overlap/step matching did not identify a bar's curve, search
                // every curve ending at any earlier X position. Using both the X gap
                // and Y displacement lets a curve reconnect after missing/noisy
                // columns without incorrectly treating the new bar as a new curve.
                var closestMatches =
                    from curve in Curves
                    where !matchedCurves.Contains(curve)
                    let lastBar = curve.VerticalRanges[^1]
                    where lastBar.X < relativeX
                    from bar in columnBars
                    where !matchedBars.Contains(bar)
                    let yDifference = GetMidpointYDifference(lastBar, bar)
                    where yDifference <= MaximumReconnectYDifference
                    let distance = GetEndpointDistanceSquared(lastBar, bar)
                    orderby distance
                    select new { Curve = curve, Bar = bar };

                foreach (var match in closestMatches)
                {
                    if (matchedCurves.Contains(match.Curve) ||
                        matchedBars.Contains(match.Bar))
                    {
                        continue;
                    }

                    match.Curve.Add(match.Bar);
                    matchedCurves.Add(match.Curve);
                    matchedBars.Add(match.Bar);
                }

                // A valid curve must enter from the left edge. Permit a new curve
                // only in relative columns 0, 1, or 2 so a slightly clipped/blurred
                // beginning is accepted, but noise in the middle cannot seed one.
                if (relativeX > MaximumCurveStartX)
                    continue;

                foreach (GraphVerticalRange bar in columnBars)
                {
                    if (matchedBars.Contains(bar))
                        continue;

                    Curve curve = new(bar.X, bar.FromY);
                    curve.VerticalRanges[0].ToY = bar.ToY;
                    Rgba32 firstPixel = image[
                        scanBounds.Left + bar.X,
                        scanBounds.Top + ((bar.FromY + bar.ToY) / 2)];
                    curve.Color = GetCurveColor(firstPixel);
                    Curves.Add(curve);
                }
            }

            // Curve detection is complete. Write one CSV for this graph using
            // graph-relative X/Y coordinates.
            WriteCurvesToCsv(scanBounds.Width);

            static bool IsColorWithinTolerance(Rgba32 pixel, Rgba32 target)
            {
                if (pixel.A == 0)
                    return false;

                int redDifference = pixel.R - target.R;
                int greenDifference = pixel.G - target.G;
                int blueDifference = pixel.B - target.B;

                // Measure the pixel's overall RGB distance from the target color.
                // Comparing the squared values avoids a square-root operation for
                // every scanned pixel while producing the same pass/fail result.
                int squaredColorDistance =
                    (redDifference * redDifference) +
                    (greenDifference * greenDifference) +
                    (blueDifference * blueDifference);
                int squaredTolerance =
                    CurveColorTolerance * CurveColorTolerance;

                return squaredColorDistance <= squaredTolerance;
            }

            static int GetOverlap(
                GraphVerticalRange first,
                GraphVerticalRange second)
            {
                return Math.Max(
                    0,
                    Math.Min(first.ToY, second.ToY) -
                    Math.Max(first.FromY, second.FromY) + 1);
            }

            static int GetGap(
                GraphVerticalRange first,
                GraphVerticalRange second)
            {
                if (first.ToY < second.FromY)
                    return second.FromY - first.ToY;
                if (second.ToY < first.FromY)
                    return first.FromY - second.ToY;
                return 0;
            }

            static long GetEndpointDistanceSquared(
                GraphVerticalRange first,
                GraphVerticalRange second)
            {
                long xDistance = second.X - first.X;
                long yDistance = GetMidpointYDifference(first, second);

                // Squared Euclidean distance gives the same ordering without
                // calculating a square root for every curve/bar candidate.
                return (xDistance * xDistance) + (yDistance * yDistance);
            }

            static int GetMidpointYDifference(
                GraphVerticalRange first,
                GraphVerticalRange second)
            {
                int firstMidpoint = (first.FromY + first.ToY) / 2;
                int secondMidpoint = (second.FromY + second.ToY) / 2;
                return Math.Abs(secondMidpoint - firstMidpoint);
            }

            static string GetCurveColor(Rgba32 pixel)
            {
                int ColorDistance(Rgba32 target) =>
                    Math.Abs(pixel.R - target.R) +
                    Math.Abs(pixel.G - target.G) +
                    Math.Abs(pixel.B - target.B);

                // The two teal shades represent the same logical curve color.
                int tealDistance = Math.Min(
                    ColorDistance(TealCurveColor),
                    ColorDistance(TealCurveAlternateColor));
                int purpleDistance = ColorDistance(PurpleCurveColor);
                return tealDistance <= purpleDistance ? "#a6ece1" : "#d933c3";
            }

        }

        private void WriteCurvesToCsv(int graphWidth)
        {
            Directory.CreateDirectory(OutputFolder);
            string csvPath = Path.Combine(
                OutputFolder,
                $"curves_page_{PageNumber}_image_{ImageIndex}_panel_{PanelIndex}_Data_{Index}.csv");

            using StreamWriter writer = new(csvPath);

            // The first column identifies the curve color. All remaining headers
            // are graph-relative X coordinates beginning at zero.
            writer.Write("Color");
            for (int x = 0; x < graphWidth; x++)
                writer.Write($",{x}");
            writer.WriteLine();

            foreach (Curve curve in Curves)
            {
                Dictionary<int, int> yByX = curve.VerticalRanges.ToDictionary(
                    range => range.X,
                    // Represent the vertical bar by the midpoint of its Y range.
                    range => (range.FromY + range.ToY) / 2);

                writer.Write(curve.Color);
                for (int x = 0; x < graphWidth; x++)
                {
                    writer.Write(',');
                    if (yByX.TryGetValue(x, out int y))
                        writer.Write(y);
                }
                writer.WriteLine();
            }
        }

    }
}
