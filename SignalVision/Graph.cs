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
    public class Graph
    {
        private static readonly Rgba32 TealCurveColor = new(0xa6, 0xec, 0xe1);
        private static readonly Rgba32 TealCurveAlternateColor = new(0x2a, 0xf2, 0xe8);
        private static readonly Rgba32 PurpleCurveColor = new(0xd9, 0x33, 0xc3);
        private const string TealCurveHexColor = "#a6ece1";
        private const int CurveColorTolerance = 100;
        private const int MaximumCurveStep = 10;
        private const int MaximumReconnectXDifference = 5;
        private const int MaximumReconnectYDifference = 5;
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
            ApplyBilateralFilter();
            Sanitize();
            SaveDataBounds(Path.Combine(OutputFolder, $"databounds_page_{PageNumber}_image_{ImageIndex}_panel_{PanelIndex}_Data_{Index}.png"));//TODO:
            GetCurves2();
            //GetCurves();
            //Console.WriteLine("done");
        }

        private void ApplyBilateralFilter()
        {
            SanitizedImage?.Dispose();
            SanitizedImage = Parent.Image?.Clone();
            if (SanitizedImage is null || DataBounds.IsEmpty)
                return;

            Rectangle filterBounds = Rectangle.Intersect(
                DataBounds,
                new Rectangle(0, 0, SanitizedImage.Width, SanitizedImage.Height));
            if (filterBounds.IsEmpty)
                return;

            // Process only the graph data region and use PNG for the temporary
            // conversion so the filter stage introduces no new JPEG artifacts.
            using Image<Rgba32> graphData = SanitizedImage.Clone(
                context => context.Crop(filterBounds));
            using MemoryStream input = new();
            graphData.SaveAsPng(input);

            using OpenCvSharp.Mat source = OpenCvSharp.Cv2.ImDecode(
                input.ToArray(),
                OpenCvSharp.ImreadModes.Color);
            using OpenCvSharp.Mat filtered = new();

            // Suppress small JPEG color variations while retaining curve edges.
            OpenCvSharp.Cv2.BilateralFilter(
                source,
                filtered,
                d: 5,
                sigmaColor: 50,
                sigmaSpace: 50);

            OpenCvSharp.Cv2.ImEncode(".png", filtered, out byte[] encoded);
            using Image<Rgba32> filteredGraphData =
                SixLabors.ImageSharp.Image.Load<Rgba32>(encoded);
            SanitizedImage.Mutate(context => context.DrawImage(
                filteredGraphData,
                filterBounds.Location,
                1f));

            SanitizedImage.SaveAsPng(Path.Combine(OutputFolder, $"Sanitized_page_{PageNumber}_image_{ImageIndex}_panel_{PanelIndex}_Data_{Index}.png"));//TODO
        }

        private void Sanitize()
        {
            SanitizedImage ??= Parent.Image?.Clone();
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

            //SanitizedImage.SaveAsPng(Path.Combine(OutputFolder, $"databounds_page_{PageNumber}_image_{ImageIndex}_panel_{PanelIndex}_Data_{Index}.png"));//TODO
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

        private bool IsCurvePixel(Rgba32 pixel)
        {
            bool isTealCurveColor = IsColorWithinTolerance(pixel, TealCurveColor);
            bool isTealCurveAlternateColor = IsColorWithinTolerance(pixel, TealCurveAlternateColor);
            bool isPurpleCurveColor = IsPurpleCurveColor(pixel);// IsColorWithinTolerance(pixel, PurpleCurveColor);

            return isTealCurveColor || isTealCurveAlternateColor || isPurpleCurveColor;
        }

        private bool IsPurpleCurveColor(Rgba32 pixel)
        {
            bool flag = IsColorWithinTolerance(pixel, PurpleCurveColor);
            if (flag) return flag;

            int purpleCurveColorSum = PurpleCurveColor.R + PurpleCurveColor.G + PurpleCurveColor.B;
            int pixelSum = pixel.R + pixel.G + pixel.B;
            if (pixelSum < 100 || pixelSum > 400) return false;
            float purpleR = (float)PurpleCurveColor.R / purpleCurveColorSum;
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

        private void GetCurves2()
        {
            Curves.Clear();
            Image<Rgba32>? image = SanitizedImage;
            if (image is null || DataBounds.IsEmpty)
                return;

            Rectangle scanBounds = Rectangle.Intersect(DataBounds, new Rectangle(0, 0, image.Width, image.Height));
            if (scanBounds.IsEmpty)
                return;

            for (int x = scanBounds.Left; x < scanBounds.Right; x++)
            {
                List<GraphVerticalRange> columnBars = [];
                for (int y = scanBounds.Top; y < scanBounds.Bottom; y++)
                {
                    Rgba32 color = image[x, y];
                    string colorName = "Unknown";

                    if (x == 10 && y == 335)
                    {
                        Console.WriteLine("test");//TODO:
                    }

                    double baselineScore = IsBaselineColor(color);
                    double curveScore = IsCurveColor(color);
                    double markerScore = IsMarkerColor(color);
                    if (baselineScore > 0.90 || (baselineScore > 0.80 && markerScore > 0.80))
                        colorName = "Baseline";
                    else if (curveScore > 0.90 || (curveScore > 0.80 && markerScore > 0.80))
                        colorName = "Curve";
                    else if (markerScore > 0.90)
                        colorName = "Marker";

                    if (colorName != "Unknown")
                    {
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
                            }
                            else
                            {
                                GraphVerticalRange graphPixels = new(pixel);
                                columnBars.Add(graphPixels);
                            }
                        }
                        else
                        {
                            GraphVerticalRange graphPixels = new(pixel);
                            columnBars.Add(graphPixels);
                        }
                    }
                }

                if (columnBars.Count != 2)
                {
                    Console.WriteLine($"Column {x} has {columnBars.Count} bars");//TODO:
                }
                if (x == 109)
                {
                    Console.WriteLine("test");//TODO:
                }

                if (x == scanBounds.Left)
                {
                    foreach(GraphVerticalRange bar in columnBars)
                    {
                        Curve curve = new(bar);
                        Curves.Add(curve);
                    }
                }
                else
                {
                    foreach (GraphVerticalRange bar in columnBars)
                    {
                        if (bar.Pixels[0].X==12 && bar.Pixels[0].Y == 337)
                        {
                            Console.WriteLine("test");//TODO:
                        }
                        Curve? nearestCurve = null;
                        int longestBarLength = -1;
                        int bestOverlap = -1;
                        int bestYGap = int.MaxValue;
                        int bestXGap = int.MaxValue;

                        int barTop = bar.Pixels[0].Y;
                        int barBottom = bar.Pixels[^1].Y;

                        foreach (Curve curve in Curves)
                        {
                            GraphVerticalRange lastBar = curve.VerticalRanges[^1];
                            if (curve.VerticalRanges.Count==2 && curve.VerticalRanges[0].Pixels[0].X==8 && curve.VerticalRanges[0].Pixels[0].Y == 339)
                            {
                                Console.WriteLine("test");//TODO:
                            }

                            // Allow the curve to bridge columns whose bar was lost to
                            // JPEG compression. The most recent bar must still be to
                            // the left, which also prevents two bars at the same X
                            // coordinate from attaching to one curve.
                            int xGap = x - lastBar.Pixels[0].X;
                            if (xGap < 1 || xGap > MaximumReconnectXDifference)
                                continue;

                            int lastTop = lastBar.Pixels[0].Y;
                            int lastBottom = lastBar.Pixels[^1].Y;

                            // Number of shared Y coordinates, inclusive.
                            int overlap = Math.Max(
                                0,
                                Math.Min(barBottom, lastBottom) -
                                Math.Max(barTop, lastTop) + 1);

                            // Distance between the two vertical ranges.
                            int yGap;
                            if (overlap > 0)
                            {
                                yGap = 0;
                            }
                            else if (barBottom < lastTop)
                            {
                                yGap = lastTop - barBottom;
                            }
                            else
                            {
                                yGap = barTop - lastBottom;
                            }

                            if (overlap == 0 &&
                                yGap > MaximumReconnectYDifference)
                            {
                                continue;
                            }

                            int lastBarLength = lastBar.Pixels.Count;

                            if (overlap > bestOverlap ||
                                (overlap == bestOverlap && yGap < bestYGap) ||
                                (overlap == bestOverlap &&
                                 yGap == bestYGap &&
                                 xGap < bestXGap) ||
                                (overlap == bestOverlap &&
                                 yGap == bestYGap &&
                                 xGap == bestXGap &&
                                 lastBarLength > longestBarLength))
                            {
                                nearestCurve = curve;
                                longestBarLength = lastBarLength;
                                bestOverlap = overlap;
                                bestYGap = yGap;
                                bestXGap = xGap;
                            }
                        }

                        if (nearestCurve is not null)
                        {
                            nearestCurve.VerticalRanges.Add(bar);
                        }
                        else if (x-5 < scanBounds.Left)
                        {
                            // Optional: start a new curve when no previous-column match exists.
                            Curves.Add(new Curve(bar));
                        }
                    }
                }
            }
            Console.WriteLine($"test");
        }

        public static double RatioDistance(double r1, double g1, double b1, double r2, double g2, double b2)
        {
            double dr = r1 - r2;
            double dg = g1 - g2;
            double db = b1 - b2;

            return Math.Sqrt(
                dr * dr +
                dg * dg +
                db * db);
        }

        private double MatchColor(Rgba32 color, Rgba32 target)
        {
            int targetSum = target.R + target.G + target.B;
            int pixelSum = color.R + color.G + color.B;

            if (targetSum == 0 || pixelSum == 0)
                return color.Equals(target) ? 1.0 : 0.0;

            // Filter pixels whose overall brightness differs too much.
            double brightnessDelta = Math.Abs(pixelSum - targetSum) / (255.0 * 3.0);

            const double maxBrightnessDelta = 0.12; // Tune between 0.08–0.15
            if (brightnessDelta > maxBrightnessDelta)
                return 0.0;

            // Remove the neutral component before comparing hue. Anti-aliasing can
            // add nearly the same amount to all three channels; for example,
            // (118, 209, 201) still has a cyan chromatic component of (0, 91, 83).
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

            double distance = RatioDistance(
                pixelRRatio, pixelGRatio, pixelBRatio,
                targetRRatio, targetGRatio, targetBRatio);

            double maxDistance = Math.Sqrt(2.0);
            return Math.Clamp(1.0 - distance / maxDistance, 0.0, 1.0);
        }

        private double IsBaselineColor(Rgba32 color)
        {
            return MatchColor(color, new Rgba32(127, 255, 255));
        }

        private double IsCurveColor(Rgba32 color)
        {
            //(186,85,211) #BA55D3
            //return MatchColor(color, new Rgba32(186, 85, 211));
            return MatchColor(color, new Rgba32(189, 81, 167));
        }

        private double IsMarkerColor(Rgba32 color)
        {
            //(0,255,255) #00FFFF
            return MatchColor(color, new Rgba32(0, 255, 255));
        }
        /*
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

            // Keep the first teal curve and the curves following it up to, but not
            // including, the second teal curve.
            TrimCurvesToFirstTealSection();

            // Curve detection is complete. Write one CSV for this graph using
            // graph-relative X/Y coordinates.
            WriteCurvesToCsv(scanBounds.Width);
        }*/

        private static bool IsColorWithinTolerance(Rgba32 pixel, Rgba32 target)
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
        /*
        private static int GetOverlap(
            GraphVerticalRange first,
            GraphVerticalRange second)
        {
            return Math.Max(
                0,
                Math.Min(first.ToY, second.ToY) -
                Math.Max(first.FromY, second.FromY) + 1);
        }

        private static int GetGap(
            GraphVerticalRange first,
            GraphVerticalRange second)
        {
            if (first.ToY < second.FromY)
                return second.FromY - first.ToY;
            if (second.ToY < first.FromY)
                return first.FromY - second.ToY;
            return 0;
        }

        private static long GetEndpointDistanceSquared(
            GraphVerticalRange first,
            GraphVerticalRange second)
        {
            long xDistance = second.X - first.X;
            long yDistance = GetMidpointYDifference(first, second);

            // Squared Euclidean distance gives the same ordering without
            // calculating a square root for every curve/bar candidate.
            return (xDistance * xDistance) + (yDistance * yDistance);
        }

        private static int GetMidpointYDifference(
            GraphVerticalRange first,
            GraphVerticalRange second)
        {
            int firstMidpoint = (first.FromY + first.ToY) / 2;
            int secondMidpoint = (second.FromY + second.ToY) / 2;
            return Math.Abs(secondMidpoint - firstMidpoint);
        }

        private static string GetCurveColor(Rgba32 pixel)
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
            return tealDistance <= purpleDistance
                ? TealCurveHexColor
                : "#d933c3";
        }

        private void TrimCurvesToFirstTealSection()
        {
            int firstTealIndex = Curves.FindIndex(curve =>
                string.Equals(
                    curve.Color,
                    TealCurveHexColor,
                    StringComparison.OrdinalIgnoreCase));

            // Leave the detected curves intact if no teal boundary was found.
            if (firstTealIndex < 0)
                return;

            int secondTealIndex = Curves.FindIndex(
                firstTealIndex + 1,
                curve => string.Equals(
                    curve.Color,
                    TealCurveHexColor,
                    StringComparison.OrdinalIgnoreCase));

            // Removing from the second teal through the end also removes the
            // second teal boundary itself.
            if (secondTealIndex >= 0)
                Curves.RemoveRange(secondTealIndex, Curves.Count - secondTealIndex);

            if (firstTealIndex > 0)
                Curves.RemoveRange(0, firstTealIndex);
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
        }*/

    }
}
