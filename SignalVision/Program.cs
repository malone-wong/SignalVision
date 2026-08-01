using SignalVision;
using UglyToad.PdfPig;

Config config = new();
using PdfDocument document = PdfDocument.Open(config.PDF);
CaseSummaryData caseSummaryData = new(document, config.PDF, config);
caseSummaryData.Logger.Info($"PDF file: {config.PDF}");
caseSummaryData.Process();
