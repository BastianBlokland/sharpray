using System;
using System.Diagnostics;
using System.Threading.Tasks;

class Denoiser
{
    public float SigmaSpace; // Blur radius in pixels; higher = smoother.
    public float SigmaColor; // Color similarity threshold; lower = preserves more edges.
    public float SigmaNormal; // Normal similarity threshold; lower = respects geometry boundaries more.

    private int _radius;
    private float _sigmaSpaceSqr2;
    private float _sigmaColorSqr2;
    private float _sigmaNormalSqr2;

    public Denoiser(float sigmaSpace, float sigmaColor, float sigmaNormal)
    {
        SigmaSpace = sigmaSpace;
        SigmaColor = sigmaColor;
        SigmaNormal = sigmaNormal;

        _radius = (int)MathF.Ceiling(sigmaSpace * 2f);
        _sigmaSpaceSqr2 = sigmaSpace * sigmaSpace * 2f;
        _sigmaColorSqr2 = sigmaColor * sigmaColor * 2f;
        _sigmaNormalSqr2 = sigmaNormal * sigmaNormal * 2f;
    }

    public Image Denoise(Image img, Vec3[] normals)
    {
        Debug.Assert(img.Pixels.Length == normals.Length);

        Image result = new Image(img.Width, img.Height);
        Parallel.For(0, (int)img.Height, y =>
        {
            for (int x = 0; x < img.Width; ++x)
            {
                result.Pixels[y * img.Width + x] = Filter(img, normals, x, y);
            }
        });
        return result;
    }

    private Pixel Filter(Image img, Vec3[] normals, int x, int y)
    {
        // Joint bilateral filter denoiser, using color and surface normals as guidance.
        // https://en.wikipedia.org/wiki/Bilateral_filter

        Color centerColor = Color.FromPixel(img.Pixels[y * img.Width + x]);
        Vec3 centerNormal = normals[y * img.Width + x];
        bool hasCenterNormal = centerNormal.MagnitudeSqr() > 0f;

        float weightSum = 0f;
        Color colorSum = Color.Black;

        for (int kernelY = -_radius; kernelY <= _radius; ++kernelY)
        {
            for (int kernelX = -_radius; kernelX <= _radius; ++kernelX)
            {
                int refX = x + kernelX;
                int refY = y + kernelY;
                if (refX < 0 || refX >= img.Width || refY < 0 || refY >= img.Height)
                    continue; // Out of bounds.

                Color refColor = Color.FromPixel(img.Pixels[refY * img.Width + refX]);
                Vec3 refNormal = normals[refY * img.Width + refX];

                float spatialDist = kernelX * kernelX + kernelY * kernelY;
                float weight = MathF.Exp(-spatialDist / _sigmaSpaceSqr2);

                Color colorDelta = centerColor - refColor;
                weight *= MathF.Exp(-(colorDelta.R * colorDelta.R + colorDelta.G * colorDelta.G + colorDelta.B * colorDelta.B) / _sigmaColorSqr2);

                if (hasCenterNormal && refNormal.MagnitudeSqr() > 0f)
                {
                    Vec3 normalDelta = centerNormal - refNormal;
                    weight *= MathF.Exp(-normalDelta.MagnitudeSqr() / _sigmaNormalSqr2);
                }

                weightSum += weight;
                colorSum += refColor * weight;
            }
        }

        return (colorSum / weightSum).ToPixel();
    }
}
