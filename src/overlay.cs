using System;
using System.Collections.Generic;

class Overlay
{
    private struct LineEntry
    {
        public Line Line;
        public Color Color;

        public LineEntry(Line line, Color color)
        {
            Line = line;
            Color = color;
        }
    }

    private List<LineEntry> _lines = new List<LineEntry>();

    public void AddLine(Line line, Color color) => _lines.Add(new LineEntry(line, color));

    public void Draw(Image image, View view)
    {
        float aspect = (float)image.Width / image.Height;
        Vec2 size = new Vec2(image.Width, image.Height);
        foreach (LineEntry line in _lines)
        {
            Vec2? a = view.Project(line.Line.A, aspect);
            Vec2? b = view.Project(line.Line.B, aspect);
            if (a is Vec2 pa && b is Vec2 pb)
                DrawLine(image, (pa * size).ToInt(), (pb * size).ToInt(), line.Color);
        }
    }

    private static void DrawLine(Image image, Vec2i a, Vec2i b, Color color)
    {
        // Bresenham's line algorithm.
        // https://en.wikipedia.org/wiki/Bresenham%27s_line_algorithm

        Pixel pixel = color.ToPixel();
        int dx = Math.Abs(b.X - a.X), sx = a.X < b.X ? 1 : -1;
        int dy = -Math.Abs(b.Y - a.Y), sy = a.Y < b.Y ? 1 : -1;
        int err = dx + dy;

        while (true)
        {
            if ((uint)a.X < image.Width && (uint)a.Y < image.Height)
            {
                image.Pixels[a.Y * image.Width + a.X] = pixel;
            }

            if (a.X == b.X && a.Y == b.Y)
                break;

            int e2 = 2 * err;
            if (e2 >= dy)
            {
                err += dy; a.X += sx;
            }
            if (e2 <= dx)
            {
                err += dx; a.Y += sy;
            }
        }
    }
}
