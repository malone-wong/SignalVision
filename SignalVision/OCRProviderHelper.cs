using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SignalVision;

public static class OCRHelper
{
    public static string ExtractTextFromImage(
        Image<Rgba32> image,
        Config config,
        Logger logger)
    {
        return config.OCRProvider switch
        {
            OcrProvider.Microsoft => MicrosoftOCRHelper.ExtractTextFromImage(
                image,
                config,
                logger.WithTag("MicrosoftOCR")),
            OcrProvider.Paddle => PaddleOCRHelper.ExtractTextFromImage(
                image,
                logger.WithTag("PaddleOCR")),
            _ => throw new InvalidOperationException(
                $"Unsupported OCR provider: {config.OCRProvider}")
        };
    }

    /// <summary>
    /// Locates the text blocks in an image, in the image's own coordinates.
    /// </summary>
    /// <remarks>
    /// Curve extraction uses this to tell an in-graph label apart from a trace
    /// drawn in the same color, so an engine that finds nothing must degrade to
    /// "no labels" rather than fail the graph.
    /// </remarks>
    public static IReadOnlyList<OcrTextRegion> DetectTextRegions(
        Image<Rgba32> image,
        Config config,
        Logger logger)
    {
        return config.OCRProvider switch
        {
            OcrProvider.Microsoft => MicrosoftOCRHelper.DetectTextRegions(
                image,
                config,
                logger.WithTag("MicrosoftOCR")),
            OcrProvider.Paddle => PaddleOCRHelper.DetectTextRegions(
                image,
                config.OCRScale,
                config.OCRMaxPreparedDimension,
                logger.WithTag("PaddleOCR")),
            _ => throw new InvalidOperationException(
                $"Unsupported OCR provider: {config.OCRProvider}")
        };
    }
}
