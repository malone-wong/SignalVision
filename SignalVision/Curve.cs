using System;
using System.Collections.Generic;
using System.Text;

namespace SignalVision
{
    public class Curve
    {
        public string Color
        {
            get
            {
                if (VerticalRanges.Count > 0)
                {
                    return VerticalRanges[0].Color;
                }
                return string.Empty;
            }
        }
        public List<GraphVerticalRange> VerticalRanges { get; set; } = new List<GraphVerticalRange>();

        public Curve(GraphVerticalRange bar)
        {
            VerticalRanges.Add(bar);
        }

        public override string ToString()
        {
            return $"[Color: {Color}] [VerticalRanges Count: {VerticalRanges.Count}] [First VerticalRange: {VerticalRanges[0]}]";
        }
    }
}
