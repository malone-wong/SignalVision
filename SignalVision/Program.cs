using SignalVision;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using System.Text;
using System.Text.Json;
using UglyToad.PdfPig;


Config config = Config.Read();
Console.WriteLine($"PDF file: {config.PDF}");

using (PdfDocument document = PdfDocument.Open(config.PDF))
{
    CaseSummaryData caseSummaryData = new(document, config);
}
