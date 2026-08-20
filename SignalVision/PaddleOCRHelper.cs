using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models.Local;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace SignalVision;

public static class PaddleOCRHelper
{
    private const int PreparedPadding = 20;

    private static readonly object OcrLock = new();
    private static readonly Lazy<PaddleOcrAll> OcrEngine = new(() =>
        new PaddleOcrAll(LocalFullModels.EnglishV5, PaddleDevice.Mkldnn())
        {
            AllowRotateDetection = false,
            Enable180Classification = false
        });

    public static string ExtractTextFromImage(
        Image<Rgba32> image,
        Logger logger)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(image);

            using MemoryStream stream = new();
            image.SaveAsPng(stream);
            using Mat source = Cv2.ImDecode(stream.ToArray(), ImreadModes.Color);

            if (source.Empty())
                throw new InvalidOperationException("Failed to convert the image for Paddle OCR.");

            lock (OcrLock)
            {
                return OcrEngine.Value.Run(source).Text;
            }
        }
        catch (Exception ex)
        {
            logger.Error($"Error extracting text from image: {ex}");
            throw;
        }
    }

    /// <summary>
    /// Locates the text blocks in an image and reports them in the source
    /// image's own coordinates.
    /// </summary>
    /// <remarks>
    /// Graph labels are only a few pixels tall, so the image is enlarged before
    /// detection and the resulting boxes are mapped back. Detection failures are
    /// logged rather than thrown: callers use this to refine curve extraction,
    /// which must still work when no text is found.
    /// </remarks>
    public static IReadOnlyList<OcrTextRegion> DetectTextRegions(
        Image<Rgba32> image,
        int scale,
        int maximumPreparedDimension,
        Logger logger)
    {
        ArgumentNullException.ThrowIfNull(image);

        try
        {
            int maximumContent = Math.Max(1, maximumPreparedDimension - (PreparedPadding * 2));
            double factor = Math.Min(
                Math.Max(1, scale),
                (double)maximumContent / Math.Max(image.Width, image.Height));
            if (factor < 1.0)
                factor = 1.0;

            int scaledWidth = Math.Max(1, (int)Math.Round(image.Width * factor));
            int scaledHeight = Math.Max(1, (int)Math.Round(image.Height * factor));

            using Image<Rgba32> prepared = image.Clone();
            if (factor > 1.0)
            {
                prepared.Mutate(context => context.Resize(
                    scaledWidth, scaledHeight, KnownResamplers.Lanczos3));
            }

            // Pad so glyphs touching an edge are still detected. The padding is
            // subtracted again when the boxes are mapped back.
            prepared.Mutate(context => context.Pad(
                prepared.Width + (PreparedPadding * 2),
                prepared.Height + (PreparedPadding * 2),
                Color.Black));

            using MemoryStream stream = new();
            prepared.SaveAsPng(stream);
            using Mat source = Cv2.ImDecode(stream.ToArray(), ImreadModes.Color);
            if (source.Empty())
            {
                logger.Warn("Failed to convert the image for Paddle OCR text detection.");
                return [];
            }

            PaddleOcrResult result;
            lock (OcrLock)
            {
                result = OcrEngine.Value.Run(source);
            }

            List<OcrTextRegion> regions = [];
            foreach (PaddleOcrResultRegion region in result.Regions)
            {
                Rect box = region.Rect.BoundingRect();
                int left = (int)Math.Floor((box.Left - PreparedPadding) / factor);
                int top = (int)Math.Floor((box.Top - PreparedPadding) / factor);
                int right = (int)Math.Ceiling((box.Right - PreparedPadding) / factor);
                int bottom = (int)Math.Ceiling((box.Bottom - PreparedPadding) / factor);

                Rectangle bounds = Rectangle.Intersect(
                    Rectangle.FromLTRB(left, top, right, bottom),
                    new Rectangle(0, 0, image.Width, image.Height));
                if (bounds.Width <= 0 || bounds.Height <= 0)
                    continue;

                regions.Add(new OcrTextRegion
                {
                    Text = region.Text ?? string.Empty,
                    Bounds = bounds,
                });
            }

            return regions;
        }
        catch (Exception ex)
        {
            logger.Warn($"Paddle OCR text detection failed: {ex.Message}");
            return [];
        }
    }
}
