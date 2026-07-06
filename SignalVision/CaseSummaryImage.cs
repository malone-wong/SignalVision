using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using UglyToad.PdfPig.Content;

namespace SignalVision
{
    public class CaseSummaryImage : IDisposable
    {
        public static readonly int[] ActiveTitleR = [0];
        public static readonly int[] ActiveTitleG = [86];
        public static readonly int[] ActiveTitleB = [153];
        public static readonly int[] InactiveTitleR = [0];
        public static readonly int[] InactiveTitleG = [114];
        public static readonly int[] InactiveTitleB = [197];
        public const int Tolerance = 30;

        public IPdfImage PDFImage { get; }
        public Config Config { get; }
        public byte[] RawBytes { get; }
        public byte[]? EncodedBytes { get; }
        public Image<Rgba32>? Image { get; }
        public byte[]? LuminancePixels { get; }
        public int Width => Image?.Width ?? PDFImage.WidthInSamples;
        public int Height => Image?.Height ?? PDFImage.HeightInSamples;
        public List<WindowsPanel> WindowsPanels { get; }
        public string ImageType { get; }

        public CaseSummaryImage(IPdfImage image, Config config)
        {
            PDFImage = image;
            Config = config;
            RawBytes = image.RawBytes.ToArray();
            WindowsPanels = [];

            if (TryLoadImage(RawBytes, out Image<Rgba32>? loadedImage) && loadedImage is not null)
            {
                if (IsJpeg(RawBytes))
                {
                    loadedImage.Mutate(context => context.Flip(SixLabors.ImageSharp.Processing.FlipMode.Vertical));
                    ImageType = "JPEG";
                }

                EncodedBytes = RawBytes;
                Image = loadedImage;
                LuminancePixels = ExtractLuminancePixels(loadedImage);
            }
            else if (image.TryGetPng(out byte[]? pngBytes) && TryLoadImage(pngBytes, out loadedImage) && loadedImage is not null)
            {
                EncodedBytes = pngBytes;
                Image = loadedImage;
                LuminancePixels = ExtractLuminancePixels(loadedImage);
                ImageType = "PNG";
            }

            List<WindowsPanel> panels = GetPanels(Image, 0, 0, Width, Height);//TODO:
            Console.WriteLine($"Total panels: {panels.Count}");
            foreach(WindowsPanel panel in panels)
            {
                Console.WriteLine($"Titlebar: {panel.GetTitleBar()}");
                Console.WriteLine($"Whole Content: {panel.GetWholeContent(panels, Height)}");
                panel.SaveWholePanel($"c:/temp/whole_{DateTime.Now.ToString("yyyyMMddHHmmssfff")}.png", panels, Height);
                panel.SaveAsCSV($"c:/temp/{DateTime.Now.ToString("yyyyMMddHHmmssfff")}.csv", panels, Height);

                panel.GetGraphContent(panels, Height);
            }
        }

        public void Dispose()
        {
            Image?.Dispose();
        }

        private static List<WindowsPanel> Merge(List<WindowsPanel> master, List<WindowsPanel> mergeItems)
        {
            WindowsPanel first = mergeItems[0];
            List<WindowsPanel> rest = mergeItems.GetRange(1, mergeItems.Count - 1);
            first.Union(rest);
            return master.Except(rest).ToList();
        }

        private static List<WindowsPanel> GetPanels(Image<Rgba32>? image, int startX, int startY, int endX, int endY)
        {
            Console.WriteLine($"Search Image: [{startX}, {startY}] [{endX}, {endY}]");
            if (image == null) return [];
            Image<Rgba32> image2 = image.Clone();
            image2.Mutate(ctx => ctx.GaussianBlur(1f));
            image2.SaveAsPng($"c:/temp/blur_{DateTime.Now.ToString("yyyyMMddHHmmssfff")}.png");//TODO:

            List<WindowsPanel> panels = [];
            for(int y = startY; y < endY; y++)
            {
                for(int x = startX; x < endX; x++)
                {
                    Rgba32 color = image2[x, y];
                    bool activeTitleBarPixel = IsActiveTitleBarPixel(color);
                    bool inactiveTitleBarPixel = IsInActiveTitleBarPixel(color);

                    if (x == 987 && y == 97) {
                        Console.WriteLine("Test");
                    }

                    if (activeTitleBarPixel || inactiveTitleBarPixel)
                    {
                        List<WindowsPanel> inPanels = panels.FindAll(p => p.IsPixelInPanel(x, y));
                        if (inPanels.Count == 1) continue;
                        if (inPanels.Count > 1)
                        {
                            panels = Merge(panels, inPanels);
                            continue;
                        }

                        List<WindowsPanel> inNextPanels = panels.FindAll(p => p.IsPixelNextToPanel(x, y));
                        if (inNextPanels.Count == 1)
                        {
                            inNextPanels[0].ExpandToIncludePixel(x, y);
                            continue;
                        }
                        if (inNextPanels.Count > 1)
                        {
                            panels = Merge(panels, inNextPanels);
                            continue;
                        }

                        panels.Add(new WindowsPanel(x, y, activeTitleBarPixel, image));
                    }
                }
            }

            panels= panels.Where(panel => panel.Bounds.Width >= WindowsPanel.MinimumPanelWidthSize && panel.Bounds.Height >= WindowsPanel.MinimumPanelHeightSize).ToList();

            Console.WriteLine($"Panels Children Count: {panels.Count}");
            //DEBUG
            foreach (WindowsPanel panel in panels)
            {
                //save panel as an image
                panel.Save($"c:/temp/{DateTime.Now.ToString("yyyyMMddHHmmssfff")}.png", image, panel.Bounds);

                Console.WriteLine($"Children panel: [{panel.Bounds.Left}, {panel.Bounds.Top}] [{panel.Bounds.Right}, {panel.Bounds.Bottom}]");
            }
            //End of Debug

            List<WindowsPanel> totalChildren = [];
            foreach (WindowsPanel panel in panels)
            {
                Rectangle content = panel.GetContentBound();
                if (content.Height == panel.Bounds.Height) continue;
                int titleHeight = Math.Abs(content.Top - panel.Bounds.Top);
                if (titleHeight < 10 || titleHeight > 40) continue;
                Console.WriteLine($"Title Height: {titleHeight}");
                List<WindowsPanel> children = GetPanels(image, content.Left, content.Top, content.Right, content.Bottom);
                totalChildren.AddRange(children);
            }
            panels.AddRange(totalChildren);

            return panels;
        }

        static public bool IsInActiveTitleBarPixel(Rgba32 pixel)
        {
            for(int i = 0; i < InactiveTitleR.Length; i++) {
                bool flag = Math.Abs(pixel.R - InactiveTitleR[i]) <= Tolerance
                && Math.Abs(pixel.G - InactiveTitleG[i]) <= Tolerance
                && Math.Abs(pixel.B - InactiveTitleB[i]) <= Tolerance;
                if (flag) return true;
            }
            return false;
        }

        static public bool IsActiveTitleBarPixel(Rgba32 pixel)
        {
            for (int i = 0; i < ActiveTitleR.Length; i++)
            {
                bool flag = Math.Abs(pixel.R - ActiveTitleR[i]) <= Tolerance
                && Math.Abs(pixel.G - ActiveTitleG[i]) <= Tolerance
                && Math.Abs(pixel.B - ActiveTitleB[i]) <= Tolerance;
                if (flag) return true;
            }
            return false;
        }
        

        private static bool TryLoadImage(byte[] imageBytes, out Image<Rgba32>? image)
        {
            try
            {
                image = SixLabors.ImageSharp.Image.Load<Rgba32>(imageBytes);
                return true;
            }
            catch
            {
                image = null;
                return false;
            }
        }

        private static byte[] ExtractLuminancePixels(Image<Rgba32> image)
        {
            byte[] luminancePixels = new byte[image.Width * image.Height];
            image.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height; y++)
                {
                    Span<Rgba32> row = accessor.GetRowSpan(y);
                    int rowOffset = y * accessor.Width;

                    for (int x = 0; x < row.Length; x++)
                    {
                        luminancePixels[rowOffset + x] = ToLuminance(row[x]);
                    }
                }
            });

            return luminancePixels;
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
    }
}
