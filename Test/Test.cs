
using SignalVision;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

using (PdfDocument document = PdfDocument.Open("c:/temp/CaseSummaryData _20260222 (masked).pdf"))
{
    Page page = document.GetPage(7);
    //CaseSummaryImage image = new CaseSummaryImage(page.GetImages().First(), null, new Config());
    //Console.WriteLine(image);
}
Console.WriteLine("Complete!");
