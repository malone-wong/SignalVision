using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Text;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace SignalVision
{
    public class MicrosoftOCRHelper
    {
        private const int PreparedPadding = 20;
        private const int SourceOverlap = 80;

        public static string ExtractTextFromImage(
            Image<Rgba32> imageSharpImage,
            Config config,
            Logger logger)
        {
            try
            {
                ArgumentNullException.ThrowIfNull(imageSharpImage);

                int preferredScale = config.OCRScale;
                int maxPreparedDimension = config.OCRMaxPreparedDimension;
                if (preferredScale <= 0 || maxPreparedDimension <= PreparedPadding * 2)
                    throw new InvalidOperationException("OCR scale and maximum dimension must be positive.");

                OcrEngine ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages()
                    ?? throw new InvalidOperationException("Failed to initialize the OCR engine.");

                // Small title-bar characters are unreliable for Windows OCR. Process wide
                // images in sections so each section can be enlarged without exceeding the
                // OCR engine's maximum image dimension.
                int maxContentDimension = maxPreparedDimension - (PreparedPadding * 2);
                int sourceChunkWidth = Math.Max(1, maxContentDimension / preferredScale);
                int overlap = Math.Min(SourceOverlap, sourceChunkWidth / 4);
                int step = Math.Max(1, sourceChunkWidth - overlap);
                string text = string.Empty;

                for (int left = 0; left < imageSharpImage.Width; left += step)
                {
                    int width = Math.Min(sourceChunkWidth, imageSharpImage.Width - left);
                    using var section = imageSharpImage.Clone(context => context.Crop(
                        new Rectangle(left, 0, width, imageSharpImage.Height)));
                    Color backgroundColor = Color.FromPixel(section[0, 0]);

                    double scale = Math.Min(
                        preferredScale,
                        (double)maxContentDimension / Math.Max(section.Width, section.Height));

                    if (scale > 1.0)
                    {
                        section.Mutate(context => context.Resize(
                            Math.Max(1, (int)Math.Round(section.Width * scale)),
                            Math.Max(1, (int)Math.Round(section.Height * scale)),
                            KnownResamplers.Lanczos3));
                    }

                    section.Mutate(context => context
                        .Pad(
                            section.Width + (PreparedPadding * 2),
                            section.Height + (PreparedPadding * 2),
                            backgroundColor)
                        .Grayscale()
                        .Contrast(1.2f)
                        .GaussianSharpen(0.5f));
                    //section.SaveAsPng($"c:/temp/ocr_{DateTime.Now.ToString("yyyyMMddHHmmssfff")}.png");//TODO: Remove this debug line in production
                    string sectionText = RecognizeBestVariant(section, ocrEngine);

                    if (!string.IsNullOrWhiteSpace(sectionText))
                        text = MergeOverlappingText(text, sectionText);

                    if (left + width >= imageSharpImage.Width)
                        break;
                }

                return text;
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
        /// Unlike <see cref="ExtractTextFromImage"/> this uses a single scaled
        /// pass rather than overlapping sections, because overlapping sections
        /// would report the same word at two different offsets. Detection
        /// failures are logged rather than thrown: callers use this to refine
        /// curve extraction, which must still work when no text is found.
        /// </remarks>
        public static IReadOnlyList<OcrTextRegion> DetectTextRegions(
            Image<Rgba32> imageSharpImage,
            Config config,
            Logger logger)
        {
            ArgumentNullException.ThrowIfNull(imageSharpImage);

            try
            {
                OcrEngine? ocrEngine = OcrEngine.TryCreateFromUserProfileLanguages();
                if (ocrEngine is null)
                {
                    logger.Warn("Failed to initialize the OCR engine for text detection.");
                    return [];
                }

                int maxContentDimension = Math.Max(
                    1,
                    config.OCRMaxPreparedDimension - (PreparedPadding * 2));
                double scale = Math.Min(
                    Math.Max(1, config.OCRScale),
                    (double)maxContentDimension /
                        Math.Max(imageSharpImage.Width, imageSharpImage.Height));
                if (scale < 1.0)
                    scale = 1.0;

                using Image<Rgba32> prepared = imageSharpImage.Clone();
                if (scale > 1.0)
                {
                    prepared.Mutate(context => context.Resize(
                        Math.Max(1, (int)Math.Round(imageSharpImage.Width * scale)),
                        Math.Max(1, (int)Math.Round(imageSharpImage.Height * scale)),
                        KnownResamplers.Lanczos3));
                }

                prepared.Mutate(context => context.Pad(
                    prepared.Width + (PreparedPadding * 2),
                    prepared.Height + (PreparedPadding * 2),
                    Color.Black));

                using MemoryStream memoryStream = new();
                prepared.SaveAsPng(memoryStream);

                using InMemoryRandomAccessStream winRtStream = new();
                using DataWriter writer = new(winRtStream.GetOutputStreamAt(0));
                writer.WriteBytes(memoryStream.ToArray());
                writer.StoreAsync().AsTask().GetAwaiter().GetResult();
                writer.FlushAsync().AsTask().GetAwaiter().GetResult();
                winRtStream.Seek(0);

                BitmapDecoder decoder = BitmapDecoder.CreateAsync(winRtStream)
                    .AsTask().GetAwaiter().GetResult();
                using SoftwareBitmap softwareBitmap = decoder.GetSoftwareBitmapAsync()
                    .AsTask().GetAwaiter().GetResult();

                OcrResult result = ocrEngine.RecognizeAsync(softwareBitmap)
                    .AsTask().GetAwaiter().GetResult();

                List<OcrTextRegion> regions = [];
                foreach (OcrLine line in result.Lines)
                {
                    foreach (OcrWord word in line.Words)
                    {
                        int left = (int)Math.Floor((word.BoundingRect.Left - PreparedPadding) / scale);
                        int top = (int)Math.Floor((word.BoundingRect.Top - PreparedPadding) / scale);
                        int right = (int)Math.Ceiling((word.BoundingRect.Right - PreparedPadding) / scale);
                        int bottom = (int)Math.Ceiling((word.BoundingRect.Bottom - PreparedPadding) / scale);

                        Rectangle bounds = Rectangle.Intersect(
                            Rectangle.FromLTRB(left, top, right, bottom),
                            new Rectangle(0, 0, imageSharpImage.Width, imageSharpImage.Height));
                        if (bounds.Width <= 0 || bounds.Height <= 0)
                            continue;

                        regions.Add(new OcrTextRegion
                        {
                            Text = word.Text ?? string.Empty,
                            Bounds = bounds,
                        });
                    }
                }

                return regions;
            }
            catch (Exception ex)
            {
                logger.Warn($"Windows OCR text detection failed: {ex.Message}");
                return [];
            }
        }

        private static string RecognizeBestVariant(Image<Rgba32> image, OcrEngine ocrEngine)
        {
            string bestText = Recognize(image, ocrEngine);
            int bestScore = RecognitionScore(bestText);

            // Decorative icons next to title text can cause Windows OCR to discard an
            // otherwise clear word. Retry with a small relative trim from either edge.
            // The untrimmed pass remains a candidate, so genuine edge text is retained
            // whenever it produces the most complete result.
            int horizontalPadding = Math.Min(PreparedPadding, image.Width / 10);
            int contentWidth = Math.Max(1, image.Width - (horizontalPadding * 2));
            int edgeTrim = Math.Max(1, contentWidth / 20);

            var trims = new (int Left, int Right)[]
            {
                (edgeTrim, 0),
                (0, edgeTrim),
                (edgeTrim, edgeTrim)
            };

            foreach ((int left, int right) in trims)
            {
                int cropLeft = horizontalPadding + left;
                int cropRight = image.Width - horizontalPadding - right;
                if (cropRight <= cropLeft)
                    continue;

                using var variant = image.Clone(context => context
                    .Crop(new Rectangle(cropLeft, 0, cropRight - cropLeft, image.Height))
                    .Pad(
                        cropRight - cropLeft + (PreparedPadding * 2),
                        image.Height,
                        Color.FromPixel(image[horizontalPadding, image.Height / 2])));

                string candidate = Recognize(variant, ocrEngine);
                int candidateScore = RecognitionScore(candidate);
                if (candidateScore > bestScore)
                {
                    bestText = candidate;
                    bestScore = candidateScore;
                }
            }

            return bestText;
        }

        private static int RecognitionScore(string text) =>
            text.Count(char.IsLetterOrDigit);

        private static string MergeOverlappingText(string existing, string addition)
        {
            string[] existingWords = existing.Split(
                (char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            string[] additionWords = addition.Split(
                (char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            if (existingWords.Length == 0)
                return string.Join(' ', additionWords);
            if (additionWords.Length == 0)
                return string.Join(' ', existingWords);

            int maximumOverlap = Math.Min(existingWords.Length, additionWords.Length);
            int matchingWords = 0;
            for (int count = maximumOverlap; count > 0; count--)
            {
                bool matches = true;
                for (int index = 0; index < count; index++)
                {
                    if (!string.Equals(
                        existingWords[existingWords.Length - count + index],
                        additionWords[index],
                        StringComparison.OrdinalIgnoreCase))
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                {
                    matchingWords = count;
                    break;
                }
            }

            return string.Join(' ', existingWords.Concat(additionWords.Skip(matchingWords)));
        }

        private static string Recognize(Image<Rgba32> image, OcrEngine ocrEngine)
        {
            using var memoryStream = new MemoryStream();
            image.SaveAsPng(memoryStream);

            using var winRtStream = new InMemoryRandomAccessStream();
            using var writer = new DataWriter(winRtStream.GetOutputStreamAt(0));
            writer.WriteBytes(memoryStream.ToArray());
            writer.StoreAsync().AsTask().GetAwaiter().GetResult();
            writer.FlushAsync().AsTask().GetAwaiter().GetResult();
            winRtStream.Seek(0);

            BitmapDecoder decoder = BitmapDecoder.CreateAsync(winRtStream)
                .AsTask().GetAwaiter().GetResult();
            using SoftwareBitmap softwareBitmap = decoder.GetSoftwareBitmapAsync()
                .AsTask().GetAwaiter().GetResult();

            OcrResult result = ocrEngine.RecognizeAsync(softwareBitmap)
                .AsTask().GetAwaiter().GetResult();
            return result.Text;
        }
    }
}
