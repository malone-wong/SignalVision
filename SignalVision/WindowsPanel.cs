using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Reflection.Metadata.Ecma335;
using static System.Net.Mime.MediaTypeNames;

namespace SignalVision
{
    public class WindowsPanel
    {
        public Rectangle Bounds { get; set; }
        public Rectangle Title { get; set; }
        public bool IsActive { get; }
        public CaseSummaryImage Parent { get; }

        public Logger Logger { get; set; }

        public int Index { get; }

        public Image<Rgba32>? Image => Parent.Image;
        public Image<Rgba32>? BlurredImage => Parent.BlurredImage;
        public Image<Rgba32>? VerticalBurredImage => Parent.VerticalBlurredImage;

        public List<Graph> Graphs { get; } = [];

        public WindowsPanel(int titlex, int titley, bool isActive, int index, CaseSummaryImage parent)
        {
            Title = new Rectangle(titlex, titley, 1, 1);
            IsActive = isActive;
            Parent = parent;
            Index = index;
            Logger = Parent.Logger.WithTag($"WindowsPanel: titlex {titlex}");
        }

        public bool InsideTitle(int x, int y)
        {
            return Title.Contains(x, y);
        }

        public bool NextToTitle(int x, int y)
        {
            Rectangle expanded = Rectangle.Inflate(Title, 1, 1);
            return expanded.Contains(x, y) && !Title.Contains(x, y);
        }

        public bool IncludeTitle(int x, int y)
        {
            if (NextToTitle(x, y))
            {
                Title = Rectangle.Union(Title, new Rectangle(x, y, 1, 1));
                return true;
            }
            return false;
        }

        public WindowsPanel UnionTitle(List<WindowsPanel> others)
        {
            foreach (var other in others)
            {
                Title = Rectangle.Union(Title, other.Title);
            }
            return this;
        }

        public void TrimTitle()
        {
            var image = Parent.BlurredImage;
            var config = Parent.Config;

            if (image is null || Title.Width <= 0 || Title.Height <= 0)
                return;

            var bounds = Rectangle.Intersect(
                Title,
                new Rectangle(0, 0, image.Width, image.Height));

            if (bounds.IsEmpty)
            {
                Title = Rectangle.Empty;
                return;
            }

            bool IsTitleRow(int y)
            {
                int count = 0;

                for (int x = bounds.Left; x < bounds.Right; x++)
                {
                    var pixel = image[x, y];

                    if (CaseSummaryImage.IsActiveTitleBarPixel(pixel, config) ||
                        CaseSummaryImage.IsInActiveTitleBarPixel(pixel, config))
                    {
                        count++;
                    }
                }

                return count >= bounds.Width * config.WindowsPanelTitleDensity;
            }

            int top = bounds.Top;
            int bottom = bounds.Bottom;

            while (top < bottom && !IsTitleRow(top))
                top++;

            while (bottom > top && !IsTitleRow(bottom - 1))
                bottom--;

            Title = Rectangle.FromLTRB(
                bounds.Left,
                top,
                bounds.Right,
                bottom);
        }

        public string GetTitleText()
        {
            using var croppedImage = GetTitleImage();
            if (croppedImage is null)
                return string.Empty;

            // Get the title using the OCR provider selected in configuration.
            return OCRHelper.ExtractTextFromImage(
                croppedImage,
                Parent.Config,
                Parent.Parent.Logger.WithTag("OCRHelper"));
        }

        public Image<Rgba32>? GetTitleImage()
        {
            var image = Parent.Image;
            if (image is null || Title.Width <= 0 || Title.Height <= 0)
                return null;
            var bounds = Rectangle.Intersect(
                Title,
                new Rectangle(0, 0, image.Width, image.Height));
            if (bounds.IsEmpty)
                return null;
            return image.Clone(ctx => ctx.Crop(bounds));
        }

        public string IsSimilarTitle(List<string> titles)
        {
            string thisTitle = GetTitleText();
            //remove all spaces and newlines from thisTitle
            thisTitle = thisTitle.Replace(" ", "").Replace("\n", "").Replace("\r", "").Replace("\t", "");
            foreach (var title in titles)
            {
                string tmp = title.Replace(" ", "").Replace("\n", "").Replace("\r", "").Replace("\t", "");
                Logger.Debug($"Matching Panel Title: {thisTitle} Config: {tmp}");
                if (thisTitle.Contains(tmp, StringComparison.OrdinalIgnoreCase))
                    return title;
            }
            return string.Empty;
        }

        public void SaveTitle(string path)
        {
            using var croppedImage = GetTitleImage();
            croppedImage?.SaveAsPng(path);
        }

        public void SaveBounds(string path)
        {
            var image = Parent.Image;
            if (image is null || Bounds.Width <= 0 || Bounds.Height <= 0)
                return;
            var bounds = Rectangle.Intersect(
                Bounds,
                new Rectangle(0, 0, image.Width, image.Height));
            if (bounds.IsEmpty)
                return;
            using var croppedImage = image.Clone(ctx => ctx.Crop(bounds));
            croppedImage.SaveAsPng(path);
        }

        public void ExtractGraphs()
        {
            Graphs.Clear();

            Image<Rgba32>? image = Parent.Image;
            if (image is null || Bounds.Width <= 0 || Bounds.Height <= 0)
                return;

            Rectangle panelBounds = Rectangle.Intersect(
                Bounds,
                new Rectangle(0, 0, image.Width, image.Height));
            if (panelBounds.IsEmpty)
                return;

            int searchTop = Math.Max(panelBounds.Top, Title.Bottom);
            int separatorHeight = panelBounds.Bottom - searchTop;
            int minimumSeparatorWidth = Parent.Config.WindowsPanelGraphSeparatorMinWidth;
            if (separatorHeight <= 0 || minimumSeparatorWidth <= 0)
                return;

            // A separator is vertical, so separator-colored pixels must occupy
            // at least half of its column. This rejects short grid/waveform marks.
            bool IsSeparatorColumn(int column)
            {
                int count = 0;
                for (int y = searchTop; y < panelBounds.Bottom; y++)
                {
                    if (CaseSummaryImage.IsGraphSeparatorPixel(image[column, y], Parent.Config))
                        count++;
                }

                return count >= separatorHeight * 0.5f;
            }

            List<(int Left, int Right)> separators = [];
            int cursor = panelBounds.Left;
            while (cursor < panelBounds.Right)
            {
                while (cursor < panelBounds.Right && !IsSeparatorColumn(cursor))
                    cursor++;

                int separatorLeft = cursor;
                while (cursor < panelBounds.Right && IsSeparatorColumn(cursor))
                    cursor++;

                if (cursor - separatorLeft >= minimumSeparatorWidth)
                    separators.Add((separatorLeft, cursor));
            }

            // Separator pixels do not belong to either neighboring graph.
            List<(int Left, int Right)> graphAreas = [];
            int graphLeft = panelBounds.Left;
            foreach ((int separatorLeft, int separatorRight) in separators)
            {
                if (separatorLeft > graphLeft)
                    graphAreas.Add((graphLeft, separatorLeft));
                graphLeft = separatorRight;
            }

            if (graphLeft < panelBounds.Right)
                graphAreas.Add((graphLeft, panelBounds.Right));

            if (graphAreas.Count == 0)
                return;

            // Locate the light graph-label strip independently of the vertical
            // separators. Its clearest row is also the top of each graph area.
            int headerSearchBottom = Math.Min(
                panelBounds.Bottom,
                searchTop + Math.Max(40, panelBounds.Height / 8));
            int headerTop = -1;
            int mostTitlePixels = 0;
            for (int y = searchTop; y < headerSearchBottom; y++)
            {
                int count = 0;
                for (int x = panelBounds.Left; x < panelBounds.Right; x++)
                {
                    if (CaseSummaryImage.IsGraphTitleBarPixel(image[x, y], Parent.Config))
                        count++;
                }

                if (count > mostTitlePixels)
                {
                    mostTitlePixels = count;
                    headerTop = y;
                }
            }

            if (headerTop < 0 || mostTitlePixels < panelBounds.Width / 2)
                return;

            int i = 0;
            foreach ((int left, int right) in graphAreas)
            {
                Rectangle titleBounds = Rectangle.FromLTRB(
                    left,
                    headerTop,
                    right,
                    Math.Min(panelBounds.Bottom, headerTop + 18));

                string title;
                using (Image<Rgba32> titleImage = image.Clone(ctx => ctx.Crop(titleBounds)))
                using (Image<Rgba32> ocrTitleImage = PrepareGraphTitleForOcr(titleImage))
                {
                    title = OCRHelper.ExtractTextFromImage(
                        ocrTitleImage,
                        Parent.Config,
                        Logger.WithTag("Graph title"));
                    //ocrTitleImage.SaveAsPng(Path.Combine(Parent.Parent.Parent.OutputFolder, $"graphtitle_{Parent.Parent.PageNumber}_{Parent.ImageIndex}_{titleImage.GetHashCode()}.png"));//TODO:
                }

                title = title.Replace("\r", "").Replace("\n", "").Trim();
                string normalizedTitle = new(title.Where(char.IsLetterOrDigit).ToArray());
                if (string.Equals(normalizedTitle, "Labels", StringComparison.OrdinalIgnoreCase))
                    continue;

                Logger.Info($"Graph title: {title}");
                Graphs.Add(new Graph(i++,
                    title,
                    Rectangle.FromLTRB(left, headerTop, right, panelBounds.Bottom),
                    this
                ));
            }

            foreach (Graph graph in Graphs)
            {
                //TODO: save the graph image to the folder
                {
                    var bounds = Rectangle.Intersect(
                        graph.Bounds,
                        new Rectangle(0, 0, image.Width, image.Height));
                    using var croppedImage = image.Clone(ctx => ctx.Crop(bounds));
                    croppedImage.SaveAsPng(Path.Combine(Parent.Parent.Parent.OutputFolder, $"graph_{Parent.Parent.PageNumber}_{Parent.ImageIndex}_{graph.GetHashCode()}.png"));
                }
            }
        }

        private Image<Rgba32> PrepareGraphTitleForOcr(Image<Rgba32> source)
        {
            // MicrosoftOCRHelper already performs scaling and padding. Paddle
            // receives its input unchanged, so prepare only that provider here.
            if (Parent.Config.OCRProvider != OcrProvider.Paddle)
                return source.Clone();

            const int preparedPadding = 20;
            int configuredScale = Math.Max(1, Parent.Config.OCRScale);
            int maximumContentSize = Math.Max(
                1,
                Parent.Config.OCRMaxPreparedDimension - (preparedPadding * 2));
            double scale = Math.Min(
                configuredScale,
                (double)maximumContentSize / Math.Max(source.Width, source.Height));

            Image<Rgba32> prepared = source.Clone();
            if (scale > 1.0)
            {
                prepared.Mutate(context => context.Resize(
                    Math.Max(1, (int)Math.Round(source.Width * scale)),
                    Math.Max(1, (int)Math.Round(source.Height * scale)),
                    KnownResamplers.Lanczos3));
            }

            // Sample the title strip away from its borders for a clean padding color.
            Rgba32 backgroundPixel = source[
                source.Width / 2,
                Math.Min(source.Height - 1, Math.Max(0, source.Height / 4))];
            Color background = Color.FromPixel(backgroundPixel);
            prepared.Mutate(context => context
                .Pad(
                    prepared.Width + (preparedPadding * 2),
                    prepared.Height + (preparedPadding * 2),
                    background)
                .Grayscale()
                .Contrast(1.4f)
                .GaussianSharpen(0.6f));

            return prepared;
        }

        /*public WindowsPanel Union(List<WindowsPanel> panels)
        {
            foreach (var panel in panels)
            {
                Bounds = Rectangle.Union(Bounds, panel.Bounds);
            }
            return this;
        }

        public Rectangle GetWholeContent(List<WindowsPanel> allPanels, int maxBottom)
        {
            var thisTitlebar = this.GetTitleBar();
            int top=Bounds.Top;
            int bottom= thisTitlebar.Bottom;
            int left=Bounds.Left;
            int right=Bounds.Right;
            foreach (var panel in allPanels)
            {
                if (panel == this) continue;
                var titlebar = panel.GetTitleBar();
                if (titlebar.Bottom < thisTitlebar.Top) continue;
                if (titlebar.Right<=thisTitlebar.Left) continue;
                if (titlebar.Left >= thisTitlebar.Right) continue;
                if (titlebar.Top <= bottom) continue;
                bottom = titlebar.Top - 1;
            }
            if (bottom == thisTitlebar.Bottom) bottom = maxBottom;
            return new Rectangle(left, top, right - left, bottom - top);
        }

        public Rectangle GetTitleBar()
        {
            bool IsBorder(Rgba32 pixel) =>
                CaseSummaryImage.IsActiveTitleBarPixel(pixel) || CaseSummaryImage.IsInActiveTitleBarPixel(pixel);
            int top = Bounds.Top;
            int bottom = Bounds.Top;
            int left = Bounds.Left;
            int right = Bounds.Right;

            Image.ProcessPixelRows(accessor =>
            {
                for (int y = top; y < Bounds.Bottom; y++)
                {
                    int count = 0;
                    Span<Rgba32> row = accessor.GetRowSpan(y);
                    for (int x = left; x < right; x++)
                    {
                        if (IsBorder(row[x])) count++;
                    }

                    if (count >= (right - left) * TitleBarDensity) bottom=y;
                    else break;
                }
            });

            // Construct the final rectangle from the shrunken edges
            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        static public float GetColorPixelRateHorizontal(int startx, int endx, int y, Image<Rgba32> image, Rgba32 color, int tolerance)
        {
            int total = 0;
            int colorCount = 0;
            for (int x = startx; x < endx; x++)
            {
                Rgba32 pixel = image[x, y];
                if (Math.Abs(pixel.R - color.R) <= tolerance &&
                    Math.Abs(pixel.G - color.G) <= tolerance &&
                    Math.Abs(pixel.B - color.B) <= tolerance)
                {
                    colorCount++;
                }
                total++;
            }
            return (float)colorCount / total;
        }

        public Graph GetGraphContent(List<WindowsPanel> allPanels, int maxBottom)
        {
            Graph graph = new ();
            Rectangle content = GetWholeContent(allPanels, maxBottom);
            using Image<Rgba32> blurredImage = Image.Clone(ctx => ctx.GaussianBlur(1f));
            int totalWidth = 0;
            int blackWidth = 0;

            int startY = content.Top;
            int endY = content.Bottom;

            //look for the start y and end y for the horizonal line >0.5 are black pixels
            {
                for (int y = content.Top; y < endY; y++)
                {
                    float ratio = GetColorPixelRateHorizontal(content.Left, content.Right, y, blurredImage, new Rgba32(0, 0, 0), 50);
                    if (ratio < 0.5f)
                    {
                        startY = y;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            //look for the end y for the horizonal line >0.5 are black pixels
            {
                for (int y = content.Bottom - 1; y >= startY; y--)
                {
                    float ratio = GetColorPixelRateHorizontal(content.Left, content.Right, y, blurredImage, new Rgba32(0, 0, 0), 50);
                    if (ratio < 0.5f)
                    {
                        endY = y;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            for (int x= content.Left; x < content.Right; x++)
            {
                List<GraphPixel> columnPixels = new();
                int total = 0;
                int blackCount = 0;
                for (int y = startY; y < endY; y++)
                {
                    Rgba32 pixel = blurredImage[x, y];
                    if (pixel.R < 50 && pixel.G < 50 && pixel.B < 50)
                    {
                        blackCount++;
                    }
                    else
                    {
                        columnPixels.Add(new GraphPixel (x, y, pixel.R, pixel.G, pixel.B ));
                    }
                    total++;
                }
                float ratio = (float)blackCount / total;
                if (ratio > 0.5f)
                {
                    blackWidth++;
                    graph.Pixels.AddRange(columnPixels);
                }
                else
                {
                    columnPixels.Clear();
                }
                totalWidth++;
            }
            Save($"c:/temp/GetGraphContent_{DateTime.Now.ToString("yyyyMMddHHmmssfff")}.png", blurredImage, content);
            float blackRatio = (float)blackWidth / totalWidth;
            if (blackRatio > 0.5f)
            {
                Console.WriteLine("Graph");
            }
            else
            {
                graph.Pixels.Clear();
            }
            if (graph.Pixels.Count > 0)
            {
                //adjust the minimum x as 0 in the graph pixels
                int minX = graph.Pixels.Min(p => p.X);
                foreach(var pixel in graph.Pixels)
                {
                    pixel.X -= minX;
                }
                int maxX = graph.Pixels.Max(p => p.X);
                int maxY = graph.Pixels.Max(p => p.Y);

                //write the pixel x,y to CSV file
                using (var writer = new StreamWriter($"c:/temp/GetGraphContent_{DateTime.Now.ToString("yyyyMMddHHmmssfff")}.csv"))
                {
                    //writer.WriteLine("X:Y|Red:Green:Blue");
                    for (int y = 0; y < maxY; y++)
                    {
                        List<GraphPixel> rowPixels = graph.Pixels.Where(p => p.Y == y).ToList();
                        //sort the rowPixels by x
                        rowPixels.Sort((a, b) => a.X.CompareTo(b.X));
                        //write the rowPixels to CSV file as a line with x,y,red,green,blue 
                        for (int x = 0; x < maxX; x++)
                        {
                            GraphPixel? pixel = rowPixels.FirstOrDefault((p => p.X == x), null);
                            //check if the x is in the rowPixels
                            if (pixel==null)
                            {
                                writer.Write($",");
                            }
                            else
                            {
                                writer.Write($"{pixel.Red}:{pixel.Green}:{pixel.Blue},");
                            }
                        }
                        writer.WriteLine("");
                    }
                }
            }
            return graph;
        }

        public Rectangle GetContentBound()
        {
            // Local helper to check if a pixel belongs to a title bar/border
            bool IsBorder(Rgba32 pixel) =>
                CaseSummaryImage.IsActiveTitleBarPixel(pixel) || CaseSummaryImage.IsInActiveTitleBarPixel(pixel);

            int top = Bounds.Top;
            int bottom = Bounds.Bottom;
            int left = Bounds.Left;
            int right = Bounds.Right;

            Image.ProcessPixelRows(accessor =>
            {
                // 1. Remove Top Border (Title Bar)
                for (int y = top; y < bottom; y++)
                {
                    int count = 0;
                    Span<Rgba32> row = accessor.GetRowSpan(y);
                    for (int x = left; x < right; x++)
                    {
                        if (IsBorder(row[x])) count++;
                    }

                    if (count >= (right - left) * TitleBarDensity) top++;
                    else break;
                }

                // 2. Remove Bottom Border
                for (int y = bottom - 1; y >= top; y--)
                {
                    int count = 0;
                    Span<Rgba32> row = accessor.GetRowSpan(y);
                    for (int x = left; x < right; x++)
                    {
                        if (IsBorder(row[x])) count++;
                    }

                    if (count >= (right - left) * TitleBarDensity) bottom--;
                    else break;
                }

                // 3. Remove Left Side Border
                for (int x = left; x < right; x++)
                {
                    int count = 0;
                    for (int y = top; y < bottom; y++)
                    {
                        if (IsBorder(accessor.GetRowSpan(y)[x])) count++;
                    }

                    if (count >= (bottom - top) * TitleBarDensity) left++;
                    else break;
                }

                // 4. Remove Right Side Border
                for (int x = right - 1; x >= left; x--)
                {
                    int count = 0;
                    for (int y = top; y < bottom; y++)
                    {
                        if (IsBorder(accessor.GetRowSpan(y)[x])) count++;
                    }

                    if (count >= (bottom - top) * TitleBarDensity) right--;
                    else break;
                }
            });

            // Construct the final rectangle from the shrunken edges
            return Rectangle.FromLTRB(left, top, right, bottom);
        }

        public bool IsPixelInPanel(int x, int y)
        {
            return Bounds.Contains(x, y);
        }

        public bool IsPixelNextToPanel(int x, int y)
        {
            Rectangle expandedBounds = Bounds;
            expandedBounds.Inflate(1, 1);
            return expandedBounds.Contains(x, y) && !Bounds.Contains(x, y);
        }

        public void Save(string path, Image<Rgba32> image, Rectangle rect)
        {
            //using Image<Rgba32> croppedImage = Image.Clone(ctx => ctx.Crop(Bounds));
            image.Clone(ctx => ctx.Crop(rect)).SaveAsPng(path);
        }

        public bool SaveAsCSV(string path, List<WindowsPanel> allPanels, int maxBottom)
        {
            Rectangle rect = GetWholeContent(allPanels, maxBottom);
            using Image<Rgba32> blurredImage = Image.Clone(ctx => ctx.GaussianBlur(1f)).Clone(ctx => ctx.Crop(rect));
            blurredImage.SaveAsPng(path + ".png");
            return false;
        }

        public void Save(string path, Rectangle rect)
        {
            using Image<Rgba32> croppedImage = Image.Clone(ctx => ctx.Crop(rect));
            croppedImage.SaveAsPng(path);
        }

        public void SaveWholePanel(string path, List<WindowsPanel> allPanels, int maxBottom)
        {
            Rectangle rect = GetWholeContent(allPanels, maxBottom);
            Console.WriteLine($"SaveWholePanel: {rect}");
            Console.WriteLine($"SaveWholePanel: {Image.Width} {Image.Height}");
            Save(path, rect);
        }

        public override string ToString()
        {
            return $"x1={Bounds.X}, y1={Bounds.Y}, x2={Bounds.Right}, y2={Bounds.Bottom}";
        }*/
    }
}
