using SixLabors.ImageSharp.PixelFormats;
using System;
using System.Collections.Generic;
using System.Text;

namespace SignalVision
{
    public class GraphPixel
    {
        public int X { get; set; }
        public int Y { get; set; }
        public Rgba32 Color {  get; set; }
        public double Distance { get; set; } = double.MaxValue;
        public string ColorName { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"[X: {X}] [Y: {Y}] [Color: {Color}] [Distance: {Distance}] [ColorName: {ColorName}]";
        }
    }
}
