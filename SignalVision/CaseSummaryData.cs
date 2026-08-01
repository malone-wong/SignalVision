using UglyToad.PdfPig;

namespace SignalVision
{
    public class CaseSummaryData
    {
        public Logger Logger { get; }
        public PdfDocument Document { get; }
        public Config Config { get; }
        public string SourcePdfPath { get; }
        public List<CaseSummaryPage> Pages { get; } = [];
        public string OutputFolder { get; }
        public string LogFilePath { get; }

        public CaseSummaryData(PdfDocument document, string sourcePdfPath, Config config)
        {
            Document = document;
            Config = config;
            SourcePdfPath = sourcePdfPath;
            OutputFolder = Path.Combine(
                Config.OutputBasePath,
                Path.GetFileNameWithoutExtension(SourcePdfPath));
            LogFilePath = Path.Combine(OutputFolder, "SignalVision.log");
            Logger = new Logger("CaseSummaryData", LogFilePath);
        }

        public CaseSummaryData Process()
        {
            if (Config.DeleteExistingOutputFolder && Directory.Exists(OutputFolder))
            {
                Directory.Delete(OutputFolder, true);
            }
            Directory.CreateDirectory(OutputFolder);

            int pageNumber = 1;
            foreach (var page in Document.GetPages())
            {
                Logger.Debug($"Processing page {pageNumber}...");
                Pages.Add(new CaseSummaryPage(
                    pageNumber,
                    page,
                    this));
                pageNumber++;
            }

            return this;
        }
    }
}
