using System;
using System.Collections.Generic;
using System.Text;
using UglyToad.PdfPig.Content;

namespace SignalVision
{
    public class CaseSummaryPage
    {
        public int PageNumber { get;}
        public Page Page { get; }
        public string Text { get; }
        public List<CaseSummaryImage> Images { get;}
        public Logger Logger { get; }
        public CaseSummaryData Parent {  get; }
        public Config Config => Parent.Config;

        public CaseSummaryPage(int pageNumber, Page page, CaseSummaryData parent)
        {
            PageNumber = pageNumber;
            Page = page;
            Parent = parent;
            Logger = parent.Logger.WithTag($"CaseSummaryPage: Page {pageNumber}");
            StringBuilder sb = new();
            //loop for each word in page
            foreach (var word in Page.GetWords())
            {
                sb.Append(word.Text).Append(' ');
            }

            Text = sb.ToString();
            //Write the Text to a file in the output folder where the file name is "page_{PageNumber}.txt"
            File.WriteAllText(Path.Combine(Parent.OutputFolder, $"page_{PageNumber}.txt"), Text);

            Images = [];
            int imageIndex = 1;
            foreach (IPdfImage image in page.GetImages())
            {
                Images.Add((new CaseSummaryImage(image, imageIndex++, this)));
            }
        }
    }
}
