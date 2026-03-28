using System;
using System.Diagnostics;
using System.Threading.Tasks;

class Compositor
{
    public float SigmaSpace; // Blur radius in pixels; higher = smoother.
    public float SigmaColor; // Radiance similarity threshold; lower = preserves more edges.
    public float SigmaNormal; // Normal similarity threshold; lower = respects geometry boundaries more.

    private int _radius;
    private float _sigmaSpaceSqr2;
    private float _sigmaColorSqr2;
    private float _sigmaNormalSqr2;

    public Compositor(float sigmaSpace, float sigmaColor, float sigmaNormal)
    {
        SigmaSpace = sigmaSpace;
        SigmaColor = sigmaColor;
        SigmaNormal = sigmaNormal;

        _radius = (int)MathF.Ceiling(sigmaSpace * 2f);
        _sigmaSpaceSqr2 = sigmaSpace * sigmaSpace * 2f;
        _sigmaColorSqr2 = sigmaColor * sigmaColor * 2f;
        _sigmaNormalSqr2 = sigmaNormal * sigmaNormal * 2f;
    }

    public Image Preview(Color[] radiance, uint width, uint height)
    {
        Debug.Assert(radiance.Length == width * height);

        Image result = new Image(width, height);
        for (uint i = 0; i != width * height; ++i)
        {
            result.Pixels[i] = Tonemap(radiance[i]);
        }
        return result;
    }

    public Image Compose(Color[] radiance, Vec3[] normals, uint width, uint height)
    {
        Debug.Assert(radiance.Length == normals.Length);
        Debug.Assert(radiance.Length == width * height);

        Image result = new Image(width, height);
        Parallel.For(0, (int)height, y =>
        {
            for (int x = 0; x != width; ++x)
            {
                Color filtered = Filter(radiance, normals, (int)width, (int)height, x, y);
                result.Pixels[y * width + x] = Tonemap(filtered);
            }
        });
        return result;
    }

    private Color Filter(Color[] radiance, Vec3[] normals, int width, int height, int x, int y)
    {
        // Joint bilateral filter with normal guidance.
        // https://en.wikipedia.org/wiki/Bilateral_filter

        Color centerRadiance = radiance[y * width + x];
        Vec3 centerNormal = normals[y * width + x];
        bool hasCenterNormal = centerNormal.MagnitudeSqr() > 0f;

        float weightSum = 0f;
        Color radianceSum = Color.Black;

        for (int kernelY = -_radius; kernelY <= _radius; ++kernelY)
        {
            for (int kernelX = -_radius; kernelX <= _radius; ++kernelX)
            {
                int refX = x + kernelX;
                int refY = y + kernelY;
                if (refX < 0 || refX >= width || refY < 0 || refY >= height)
                    continue;

                Color refRadiance = radiance[refY * width + refX];
                Vec3 refNormal = normals[refY * width + refX];

                float spatialDist = kernelX * kernelX + kernelY * kernelY;
                float weight = MathF.Exp(-spatialDist / _sigmaSpaceSqr2);

                Color radianceDelta = centerRadiance - refRadiance;
                weight *= MathF.Exp(-(radianceDelta.R * radianceDelta.R + radianceDelta.G * radianceDelta.G + radianceDelta.B * radianceDelta.B) / _sigmaColorSqr2);

                if (hasCenterNormal && refNormal.MagnitudeSqr() > 0f)
                {
                    Vec3 normalDelta = centerNormal - refNormal;
                    weight *= MathF.Exp(-normalDelta.MagnitudeSqr() / _sigmaNormalSqr2);
                }

                weightSum += weight;
                radianceSum += refRadiance * weight;
            }
        }

        return radianceSum / weightSum;
    }

    private static Pixel Tonemap(Color radiance)
    {
        // Linear with shoulder region.
        // By user SteveM in comment section on https://mynameismjp.wordpress.com.
        // https://mynameismjp.wordpress.com/2010/04/30/a-closer-look-at-tone-mapping/#comment-118287

        const float a = 1.8f; // Mid.
        const float b = 1.4f; // Toe.
        const float c = 0.5f; // Shoulder.
        const float d = 1.5f; // Mid.

        Color result = (radiance * (a * radiance + new Color(b))) / (radiance * (a * radiance + new Color(c)) + new Color(d));
        return result.ToPixel();
    }
}
