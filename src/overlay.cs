using System;
using System.Collections.Generic;

class Overlay
{
    private struct LineEntry
    {
        public Line Line;
        public Color Color;
        public bool DepthTest;
        public float DepthBias; // Relative bias; depth is multiplied by (1 - bias) before testing.

        public LineEntry(Line line, Color color, bool depthTest, float depthBias)
        {
            Line = line;
            Color = color;
            DepthTest = depthTest;
            DepthBias = depthBias;
        }
    }

    private List<LineEntry> _lines = new List<LineEntry>();

    public void AddLine(Line line, Color color, bool depthTest = true, float depthBias = 0.005f) =>
        _lines.Add(new LineEntry(line, color, depthTest, depthBias));

    public void Draw(Image image, View view, float[]? depth = null)
    {
        float aspect = (float)image.Width / image.Height;
        Vec2 size = new Vec2(image.Width, image.Height);

        foreach (LineEntry line in _lines)
        {
            var projA = view.Project(line.Line.A, aspect);
            var projB = view.Project(line.Line.B, aspect);
            if (projA is (Vec2 posA, float depthA) && projB is (Vec2 posB, float depthB))
            {
                float depthBias = 1f - line.DepthBias;
                Vec2i coordA = (posA * size).ToInt();
                Vec2i coordB = (posB * size).ToInt();

                RasterizeLine(
                    image,
                    line.DepthTest ? depth : null,
                    coordA,
                    coordB,
                    depthA * depthBias,
                    depthB * depthBias,
                    line.Color);
            }
        }
    }

    private static void RasterizeLine(
        Image image,
        float[]? depth,
        Vec2i start,
        Vec2i end,
        float depthA,
        float depthB,
        Color color)
    {
        // Bresenham's line algorithm.
        // https://en.wikipedia.org/wiki/Bresenham%27s_line_algorithm

        Pixel pixel = color.ToPixel();

        float depthAInv = 1f / depthA;
        float depthBInv = 1f / depthB;

        int spanX = Math.Abs(end.X - start.X);
        int spanY = -Math.Abs(end.Y - start.Y);
        int stepX = start.X < end.X ? 1 : -1;
        int stepY = start.Y < end.Y ? 1 : -1;
        int bresenhamError = spanX + spanY;
        int totalSteps = Math.Max(spanX, -spanY);
        int step = -1;
        Vec2i cursor = start;

        while (true)
        {
            ++step;
            if ((uint)cursor.X < image.Width && (uint)cursor.Y < image.Height)
            {
                int index = cursor.Y * (int)image.Width + cursor.X;
                float lineFrac = totalSteps > 0 ? (float)step / totalSteps : 0f;
                float lineDepth = 1f / float.Lerp(depthAInv, depthBInv, lineFrac);

                if (depth is null || lineDepth <= depth[index])
                    image.Pixels[index] = pixel;
            }

            if (cursor.X == end.X && cursor.Y == end.Y)
                break;

            int err2 = 2 * bresenhamError;
            if (err2 >= spanY)
            {
                bresenhamError += spanY;
                cursor.X += stepX;
            }
            if (err2 <= spanX)
            {
                bresenhamError += spanX;
                cursor.Y += stepY;
            }
        }
    }
}
