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
            //GetCurves3();
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

        private void GetCurves3()
        {
            Curves.Clear();
            Image<Rgba32>? image = SanitizedImage;
            if (image is null || DataBounds.IsEmpty)
                return;

            Rectangle scanBounds = Rectangle.Intersect(DataBounds, new Rectangle(0, 0, image.Width, image.Height));
            if (scanBounds.IsEmpty)
                return;

            double[,,] colorLikenessScores = new double[scanBounds.Width, scanBounds.Height, 3]; // 0: baseline, 1: curve, 2: marker]


            for (int x = scanBounds.Left; x < scanBounds.Right; x++)
            {
                for (int y = scanBounds.Top; y < scanBounds.Bottom; y++)
                {
                    Rgba32 color = image[x, y];
                    double baselineLikeness=ColorLikeness(color.R, color.G, color.B, 127, 255, 255);
                    double curveLikeness = ColorLikeness(color.R, color.G, color.B, 189, 81, 167);
                    double markerLikeness = ColorLikeness(color.R, color.G, color.B, 0, 255, 255);

                    colorLikenessScores[x - scanBounds.Left, y - scanBounds.Top, 0] = baselineLikeness;
                    colorLikenessScores[x - scanBounds.Left, y - scanBounds.Top, 1] = curveLikeness;
                    colorLikenessScores[x - scanBounds.Left, y - scanBounds.Top, 2] = markerLikeness;
                }
            }

            for (int x = 0; x < scanBounds.Width; x++)
            {
                for (int y = 0; y < scanBounds.Height; y++)
                {
                    if (x == 0)
                    {
                        if (colorLikenessScores[x, y, 0] > 0.8)
                        {
                            GraphPixel pixel = new()
                            {
                                X = x + scanBounds.Left,
                                Y = y + scanBounds.Top,
                                Color = image[x + scanBounds.Left, y + scanBounds.Top],
                                Distance = colorLikenessScores[x, y, 0],
                                ColorName = "Baseline"
                            };
                            if (Curves.Count > 0 && Curves.Last().VerticalRanges.Last().Pixels.Last().Y + 1 == pixel.Y)
                            {
                                Curves.Last().VerticalRanges.Last().Pixels.Add(pixel);
                            }
                            else
                            {
                                GraphVerticalRange bar = new(pixel);
                                Curve curve = new(bar);
                                Curves.Add(curve);
                            }
                        }
                    }
                }
            }

            Console.WriteLine("test");
        }

        public static double ColorLikeness(
            byte r, byte g, byte b,
            byte targetR, byte targetG, byte targetB)
        {
            // -------------------------
            // 1. Magnitude / brightness
            // -------------------------
            double magnitude = Math.Sqrt(
                r * r +
                g * g +
                b * b);

            double targetMagnitude = Math.Sqrt(
                targetR * targetR +
                targetG * targetG +
                targetB * targetB);

            if (targetMagnitude == 0)
                return 0;

            // Extremely dark pixels should have very low confidence.
            // Relative brightness compared with target.
            double brightnessRatio = magnitude / targetMagnitude;

            double brightnessScore;

            if (brightnessRatio < 0.10)
            {
                brightnessScore = 0;
            }
            else if (brightnessRatio < 0.30)
            {
                // Gradually increase from 0 -> 1
                brightnessScore =
                    (brightnessRatio - 0.10) / 0.20;
            }
            else if (brightnessRatio <= 1.10)
            {
                brightnessScore = 1.0;
            }
            else if (brightnessRatio < 1.50)
            {
                // Gradually penalize pixels that are too bright
                brightnessScore =
                    (1.50 - brightnessRatio) / 0.40;
            }
            else
            {
                brightnessScore = 0;
            }

            // -------------------------
            // 2. Color direction
            // -------------------------
            if (magnitude == 0)
                return 0;

            double dot =
                r * targetR +
                g * targetG +
                b * targetB;

            double cosine =
                dot / (magnitude * targetMagnitude);

            cosine = Math.Clamp(cosine, 0.0, 1.0);

            // Make mediocre color matches fall off faster.
            double colorScore = Math.Pow(cosine, 8);


            // -------------------------
            // 3. Chroma
            // -------------------------
            int max = Math.Max(r, Math.Max(g, b));
            int min = Math.Min(r, Math.Min(g, b));

            double chroma = max - min;

            // Gray-ish pixels should score lower.
            double chromaScore = Math.Clamp(chroma / 30.0, 0.0, 1.0);


            // -------------------------
            // Final likeness
            // -------------------------
            return colorScore *
                   brightnessScore *
                   chromaScore;
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

            string csvPath = Path.Combine(
                OutputFolder,
                $"curves_page_{PageNumber}_image_{ImageIndex}_panel_{PanelIndex}_Data_{Index}.csv");

            Curves.AddRange(DataBoundsCsvGenerator.Generate(
                image,
                scanBounds,
                csvPath,
                GetTextRegions(image, scanBounds)));
        }

        /// <summary>
        /// Locates the labels printed inside the plot, such as "(BL) 09:07:11".
        /// </summary>
        /// <remarks>
        /// These are drawn in the same pale teal as the baseline trace and sit
        /// on top of it, so curve extraction needs to know where they are to
        /// avoid reporting a label as a curve. Coordinates are returned in the
        /// source image's space to match <paramref name="scanBounds"/>.
        /// </remarks>
        private IReadOnlyList<OcrTextRegion> GetTextRegions(
            Image<Rgba32> image,
            Rectangle scanBounds)
        {
            try
            {
                using Image<Rgba32> plotArea = image.Clone(context => context.Crop(scanBounds));
                IReadOnlyList<OcrTextRegion> regions = OCRHelper.DetectTextRegions(
                    plotArea,
                    Parent.Parent.Config,
                    Parent.Logger.WithTag("Graph labels"));

                return regions
                    .Select(region => new OcrTextRegion
                    {
                        Text = region.Text,
                        Bounds = new Rectangle(
                            region.Bounds.X + scanBounds.Left,
                            region.Bounds.Y + scanBounds.Top,
                            region.Bounds.Width,
                            region.Bounds.Height),
                    })
                    .ToList();
            }
            catch (Exception exception)
            {
                // Label detection only refines extraction, so a failure here
                // must not cost us the curves.
                Parent.Logger.Warn($"Graph label detection failed: {exception.Message}");
                return [];
            }
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
