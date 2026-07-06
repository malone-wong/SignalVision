using System.Text;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace SignalVision
{
    public class CaseSummaryData
    {
        public PdfDocument Document { get; }
        public Config Config { get; }
        public List<CaseSummaryPage> Pages { get; } = [];
        public List<string> Text { get; } = [];
        public List<SignalBitmap> Bitmaps { get; } = [];

        public CaseSummaryData(PdfDocument document, Config config)
        {
            Document = document;
            Config = config;
            int pageNumber = 1;
            //loop for each page
            foreach (var page in Document.GetPages())
            {
                Console.WriteLine($"--> Page {pageNumber}");
                Pages.Add(new CaseSummaryPage(pageNumber, page, config));

                //TODO: delete
                StringBuilder sb = new();
                //loop for each word in page
                foreach (var word in page.GetWords())
                {
                    sb.Append(word.Text).Append(' ');
                }

                Text.Add(sb.ToString());

                int imageNumber = 1;
                //loop for each image in page
                foreach (IPdfImage image in page.GetImages())
                {
                    // Try to create a SignalBitmap from the image, and if successful, add it to the Bitmaps list
                    if (TryCreateSignalBitmap(image, pageNumber, imageNumber, out SignalBitmap? bitmap) && bitmap is not null)
                    {
                        Bitmaps.Add(bitmap);
                        Console.WriteLine($"    image {imageNumber}: {bitmap.Width}x{bitmap.Height} bitmap ready");
                    }
                    else
                    {
                        Console.WriteLine($"    image {imageNumber}: skipped, unsupported image encoding");
                    }

                    imageNumber++;
                }

                pageNumber++;
            }
        }

        private bool TryCreateSignalBitmap(IPdfImage pdfImage, int pageNumber, int imageNumber, out SignalBitmap? bitmap)
        {
            byte[] rawBytes = pdfImage.RawBytes.ToArray();
            bool loadedRawImage = TryLoadImage(rawBytes, out Image<Rgba32>? image);
            if (!loadedRawImage
                && (!pdfImage.TryGetPng(out byte[]? pngBytes) || !TryLoadImage(pngBytes, out image)))
            {
                bitmap = null;
                return false;
            }

            using (Image<Rgba32> loadedImage = image!)
            {
                if (loadedRawImage && IsJpeg(rawBytes))
                {
                    loadedImage.Mutate(context => context.Flip(FlipMode.Vertical));
                }

                byte[] luminancePixels = new byte[loadedImage.Width * loadedImage.Height];
                loadedImage.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < accessor.Height; y++)
                    {
                        Span<Rgba32> row = accessor.GetRowSpan(y);
                        int rowOffset = y * accessor.Width;

                        for (int x = 0; x < row.Length; x++)
                        {
                            Rgba32 pixel = row[x];
                            luminancePixels[rowOffset + x] = ToLuminance(pixel);
                        }
                    }
                });

                bitmap = new SignalBitmap(
                    pageNumber,
                    imageNumber,
                    loadedImage.Width,
                    loadedImage.Height,
                    pdfImage.Bounds,
                    luminancePixels);

                SaveWindowPanels(
                    loadedImage,
                    pageNumber,
                    imageNumber,
                    Config);
                return true;
            }
        }

        private static bool TryLoadImage(byte[] imageBytes, out Image<Rgba32>? image)
        {
            try
            {
                image = Image.Load<Rgba32>(imageBytes);
                return true;
            }
            catch
            {
                image = null;
                return false;
            }
        }

        private static byte ToLuminance(Rgba32 pixel)
        {
            return (byte)Math.Clamp(
                (int)Math.Round((0.299 * pixel.R) + (0.587 * pixel.G) + (0.114 * pixel.B)),
                byte.MinValue,
                byte.MaxValue);
        }

        private static bool IsJpeg(ReadOnlySpan<byte> bytes)
        {
            return bytes.Length >= 3
                && bytes[0] == 0xFF
                && bytes[1] == 0xD8
                && bytes[2] == 0xFF;
        }

        private static List<PanelBounds> GetPanels(Image<Rgba32> image, Config config)
        {
            List<PanelBounds> panels = [];
            int[,] area=new int[image.Width, image.Height];
            Rgba32 titleColor = ParseColor(config.WindowsPanel.TitleColor);
            int titleColorTolerance = config.WindowsPanel.TitleColorTolerance;
            int maxAreaId = 1;
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    Span<Rgba32> row = accessor.GetRowSpan(y);
                    for (int x = 0; x < row.Length; x++)
                    {
                        Rgba32 color = row[x];
                        if (IsTitleColor(color, titleColor, titleColorTolerance))
                        {
                            if (y == 0)
                            {
                                area[x, y] = 1;
                            }
                        }
                    }
                }
            });
            return panels;
        }

        private static void SaveWindowPanels(Image<Rgba32> image, int pageNumber, int imageNumber, Config config)
        {
            /*
ParseColor(Config.WindowsPanel.TitleColor),
                    Config.WindowsPanel.TitleColorTolerance
             */
            List<PanelBounds> panels = GetPanels(image, config);

            /*List<PanelTitleBar> titleBars = FindPanelTitleBars(image, titleColor, titleColorTolerance);
            List<PanelBounds> panels = titleBars
                .OrderBy(titleBar => titleBar.Y)
                .ThenBy(titleBar => titleBar.X)
                .Select(titleBar => GetPanelBounds(image, titleBars, titleBar, titleColor, titleColorTolerance))
                .Where(panel => panel.Bounds.Width > 0 && panel.Bounds.Height > panel.TitleBar.TitleHeight)
                .DistinctBy(panel => (panel.Bounds.X, panel.Bounds.Y, panel.Bounds.Width, panel.Bounds.Height))
                .ToList();*/

            if (panels.Count == 0)
            {
                return;
            }

            Directory.CreateDirectory(@"c:\temp");
            foreach (string existingFile in Directory.EnumerateFiles(@"c:\temp", $"window-panel-page-{pageNumber:D3}-image-{imageNumber:D3}-panel-*.png"))
            {
                File.Delete(existingFile);
            }

            int panelNumber = 1;
            foreach (PanelBounds panelBounds in panels)
            {
                string outputPath = Path.Combine(
                    @"c:\temp",
                    $"window-panel-page-{pageNumber:D3}-image-{imageNumber:D3}-panel-{panelNumber:D3}-x{panelBounds.Bounds.X}-y{panelBounds.Bounds.Y}.png");

                using Image<Rgba32> panel = image.Clone(context => context.Crop(panelBounds.Bounds));
                panel.SaveAsPng(outputPath);
                panelNumber++;
            }

            Console.WriteLine($"    saved {panels.Count} window panel(s) to c:\\temp");
        }

        private static (int x, int width) GetTitleBarRow(Span<Rgba32> row, Rgba32 titleColor, int titleColorTolerance)
        {
            int firstTitleBarX = -1;
            int width = 0;
            for (int x = 0; x < row.Length; x++)
            {
                if (IsTitleColor(row[x], titleColor, titleColorTolerance))
                {
                    width++;
                    if (firstTitleBarX == -1)
                    {
                        firstTitleBarX = x;
                    }
                }
            }
            return (firstTitleBarX, width);
        }


        private static List<PanelTitleBar> FindPanelTitleBars(Image<Rgba32> image, Rgba32 titleColor, int titleColorTolerance)
        {
            List<PanelTitleBar> titleBars = [];

            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height - 12; y++)
                {
                    Span<Rgba32> row = accessor.GetRowSpan(y);

                    for (int x = 0; x < row.Length; x++)
                    {
                        if (!IsTitleColor(row[x], titleColor, titleColorTolerance)
                            || titleBars.Any(existing => IsInsideTitleBar(existing, x, y)))
                        {
                            continue;
                        }

                        PanelTitleBar? titleBar = MeasureTitleBarArea(accessor, x, y, titleColor, titleColorTolerance);
                        if (titleBar is null)
                        {
                            continue;
                        }

                        if (titleBar.Width < 120 || titleBar.Width > accessor.Width - 20)
                        {
                            continue;
                        }

                        if (y < 60 && (titleBar.X > 5 || titleBar.Width < accessor.Width * 0.6))
                        {
                            continue;
                        }

                        if (titleBars.Any(existing => IsDuplicateTitleBar(existing, titleBar.X, titleBar.Y, titleBar.Width)))
                        {
                            continue;
                        }

                        titleBars.Add(titleBar);
                        x = Math.Min(row.Length - 1, titleBar.X + titleBar.Width - 1);
                    }
                }
            });

            return titleBars;
        }

        private static PanelTitleBar? MeasureTitleBarArea(
            PixelAccessor<Rgba32> accessor,
            int startX,
            int startY,
            Rgba32 titleColor,
            int titleColorTolerance)
        {
            const int minTitleBarHeight = 14;
            const int maxTitleBarHeight = 32;
            const double minRowCoverage = 0.35;

            int right = startX + 1;
            int height = 0;
            int maxHeight = Math.Min(maxTitleBarHeight, accessor.Height - startY);

            for (int offsetY = 0; offsetY < maxHeight; offsetY++)
            {
                int rowY = startY + offsetY;
                (int rowRight, double rowCoverage) = MeasureTitleBarRowExtent(
                    accessor,
                    startX,
                    rowY,
                    titleColor,
                    titleColorTolerance);

                if (rowRight <= startX || (rowCoverage < minRowCoverage && height >= minTitleBarHeight))
                {
                    break;
                }

                right = Math.Max(right, rowRight);
                height++;
            }

            if (height < minTitleBarHeight)
            {
                return null;
            }

            int width = right - startX;
            double coverage = MeasureTitleColorCoverage(accessor, startX, startY, width, height, titleColor, titleColorTolerance);
            if (coverage < 0.35)
            {
                return null;
            }

            return new PanelTitleBar(startX, startY, width, height);
        }

        private static (int Right, double Coverage) MeasureTitleBarRowExtent(
            PixelAccessor<Rgba32> accessor,
            int startX,
            int y,
            Rgba32 titleColor,
            int titleColorTolerance)
        {
            const int maxTitleTextOrIconGapPixels = 220;

            Span<Rgba32> row = accessor.GetRowSpan(y);
            int right = startX;
            int titleAreaPixels = 0;
            int totalPixels = 0;
            int gapPixels = 0;

            for (int x = startX; x < row.Length; x++)
            {
                bool isTitleAreaPixel = IsTitleBarPixel(accessor, row, x, y, titleColor, titleColorTolerance);
                totalPixels++;

                if (isTitleAreaPixel)
                {
                    titleAreaPixels++;
                    right = x + 1;
                    gapPixels = 0;
                    continue;
                }

                if (right > startX && gapPixels++ >= maxTitleTextOrIconGapPixels)
                {
                    break;
                }
            }

            int measuredWidth = Math.Max(0, right - startX);
            double coverage = measuredWidth == 0 ? 0 : (double)titleAreaPixels / Math.Min(totalPixels, measuredWidth);
            return (right, coverage);
        }

        private static List<(int StartX, int Width)> GetTitleBarRuns(
            PixelAccessor<Rgba32> accessor,
            Span<Rgba32> row,
            int y,
            Rgba32 titleColor,
            int titleColorTolerance)
        {
            List<(int StartX, int Width)> runs = [];
            int x = 0;

            while (x < row.Length)
            {
                while (x < row.Length && !IsTitleBarPixel(accessor, row, x, y, titleColor, titleColorTolerance))
                {
                    x++;
                }

                int startX = x;
                while (x < row.Length && IsTitleBarPixel(accessor, row, x, y, titleColor, titleColorTolerance))
                {
                    x++;
                }

                if (x > startX)
                {
                    runs.Add((startX, x - startX));
                }
            }

            return MergeTitleBarRuns(accessor, row, y, runs, titleColor, titleColorTolerance);
        }

        private static List<(int StartX, int Width)> MergeTitleBarRuns(
            PixelAccessor<Rgba32> accessor,
            Span<Rgba32> row,
            int y,
            List<(int StartX, int Width)> runs,
            Rgba32 titleColor,
            int titleColorTolerance)
        {
            const int maxTitleTextGapPixels = 220;
            if (runs.Count <= 1)
            {
                return runs;
            }

            List<(int StartX, int EndX)> mergedRuns = [];
            (int StartX, int EndX) current = (runs[0].StartX, runs[0].StartX + runs[0].Width);

            for (int i = 1; i < runs.Count; i++)
            {
                (int StartX, int EndX) next = (runs[i].StartX, runs[i].StartX + runs[i].Width);
                int gapStart = current.EndX;
                int gapEnd = next.StartX;
                int gapWidth = gapEnd - gapStart;

                if (gapWidth <= maxTitleTextGapPixels
                    && IsTitleTextOrIconGap(accessor, row, y, gapStart, gapEnd, titleColor, titleColorTolerance))
                {
                    current = (current.StartX, next.EndX);
                    continue;
                }

                mergedRuns.Add(current);
                current = next;
            }

            mergedRuns.Add(current);
            return mergedRuns.Select(run => (run.StartX, run.EndX - run.StartX)).ToList();
        }

        private static bool IsTitleTextOrIconGap(
            PixelAccessor<Rgba32> accessor,
            Span<Rgba32> row,
            int y,
            int gapStart,
            int gapEnd,
            Rgba32 titleColor,
            int titleColorTolerance)
        {
            if (gapEnd <= gapStart)
            {
                return true;
            }

            int gapWidth = gapEnd - gapStart;
            int brightPixels = 0;
            int neighborTitlePixels = 0;
            int darkSeparatorPixels = 0;

            for (int x = gapStart; x < gapEnd; x++)
            {
                Rgba32 pixel = row[x];
                if (IsBrightTitleTextOrIcon(pixel))
                {
                    brightPixels++;
                }
                else if (IsDarkOrNeutralSeparator(pixel))
                {
                    darkSeparatorPixels++;
                }

                if (HasTitleColorNeighbor(accessor, x, y, titleColor, titleColorTolerance))
                {
                    neighborTitlePixels++;
                }
            }

            if (gapWidth <= 24 && neighborTitlePixels < gapWidth * 0.5)
            {
                return false;
            }

            if (gapWidth <= 24 && darkSeparatorPixels >= gapWidth * 0.75 && brightPixels == 0)
            {
                return false;
            }

            return brightPixels > 0
                || neighborTitlePixels >= gapWidth * 0.5;
        }

        private static bool IsTitleBarPixel(
            PixelAccessor<Rgba32> accessor,
            Span<Rgba32> row,
            int x,
            int y,
            Rgba32 titleColor,
            int titleColorTolerance)
        {
            Rgba32 pixel = row[x];
            if (IsTitleColor(pixel, titleColor, titleColorTolerance))
            {
                return true;
            }

            return IsBrightTitleTextOrIcon(pixel)
                && HasTitleColorNeighbor(accessor, x, y, titleColor, titleColorTolerance);
        }

        private static bool HasTitleColorNeighbor(PixelAccessor<Rgba32> accessor, int x, int y, Rgba32 titleColor, int titleColorTolerance)
        {
            int top = Math.Max(0, y - 3);
            int bottom = Math.Min(accessor.Height - 1, y + 3);

            for (int rowY = top; rowY <= bottom; rowY++)
            {
                if (rowY == y)
                {
                    continue;
                }

                if (IsTitleColor(accessor.GetRowSpan(rowY)[x], titleColor, titleColorTolerance))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsBrightTitleTextOrIcon(Rgba32 pixel)
        {
            return pixel.R >= 180 && pixel.G >= 180 && pixel.B >= 180;
        }

        private static bool IsDarkOrNeutralSeparator(Rgba32 pixel)
        {
            int maxChannel = Math.Max(pixel.R, Math.Max(pixel.G, pixel.B));
            int minChannel = Math.Min(pixel.R, Math.Min(pixel.G, pixel.B));

            return maxChannel <= 120 || maxChannel - minChannel <= 24;
        }

        private static bool IsInsideTitleBar(PanelTitleBar existing, int x, int y)
        {
            return x >= existing.X
                && x < existing.X + existing.Width
                && y >= existing.Y
                && y < existing.Y + existing.TitleHeight;
        }

        private static bool IsDuplicateTitleBar(PanelTitleBar existing, int x, int y, int width)
        {
            if (Math.Abs(existing.Y - y) > 16)
            {
                return false;
            }

            int overlapStart = Math.Max(existing.X, x);
            int overlapEnd = Math.Min(existing.X + existing.Width, x + width);
            int overlap = Math.Max(0, overlapEnd - overlapStart);
            int smallerWidth = Math.Min(existing.Width, width);

            return overlap >= smallerWidth * 0.5
                || (x >= existing.X && x <= existing.X + existing.Width);
        }

        private static PanelBounds GetPanelBounds(Image<Rgba32> image, List<PanelTitleBar> titleBars, PanelTitleBar target, Rgba32 titleColor, int titleColorTolerance)
        {
            int panelLeft = FindPanelLeftEdge(image, target, titleColor, titleColorTolerance);
            int panelBottom = FindPanelBottom(image, titleBars, target, titleColor, titleColorTolerance);
            int panelRight = FindPanelRightEdge(image, target, titleColor, titleColorTolerance);
            Rectangle bounds = new(
                panelLeft,
                target.Y,
                Math.Min(panelRight - panelLeft, image.Width - panelLeft),
                Math.Max(target.TitleHeight, panelBottom - target.Y));

            return new PanelBounds(target, bounds);
        }

        private static int FindPanelLeftEdge(Image<Rgba32> image, PanelTitleBar target, Rgba32 titleColor, int titleColorTolerance)
        {
            if (target.X < 80)
            {
                return 0;
            }

            int searchLeft = Math.Max(0, target.X - 80);

            for (int x = target.X; x >= searchLeft; x--)
            {
                if (ColumnHasTitleBorder(image, x, target.Y, Math.Min(image.Height - 1, target.Y + target.TitleHeight), titleColor, titleColorTolerance))
                {
                    searchLeft = x;
                }
            }

            return searchLeft;
        }

        private static int FindPanelBottom(Image<Rgba32> image, List<PanelTitleBar> titleBars, PanelTitleBar target, Rgba32 titleColor, int titleColorTolerance)
        {
            int bottomBorder = FindHorizontalBorder(image, target, titleColor, titleColorTolerance);
            if (bottomBorder > target.Y)
            {
                return bottomBorder + 1;
            }

            PanelTitleBar? nextPanelBelow = titleBars
                .Where(candidate => candidate.Y > target.Y + target.TitleHeight + 40)
                .Where(candidate => HorizontalOverlap(candidate, target) >= Math.Min(candidate.Width, target.Width) * 0.25)
                .OrderBy(candidate => candidate.Y)
                .FirstOrDefault();

            if (nextPanelBelow is not null)
            {
                return nextPanelBelow.Y;
            }

            return image.Height;
        }

        private static int FindPanelRightEdge(Image<Rgba32> image, PanelTitleBar target, Rgba32 titleColor, int titleColorTolerance)
        {
            int searchRight = Math.Min(image.Width - 1, target.X + Math.Max(target.Width + 24, 120));
            int titleMidY = target.Y + (target.TitleHeight / 2);

            for (int x = searchRight; x > target.X + 40; x--)
            {
                if (ColumnHasTitleBorder(image, x, target.Y, Math.Min(image.Height - 1, target.Y + target.TitleHeight + 80), titleColor, titleColorTolerance)
                    || IsTitleColor(image[x, titleMidY], titleColor, titleColorTolerance))
                {
                    return Math.Min(image.Width, x + 1);
                }
            }

            return Math.Min(image.Width, target.X + target.Width);
        }

        private static int FindHorizontalBorder(Image<Rgba32> image, PanelTitleBar target, Rgba32 titleColor, int titleColorTolerance)
        {
            int startY = target.Y + target.TitleHeight + 40;
            int right = Math.Min(image.Width, target.X + target.Width);

            for (int y = startY; y < image.Height; y++)
            {
                int matchingPixels = 0;
                int totalPixels = 0;
                for (int x = target.X; x < right; x++)
                {
                    totalPixels++;
                    if (IsTitleColor(image[x, y], titleColor, titleColorTolerance))
                    {
                        matchingPixels++;
                    }
                }

                if (totalPixels > 0 && matchingPixels >= totalPixels * 0.55)
                {
                    return y;
                }
            }

            return -1;
        }

        private static bool ColumnHasTitleBorder(Image<Rgba32> image, int x, int startY, int endY, Rgba32 titleColor, int titleColorTolerance)
        {
            int matchingPixels = 0;
            int totalPixels = 0;

            for (int y = startY; y <= endY; y++)
            {
                totalPixels++;
                if (IsTitleColor(image[x, y], titleColor, titleColorTolerance))
                {
                    matchingPixels++;
                }
            }

            return totalPixels > 0 && matchingPixels >= totalPixels * 0.6;
        }

        private static int HorizontalOverlap(PanelTitleBar left, PanelTitleBar right)
        {
            int overlapStart = Math.Max(left.X, right.X);
            int overlapEnd = Math.Min(left.X + left.Width, right.X + right.Width);
            return Math.Max(0, overlapEnd - overlapStart);
        }

        private static int MeasureTitleBarHeight(PixelAccessor<Rgba32> accessor, int x, int y, int width, Rgba32 titleColor, int titleColorTolerance)
        {
            int maxHeight = Math.Min(32, accessor.Height - y);
            int height = 0;

            for (int offsetY = 0; offsetY < maxHeight; offsetY++)
            {
                double coverage = MeasureTitleColorCoverage(accessor, x, y + offsetY, width, 1, titleColor, titleColorTolerance);
                if (coverage < 0.35 && height >= 12)
                {
                    break;
                }

                height++;
            }

            return height;
        }

        private static double MeasureTitleColorCoverage(PixelAccessor<Rgba32> accessor, int x, int y, int width, int height, Rgba32 titleColor, int titleColorTolerance)
        {
            int titleColorPixels = 0;
            int totalPixels = 0;
            int right = Math.Min(accessor.Width, x + width);
            int bottom = Math.Min(accessor.Height, y + height);

            for (int rowY = y; rowY < bottom; rowY++)
            {
                Span<Rgba32> row = accessor.GetRowSpan(rowY);
                for (int rowX = x; rowX < right; rowX++)
                {
                    totalPixels++;
                    if (IsTitleColor(row[rowX], titleColor, titleColorTolerance))
                    {
                        titleColorPixels++;
                    }
                }
            }

            return totalPixels == 0 ? 0 : (double)titleColorPixels / totalPixels;
        }

        private static bool IsTitleColor(Rgba32 pixel, Rgba32 titleColor, int titleColorTolerance)
        {
            int tolerance = Math.Clamp(titleColorTolerance, 0, byte.MaxValue);
            return Math.Abs(pixel.R - titleColor.R) <= tolerance
                && Math.Abs(pixel.G - titleColor.G) <= tolerance
                && Math.Abs(pixel.B - titleColor.B) <= tolerance;
        }

        private static Rgba32 ParseColor(string color)
        {
            string hex = color.Trim().TrimStart('#');
            if (hex.Length != 6)
            {
                throw new FormatException($"Expected TitleColor to be a 6-digit hex color, but got '{color}'.");
            }

            return new Rgba32(
                Convert.ToByte(hex[..2], 16),
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16));
        }
    }

    public sealed record SignalBitmap(
        int PageNumber,
        int ImageNumber,
        int Width,
        int Height,
        PdfRectangle Bounds,
        byte[] LuminancePixels);

    internal sealed record PanelTitleBar(int X, int Y, int Width, int TitleHeight);

    internal sealed record PanelBounds(PanelTitleBar TitleBar, Rectangle Bounds);
}
