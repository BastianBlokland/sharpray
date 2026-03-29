using System;
using System.Collections.Generic;

class Overlay
{
    private record struct LineEntry(Line Line, Color Color, bool DepthTest, float DepthBias);
    private record struct TextEntry3D(Vec3 WorldPos, string Text, Color Color);
    private record struct TextEntry2D(Vec2i ScreenPos, string Text, Color Color);

    private List<LineEntry> _lines = new List<LineEntry>();
    private List<TextEntry3D> _text3D = new List<TextEntry3D>();
    private List<TextEntry2D> _text2D = new List<TextEntry2D>();

    public void AddLine(Line line, Color color, bool depthTest = true, float depthBias = 0.005f) =>
        _lines.Add(new LineEntry(line, color, depthTest, depthBias));

    public void AddLineBox(AABox box, Color color, bool depthTest = true, float depthBias = 0.005f) =>
        AddLineBox(new Box(box, Quat.Identity()), color, depthTest, depthBias);

    public void AddLineBox(Box box, Color color, bool depthTest = true, float depthBias = 0.005f)
    {
        Span<Vec3> corners = stackalloc Vec3[8];
        box.Corners(corners);

        AddLine(new Line(corners[0], corners[1]), color, depthTest, depthBias);
        AddLine(new Line(corners[2], corners[3]), color, depthTest, depthBias);
        AddLine(new Line(corners[4], corners[5]), color, depthTest, depthBias);
        AddLine(new Line(corners[6], corners[7]), color, depthTest, depthBias);

        AddLine(new Line(corners[0], corners[2]), color, depthTest, depthBias);
        AddLine(new Line(corners[1], corners[3]), color, depthTest, depthBias);
        AddLine(new Line(corners[4], corners[6]), color, depthTest, depthBias);
        AddLine(new Line(corners[5], corners[7]), color, depthTest, depthBias);

        AddLine(new Line(corners[0], corners[4]), color, depthTest, depthBias);
        AddLine(new Line(corners[1], corners[5]), color, depthTest, depthBias);
        AddLine(new Line(corners[2], corners[6]), color, depthTest, depthBias);
        AddLine(new Line(corners[3], corners[7]), color, depthTest, depthBias);
    }

    public void AddText(string text, Vec3 worldPos, Color color) =>
        _text3D.Add(new TextEntry3D(worldPos, text, color));

    public void AddText(string text, Vec2i screenPos, Color color) =>
        _text2D.Add(new TextEntry2D(screenPos, text, color));

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
                    line.Color.ToPixel());
            }
        }

        foreach (TextEntry3D entry in _text3D)
        {
            if (view.Project(entry.WorldPos, aspect) is (Vec2 pos, float _))
                RasterizeText(image, entry.Text, (pos * size).ToInt(), entry.Color.ToPixel());
        }

        foreach (TextEntry2D entry in _text2D)
            RasterizeText(image, entry.Text, entry.ScreenPos, entry.Color.ToPixel());
    }

    private static void RasterizeLine(
        Image image,
        float[]? depth,
        Vec2i start,
        Vec2i end,
        float depthA,
        float depthB,
        Pixel value)
    {
        // Bresenham's line algorithm.
        // https://en.wikipedia.org/wiki/Bresenham%27s_line_algorithm

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
                    image.Pixels[index] = value;
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

    private static void RasterizeText(Image image, string text, Vec2i coord, Pixel value)
    {
        for (int i = 0; i != text.Length; ++i)
        {
            int c = text[i];
            if (c < Font.FirstChar || c > Font.LastChar)
                continue;

            int glyphBase = (c - Font.FirstChar) * Font.CharHeight;
            for (int row = 0; row != Font.CharHeight; ++row)
            {
                byte bits = Font.Glyphs[glyphBase + row];
                for (int col = 0; col != Font.CharWidth; ++col)
                {
                    if ((bits & (0x80 >> col)) == 0)
                    {
                        continue;
                    }
                    Vec2i pixel = new Vec2i(coord.X + i * Font.CharWidth + col, coord.Y + row);
                    if ((uint)pixel.X < image.Width && (uint)pixel.Y < image.Height)
                        image.Pixels[pixel.Y * (int)image.Width + pixel.X] = value;
                }
            }
        }
    }
}
