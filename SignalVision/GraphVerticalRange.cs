using System;
using System.Collections.Generic;
using System.Text;

namespace SignalVision
{
    public class GraphVerticalRange
    {
        public List<GraphPixel> Pixels { get; } = new ();
        public string Color
        {
            get
            {
                if (Pixels.Count > 0)
                {
                    return Pixels[0].ColorName;
                }
                return string.Empty;
            }
        }

        public GraphVerticalRange(GraphPixel pixel)
        {
            Pixels.Add(pixel);
        }

        public bool Add(GraphPixel pixel)
        {
            GraphPixel lastPixel = Pixels[Pixels.Count - 1];
            if (pixel.Y - 1 == lastPixel.Y)
            {
                Pixels.Add(pixel);
                return true;
            }
            return false;
        }

        public override string ToString()
        {
            return $"[Color: {Color}] [Count: {Pixels.Count}] [First Pixel: {Pixels[0]}]";
        }
    }
}
