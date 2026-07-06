using System;
using System.Collections.Generic;
using System.Text;

namespace SignalVision
{
    public class GraphPixel
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Red { get; set; }
        public int Green { get; set; }
        public int Blue { get; set; }

        public GraphPixel(int x, int y, int red, int green, int blue)
        {
            X = x;
            Y = y;
            Red = red;
            Green = green;
            Blue = blue;
        }
    }

    public class Graph
    {
        public List<GraphPixel> Pixels { get; }

        public Graph()
        {
            Pixels = [];
        }
}
}
