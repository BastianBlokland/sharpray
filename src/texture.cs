using System;

class Texture
{
    public readonly uint Width, Height;

    private readonly Color[] _texels;

    private Texture(Color[] pixels, uint width, uint height)
    {
        Width = width;
        Height = height;
        _texels = pixels;
    }

    public Color Sample(Vec2 coords)
    {
        // Repeat wrap.
        float u = coords.X - MathF.Floor(coords.X);
        float v = coords.Y - MathF.Floor(coords.Y);

        // Bilinear filtering.
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

    public static Texture FromSrgb(Image image)
    {
        Color[] texels = new Color[image.Pixels.Length];
        for (int i = 0; i != texels.Length; ++i)
            texels[i] = Color.FromPixel(image.Pixels[i]);
        return new Texture(texels, image.Width, image.Height);
    }
}
