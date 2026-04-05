using System;
using System.Collections.Generic;

class Overlay
{
    public enum Align
    {
        TopLeft,
        Center,
    }

    private record struct LineEntry(Line Line, Color Color, bool DepthTest, float DepthBias);
    private record struct TextEntry3D(Vec3 WorldPos, string Text, Color Color, Align Align);
    private record struct TextEntry2D(Vec2i ScreenPos, string Text, Color Color, Align Align);

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

    public void AddText(string text, Vec3 worldPos, Color color, Align align = Align.Center) =>
        _text3D.Add(new TextEntry3D(worldPos, text, color, align));

    public void AddText(string text, Vec2i screenPos, Color color, Align align = Align.TopLeft) =>
        _text2D.Add(new TextEntry2D(screenPos, text, color, align));

    public void Draw(Image image, View view, float[]? depth = null, Counters? counters = null)
    {
        float aspect = (float)image.Width / image.Height;
        Vec2 size = new Vec2(image.Width, image.Height);

        foreach (LineEntry line in _lines)
        {
            counters?.Bump(Counters.Type.OverlayLine);

            var projA = view.Project(line.Line.A, aspect);
            var projB = view.Project(line.Line.B, aspect);
            if (projA is (Vec2 posA, float depthA) && projB is (Vec2 posB, float depthB))
            {
                float depthBias = 1f - line.DepthBias;
                Vec2i coordA = (posA * size).RoundToInt();
                Vec2i coordB = (posB * size).RoundToInt();

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
            counters?.Bump(Counters.Type.OverlayText);

            if (view.Project(entry.WorldPos, aspect) is (Vec2 pos, float _))
                RasterizeText(image, entry.Text, (pos * size).RoundToInt(), entry.Align, entry.Color.ToPixel());
        }

        foreach (TextEntry2D entry in _text2D)
        {
            counters?.Bump(Counters.Type.OverlayText);

            RasterizeText(image, entry.Text, entry.ScreenPos, entry.Align, entry.Color.ToPixel());
        }
    }

    private static int? PixelIndex(Image image, Vec2i coord) =>
        (uint)coord.X < image.Width && (uint)coord.Y < image.Height
            ? coord.Y * (int)image.Width + coord.X
            : null;

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
            if (PixelIndex(image, cursor) is int index)
            {
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
                cursor += Vec2i.Right * stepX;
            }
            if (err2 <= spanX)
            {
                bresenhamError += spanX;
                cursor += Vec2i.Up * stepY;
            }
        }
    }

    private static void RasterizeTextLine(Image image, ReadOnlySpan<char> line, Vec2i coord, Pixel value)
    {
        for (int i = 0; i != line.Length; ++i)
        {
            if (line[i] < Font.FirstChar || line[i] > Font.LastChar)
                continue; // Unsupported character.

            int glyphBase = (line[i] - Font.FirstChar) * Font.CharHeight;
            for (int glyphRow = 0; glyphRow != Font.CharHeight; ++glyphRow)
            {
                byte fontBits = Font.Glyphs[glyphBase + glyphRow];
                for (int glyphCol = 0; glyphCol != Font.CharWidth; ++glyphCol)
                {
                    if ((fontBits & (0x80 >> glyphCol)) == 0)
                        continue;
                    if (PixelIndex(image, coord + new Vec2i(i * Font.CharWidth + glyphCol, glyphRow)) is int index)
                        image.Pixels[index] = value;
                }
            }
        }
    }

    private static void RasterizeText(Image image, ReadOnlySpan<char> text, Vec2i coord, Align align, Pixel value)
    {
        Span<Range> lines = stackalloc Range[64];
        int lineCount = text.Split(lines, '\n');

        if (align == Align.Center)
        {
            int width = 0;
            for (int i = 0; i != lineCount; ++i)
                width = Math.Max(width, text[lines[i]].Length);

            coord -= new Vec2i(width * Font.CharWidth / 2, lineCount * Font.CharHeight / 2);
        }

        for (int i = 0; i != lineCount; ++i)
        {
            ReadOnlySpan<char> line = text[lines[i]];
            RasterizeTextLine(image, line, new Vec2i(coord.X, coord.Y + i * Font.CharHeight), value);
        }
    }
}
