using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Reflection.Metadata.Ecma335;
using static System.Net.Mime.MediaTypeNames;

namespace SignalVision
{
    public class WindowsPanel
    {
        public const int MinimumPanelWidthSize = 100;
        public const int MinimumPanelHeightSize = 20;
        public const int MinimumWidth = 13;
        public Rectangle Bounds { get; set; }
        public bool IsActive { get; }
        private Image<Rgba32> Image { get; }

        public const double TitleBarDensity = 0.5;

        public WindowsPanel(int x, int y, bool isActive, Image<Rgba32> image)
        {
            Bounds = new Rectangle(x, y, 1, 1);
            IsActive = isActive;
            Image = image;
        }

        public WindowsPanel Union(List<WindowsPanel> panels)
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
                        /*foreach (var pixel in rowPixels)
                        {
                            //writer.Write($"{pixel.X}:{pixel.Y}|{pixel.Red}:{pixel.Green}:{pixel.Blue},");
                            writer.Write($"{pixel.Red}:{pixel.Green}:{pixel.Blue},");
                        }*/
                        writer.WriteLine("");
                    }
                    /*
                    foreach (var pixel in graph.Pixels)
                    {
                        writer.WriteLine($"{pixel.X},{pixel.Y},{pixel.Red},{pixel.Green},{pixel.Blue}");
                    }*/
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

        public bool ExpandToIncludePixel(int x, int y)
        {
            if (IsPixelNextToPanel(x, y))
            {
                Bounds = Rectangle.Union(Bounds, new Rectangle(x, y, 1, 1));
                return true;
            }
            return false;
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
        }
    }
}
