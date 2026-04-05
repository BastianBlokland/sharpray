using System;
using System.Diagnostics;

enum TextureFilter { Bilinear, Point }

class Texture
{
    public readonly uint Width, Height;
    public TextureFilter Filter;
    public Vec2 Tiling;

    public Vec2i Size => new Vec2i((int)Width, (int)Height);

    private readonly Color[] _texels;

    private Texture(
        Color[] pixels,
        uint width,
        uint height)
    {
        Width = width;
        Height = height;
        Filter = TextureFilter.Bilinear;
        Tiling = Vec2.One;

        _texels = pixels;
    }

    public Color Get(Vec2i pos)
    {
        Debug.Assert(pos.X >= 0 && pos.X < Width);
        Debug.Assert(pos.Y >= 0 && pos.Y < Height);
        return _texels[(uint)pos.Y * Width + (uint)pos.X];
    }

    public Color Sample(Vec2 coord)
    {
        // Repeat wrap with tiling.
        float u = coord.X * Tiling.X; u -= MathF.Floor(u);
        float v = coord.Y * Tiling.Y; v -= MathF.Floor(v);

        switch (Filter)
        {
            case TextureFilter.Point:
                uint x = (uint)(u * Width) % Width;
                uint y = (uint)(v * Height) % Height;
                return _texels[y * Width + x];

            default:
            case TextureFilter.Bilinear:
                float fx = u * (Width - 1);
                float fy = v * (Height - 1);
                uint x0 = (uint)fx;
                uint y0 = (uint)fy;
                uint x1 = (x0 + 1) % Width;
                uint y1 = (y0 + 1) % Height;
                float tx = fx - x0;
                float ty = fy - y0;
                return Color.Bilerp(
                    _texels[y0 * Width + x0],
                    _texels[y0 * Width + x1],
                    _texels[y1 * Width + x0],
                    _texels[y1 * Width + x1],
                    tx, ty);
        }
    }

    public Vec3 SampleNormal(Vec2 coord)
    {
        Color c = Sample(coord);
        return new Vec3(c.R * 2f - 1f, -(c.G * 2f - 1f), c.B * 2f - 1f).NormalizeOr(Vec3.Up);
    }

    public void Analyze(
        ReadOnlySpan<float> histogramThresholds,
        Span<int> histogram,
        out float peakLum,
        out Color peakColor,
        out float avgLum)
    {
        Debug.Assert(histogram.Length == histogramThresholds.Length + 1);

        histogram.Clear();

        peakLum = 0f;
        peakColor = Color.Black;
        double totalLum = 0.0;

        foreach (Color c in _texels)
        {
            float lum = c.Luminance;
            totalLum += lum;
            if (lum > peakLum)
            {
                peakLum = lum;
                peakColor = c;
            }
            int bucket = histogramThresholds.Length;
            for (int i = 0; i != histogramThresholds.Length; ++i)
            {
                if (lum < histogramThresholds[i])
                {
                    bucket = i;
                    break;
                }
            }
            ++histogram[bucket];
        }
        avgLum = (float)(totalLum / _texels.Length);
    }

    public void Describe(ref FormatWriter fmt)
    {
        Span<float> histThresholds = stackalloc float[] { 1f, 10f, 100f, 1000f };
        Span<int> hist = stackalloc int[histThresholds.Length + 1];
        Analyze(histThresholds, hist, out float peakLum, out Color peakColor, out float avgLum);

        fmt.WriteLine($"size={Width}x{Height}");
        fmt.WriteLine($"lumPeak={peakLum:G4} peakColor={peakColor}");
        fmt.WriteLine($"lumAvg={avgLum:G4}");
        fmt.WriteLine($"lumDynamicRange={peakLum / MathF.Max(avgLum, 1e-6f):G3}x");
        fmt.WriteLine("lumHistogram");
        fmt.IndentPush();
        for (int i = 0; i < histThresholds.Length; ++i)
        {
            fmt.WriteLine($"[<{histThresholds[i]:F0}]={hist[i]}");
        }
        fmt.WriteLine($"[>={histThresholds[^1]:F0}]={hist[histThresholds.Length]}");
        fmt.IndentPop();
    }

    public static Texture FromSrgb(Image image)
    {
        Color[] texels = new Color[image.Pixels.Length];
        for (int i = 0; i != texels.Length; ++i)
            texels[i] = Color.FromPixel(image.Pixels[i]);
        return new Texture(texels, image.Width, image.Height);
    }

    public static Texture FromLinear(Image image)
    {
        Color[] texels = new Color[image.Pixels.Length];
        for (int i = 0; i != texels.Length; ++i)
            texels[i] = Color.FromPixelLinear(image.Pixels[i]);
        return new Texture(texels, image.Width, image.Height);
    }

    public static Texture FromHdr(ImageHdr image)
    {
        Color[] texels = new Color[image.Pixels.Length];
        for (int i = 0; i != texels.Length; ++i)
            texels[i] = Color.FromPixel(image.Pixels[i]);
        return new Texture(texels, image.Width, image.Height);
    }

    public static Texture Checker(Color a, Color b, uint size = 8)
    {
        Color[] texels = new Color[size * size];
        for (uint y = 0; y != size; ++y)
        {
            for (uint x = 0; x != size; ++x)
            {
                texels[y * size + x] = ((x + y) % 2 == 0) ? a : b;
            }
        }
        Texture res = new Texture(texels, size, size);
        res.Filter = TextureFilter.Point;
        return res;
    }
}
