using System;

enum TextureFilter { Bilinear, Point }

class Texture
{
    public readonly uint Width, Height;
    public readonly TextureFilter Filter;

    private readonly Color[] _texels;

    private Texture(
        Color[] pixels,
        uint width,
        uint height,
        TextureFilter filter = TextureFilter.Bilinear)
    {
        Width = width;
        Height = height;
        Filter = filter;

        _texels = pixels;
    }

    public Color Sample(Vec2 coord)
    {
        // Repeat wrap.
        float u = coord.X - MathF.Floor(coord.X);
        float v = coord.Y - MathF.Floor(coord.Y);

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
        return new Vec3(c.R * 2f - 1f, c.G * 2f - 1f, c.B * 2f - 1f).NormalizeOr(Vec3.Up);
    }

    public static Texture FromSrgb(Image image, TextureFilter filter = TextureFilter.Bilinear)
    {
        Color[] texels = new Color[image.Pixels.Length];
        for (int i = 0; i != texels.Length; ++i)
            texels[i] = Color.FromPixel(image.Pixels[i]);
        return new Texture(texels, image.Width, image.Height, filter);
    }

    public static Texture FromNormal(Image image, TextureFilter filter = TextureFilter.Bilinear)
    {
        Color[] texels = new Color[image.Pixels.Length];
        for (int i = 0; i != texels.Length; ++i)
            texels[i] = Color.FromPixelLinear(image.Pixels[i]);
        return new Texture(texels, image.Width, image.Height, filter);
    }

    public static Texture Checker(Color a, Color b, uint size = 8, TextureFilter filter = TextureFilter.Point)
    {
        Color[] texels = new Color[size * size];
        for (uint y = 0; y != size; ++y)
        {
            for (uint x = 0; x != size; ++x)
            {
                texels[y * size + x] = ((x + y) % 2 == 0) ? a : b;
            }
        }
        return new Texture(texels, size, size, filter);
    }
}
