using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using UglyToad.PdfPig.Content;
using static System.Net.Mime.MediaTypeNames;

namespace SignalVision
{
    public class CaseSummaryImage : IDisposable
    {
        public IPdfImage PDFImage { get; }
        public Image<Rgba32>? Image { get; }
        public Image<Rgba32> BlurredImage { get; }
        public Image<Rgba32> VerticalBlurredImage { get; }
        public int Width => Image?.Width ?? PDFImage.WidthInSamples;
        public int Height => Image?.Height ?? PDFImage.HeightInSamples;
        public List<WindowsPanel> WindowsPanels { get; } = [];
        public CaseSummaryPage Parent;
        public int ImageIndex { get; }
        public Config Config => Parent.Config;
        public Logger Logger { get;  }

        public CaseSummaryImage(IPdfImage image, int imageIndex, CaseSummaryPage parent)
        {
            PDFImage = image;
            Parent = parent;
            ImageIndex = imageIndex;
            Image = ToImage(PDFImage) ?? throw new Exception("Failed to load image from PDF.");
            BlurredImage = Image.Clone();
            BlurredImage.Mutate(ctx => ctx.GaussianBlur(Config.WindowsPanelBlurRadius));

            VerticalBlurredImage = CreateVerticalBlurredImage(Image, Config.WindowsPanelBlurRadius);


            Logger = Parent.Logger.WithTag($"CaseSummaryImage: Page {Parent.PageNumber} Image {ImageIndex}");
            Process();
            //Save the image to a file in the output folder where the file name is "image_{PageNumber}_{ImageIndex}.png"
            Save(Path.Combine(Parent.Parent.OutputFolder, $"image_{Parent.PageNumber}_{ImageIndex}.png"));
        }

        public void Dispose()
        {
            Image?.Dispose();
            BlurredImage.Dispose();
            VerticalBlurredImage.Dispose();
        }

        private static Image<Rgba32> CreateVerticalBlurredImage(Image<Rgba32> source, float sigma)
        {
            Image<Rgba32> result = source.Clone();
            int radius = (int)MathF.Ceiling(sigma * 3);
            float[] weights = new float[(radius * 2) + 1];
            float weightSum = 0;

            for (int offset = -radius; offset <= radius; offset++)
            {
                float weight = MathF.Exp(-(offset * offset) / (2 * sigma * sigma));
                weights[offset + radius] = weight;
                weightSum += weight;
            }

            for (int index = 0; index < weights.Length; index++)
            {
                weights[index] /= weightSum;
            }

            source.ProcessPixelRows(result, (sourceAccessor, resultAccessor) =>
            {
                for (int y = 0; y < sourceAccessor.Height; y++)
                {
                    Span<Rgba32> resultRow = resultAccessor.GetRowSpan(y);

                    for (int x = 0; x < resultRow.Length; x++)
                    {
                        float r = 0;
                        float g = 0;
                        float b = 0;
                        float a = 0;

                        for (int offset = -radius; offset <= radius; offset++)
                        {
                            int sourceY = Math.Clamp(y + offset, 0, sourceAccessor.Height - 1);
                            Rgba32 pixel = sourceAccessor.GetRowSpan(sourceY)[x];
                            float weight = weights[offset + radius];
                            r += pixel.R * weight;
                            g += pixel.G * weight;
                            b += pixel.B * weight;
                            a += pixel.A * weight;
                        }

                        resultRow[x] = new Rgba32(
                            (byte)Math.Clamp(MathF.Round(r), byte.MinValue, byte.MaxValue),
                            (byte)Math.Clamp(MathF.Round(g), byte.MinValue, byte.MaxValue),
                            (byte)Math.Clamp(MathF.Round(b), byte.MinValue, byte.MaxValue),
                            (byte)Math.Clamp(MathF.Round(a), byte.MinValue, byte.MaxValue));
                    }
                }
            });

            return result;
        }

        public CaseSummaryImage Save(string filename)
        {
            Image?.Save(filename);
            return this;
        }

        private void Process()
        {
            if (Image == null) return;
            int index = 0;
            List<WindowsPanel> titles = [];
            //find titles
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    Rgba32 color = BlurredImage[x, y];
                    bool activeTitleBarPixel = IsActiveTitleBarPixel(color, Config);
                    bool inactiveTitleBarPixel = IsInActiveTitleBarPixel(color, Config);
                    if (activeTitleBarPixel || inactiveTitleBarPixel)
                    {
                        List<WindowsPanel> inPanels = WindowsPanels.FindAll(p => p.InsideTitle(x, y));
                        if (inPanels.Count > 0)
                        {
                            continue;
                        }

                        List<WindowsPanel> panels = WindowsPanels.FindAll(p => p.NextToTitle(x, y));
                        if (panels.Count == 1)
                        {
                            panels[0].IncludeTitle(x, y);
                            continue;
                        }
                        else if (panels.Count > 1)
                        {
                            WindowsPanel p = panels[0].UnionTitle(panels);
                            //remove all panels except the first one
                            WindowsPanels.RemoveAll(panels.Contains);
                            WindowsPanels.Add(p);
                            continue;
                        }
                        WindowsPanels.Add(new WindowsPanel(x, y, activeTitleBarPixel, index++, this));
                    }
                }
                foreach (WindowsPanel panel in WindowsPanels)
                {
                    panel.TrimTitle();
                }
            }

            //loop through all panels and remove any that are smaller than the minimum size
            WindowsPanels.RemoveAll(p => p.Title.Width < Config.WindowsPanelMinimumWidth || p.Title.Height < Config.WindowsPanelMinimumHeight);

            foreach (WindowsPanel panel in WindowsPanels)
            {
                //get the content of the panel which is between the title.
                int bottom = Image.Height;
                foreach(WindowsPanel other in WindowsPanels)
                {
                    if (other == panel) continue;
                    if (other.Title.Right < panel.Title.Left || other.Title.Left > panel.Title.Right) continue;
                    if (other.Title.Bottom < panel.Title.Top) continue;
                    bottom = Math.Min(bottom, other.Title.Top);
                }
                panel.Bounds = new Rectangle(panel.Title.Left, panel.Title.Top, panel.Title.Width, bottom - panel.Title.Top);

                string titleText = panel.GetTitleText();
                Logger.Debug($"Title: Page: {Parent.PageNumber} [{titleText.Trim()}]");//TODO:
                string foundTitle = panel.IsSimilarTitle(Config.TargetGraphTitles);
                //panel.SaveTitle($"c:/temp/title_{DateTime.Now.ToString("yyyyMMddHHmmssfff")}.png");//TODO:
                //panel.SaveTitle(Path.Combine(Parent.Parent.OutputFolder, $"title_page_{Parent.PageNumber}_image_{ImageIndex}_panel_{panel.Index}.png"));//TODO:
                if (!string.IsNullOrWhiteSpace(foundTitle))
                {
                    Logger.Debug($"Found Title: {titleText} / {foundTitle}");//TODO:
                    //panel.SaveBounds($"c:/temp/bounds_{DateTime.Now.ToString("yyyyMMddHHmmssfff")}.png");//TODO:
                    panel.SaveBounds(Path.Combine(Parent.Parent.OutputFolder, $"bounds_page_{Parent.PageNumber}_image_{ImageIndex}_panel_{panel.Index}.png"));//TODO:
                    panel.ExtractGraphs();
                }
            }
        }

        private Image<Rgba32>? ToImage(IPdfImage pdfImage)
        {
            byte[] bytes = [.. pdfImage.RawBytes];
            try
            {
                Image<Rgba32>? image = SixLabors.ImageSharp.Image.Load<Rgba32>(bytes);
                if (image != null && Config.JPEGVerticalFlip)
                    image.Mutate(context => context.Flip(SixLabors.ImageSharp.Processing.FlipMode.Vertical));
                return image;
            }
            catch
            {
            }
            pdfImage.TryGetPng(out bytes);
            return SixLabors.ImageSharp.Image.Load<Rgba32>(bytes);
        }

        static public bool IsInActiveTitleBarPixel(Rgba32 pixel, Config config)
        {
            List<Rgba32> inactiveTitleColors = config.WindowsPanelInactiveTitleColors;
            int tolerance = config.WindowsPanelTitleColorTolerance;
            foreach (var color in inactiveTitleColors)
            {
                bool flag = Math.Abs(pixel.R - color.R) <= tolerance
                && Math.Abs(pixel.G - color.G) <= tolerance
                && Math.Abs(pixel.B - color.B) <= tolerance;
                if (flag) return true;
            }
            return false;
        }

        static public bool IsGraphTitleBarPixel(Rgba32 pixel, Config config)
        {
            List<Rgba32> colors = config.WindowsPanelGraphTitleColor;
            int tolerance = config.WindowsPanelTitleColorTolerance;
            foreach (var color in colors)
            {
                bool flag = Math.Abs(pixel.R - color.R) <= tolerance
                && Math.Abs(pixel.G - color.G) <= tolerance
                && Math.Abs(pixel.B - color.B) <= tolerance;
                if (flag) return true;
            }
            return false;
        }

        static public bool IsGraphSeparatorPixel(Rgba32 pixel, Config config)
        {
            int tolerance = config.WindowsPanelTitleColorTolerance;
            foreach (Rgba32 color in config.WindowsPanelGraphSeparatorColor)
            {
                if (Math.Abs(pixel.R - color.R) <= tolerance &&
                    Math.Abs(pixel.G - color.G) <= tolerance &&
                    Math.Abs(pixel.B - color.B) <= tolerance)
                {
                    return true;
                }
            }

            return false;
        }

        static public bool IsActiveTitleBarPixel(Rgba32 pixel, Config config)
        {
            List<Rgba32> activeTitleColors = config.WindowsPanelActiveTitleColors;
            int tolerance = config.WindowsPanelTitleColorTolerance;
            foreach (var color in activeTitleColors)
            {
                bool flag = Math.Abs(pixel.R - color.R) <= tolerance
                && Math.Abs(pixel.G - color.G) <= tolerance
                && Math.Abs(pixel.B - color.B) <= tolerance;
                if (flag) return true;
            }
            return false;
        }

        /*
        byte[] RawBytes = PDFImage.RawBytes.ToArray();
        if (TryLoadImage(RawBytes, out Image<Rgba32>? loadedImage) && loadedImage is not null)
        {
            if (IsJpeg(RawBytes))
            {
                loadedImage.Mutate(context => context.Flip(SixLabors.ImageSharp.Processing.FlipMode.Vertical));
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
        }

        List<WindowsPanel> panels = GetPanels(Image, 0, 0, Width, Height);//TODO:
        Console.WriteLine($"Total panels: {panels.Count}");
        foreach (WindowsPanel panel in panels)
        {
            Console.WriteLine($"Titlebar: {panel.GetTitleBar()}");
            Console.WriteLine($"Whole Content: {panel.GetWholeContent(panels, Height)}");
            panel.SaveWholePanel($"c:/temp/whole_{DateTime.Now.ToString("yyyyMMddHHmmssfff")}.png", panels, Height);
            panel.SaveAsCSV($"c:/temp/{DateTime.Now.ToString("yyyyMMddHHmmssfff")}.csv", panels, Height);

            panel.GetGraphContent(panels, Height);
        }*/
    }
        /*
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
        }*/
}
