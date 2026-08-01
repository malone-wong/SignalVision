using OpenCvSharp;
using Sdcb.PaddleInference;
using Sdcb.PaddleOCR;
using Sdcb.PaddleOCR.Models.Local;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace SignalVision;

public static class PaddleOCRHelper
{
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
}
