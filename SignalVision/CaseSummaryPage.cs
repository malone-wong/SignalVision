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

        public CaseSummaryPage(int pageNumber, Page page, Config config)
        {
            PageNumber = pageNumber;
            Page = page;
            StringBuilder sb = new();
            //loop for each word in page
            foreach (var word in Page.GetWords())
            {
                sb.Append(word.Text).Append(' ');
            }

            Text = sb.ToString();

            Images = new List<CaseSummaryImage>();
            foreach (IPdfImage image in page.GetImages())
            {
                Images.Add(new CaseSummaryImage(image, config));
            }
        }
    }
}
