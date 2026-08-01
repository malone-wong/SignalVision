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
}
