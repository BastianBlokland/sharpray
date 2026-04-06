using System;
using System.Diagnostics;
using System.Threading.Tasks;

enum Tonemapper { Reinhard, LinearSmooth }

class Compositor
{
    public Tonemapper Tonemapper;
    public float Exposure;

    public float SigmaSpace; // Blur radius in pixels; higher = smoother.
    public float SigmaColor; // Radiance similarity threshold; lower = preserves more edges.
    public float SigmaNormal; // Normal similarity threshold; lower = respects geometry boundaries more.
    public float SigmaDepth; // Depth similarity threshold in world units; lower = sharper depth edges.

    private int _radius;
    private float _sigmaSpaceSqr2;
    private float _sigmaColorSqr2;
    private float _sigmaNormalSqr2;
    private float _sigmaDepthSqr2;
    private float _varianceScale;
    private float _varianceMax;
    private Counters _counters;

    public Compositor(
        Tonemapper tonemapper,
        float exposure,
        float sigmaSpace,
        float sigmaColor,
        float sigmaNormal,
        float sigmaDepth,
        float varianceScale,
        float varianceMax,
        Counters counters)
    {
        Tonemapper = tonemapper;
        Exposure = exposure;
        SigmaSpace = sigmaSpace;
        SigmaColor = sigmaColor;
        SigmaNormal = sigmaNormal;
        SigmaDepth = sigmaDepth;

        _counters = counters;
        _radius = (int)MathF.Ceiling(sigmaSpace * 2f);
        _sigmaSpaceSqr2 = sigmaSpace * sigmaSpace * 2f;
        _sigmaColorSqr2 = sigmaColor * sigmaColor * 2f;
        _sigmaNormalSqr2 = sigmaNormal * sigmaNormal * 2f;
        _sigmaDepthSqr2 = sigmaDepth * sigmaDepth * 2f;
        _varianceScale = varianceScale;
        _varianceMax = varianceMax;
    }

    public Image Preview(Renderer rend, Overlay? overlay)
    {
        Image result = new Image(rend.Width, rend.Height);
        Preview(rend, overlay, result);
        return result;
    }

    public void Preview(Renderer rend, Overlay? overlay, Image result)
    {
        Debug.Assert(result.Width == rend.Width && result.Height == rend.Height);
        for (uint i = 0; i != rend.Width * rend.Height; ++i)
        {
            result.Pixels[i] = Tonemap(rend.Radiance[i]);
        }
        overlay?.Draw(result, rend.View, rend.Depth, _counters);
    }

    public Image Compose(Renderer rend, Overlay? overlay)
    {
        Image result = new Image(rend.Width, rend.Height);
        Compose(rend, overlay, result);
        return result;
    }

    public void Compose(Renderer rend, Overlay? overlay, Image result)
    {
        Debug.Assert(result.Width == rend.Width && result.Height == rend.Height);
        Parallel.For(0, (int)rend.Height, y =>
        {
            for (int x = 0; x != rend.Width; ++x)
            {
                Color filtered = Filter(
                    rend.Radiance,
                    rend.Normals,
                    rend.Depth,
                    rend.Variance,
                    (int)rend.Width,
                    (int)rend.Height,
                    x,
                    y);
                result.Pixels[y * rend.Width + x] = Tonemap(filtered);
            }
            _counters.Flush();
        });

        overlay?.Draw(result, rend.View, rend.Depth, _counters);
    }

    private Color Filter(Color[] radiance, Vec3[] normals, float[] depth, float[] variance, int width, int height, int x, int y)
    {
        // Joint bilateral filter with normal and depth guidance.
        // https://en.wikipedia.org/wiki/Bilateral_filter

        Color centerRadiance = radiance[y * width + x];
        Vec3 centerNormal = normals[y * width + x];
        float centerDepth = depth[y * width + x];
        bool hasCenterNormal = centerNormal.MagnitudeSqr() > 0f;
        bool hasCenterDepth = !float.IsInfinity(centerDepth);

        float centerVariance = variance[y * width + x];

        float weightSum = 0f;
        Color radianceSum = Color.Black;

        long[] counterData = _counters.GetLocalData();

        for (int kernelY = -_radius; kernelY <= _radius; ++kernelY)
        {
            for (int kernelX = -_radius; kernelX <= _radius; ++kernelX)
            {
                int refX = x + kernelX;
                int refY = y + kernelY;
                if (refX < 0 || refX >= width || refY < 0 || refY >= height)
                    continue;

                int refIndex = refY * width + refX;
                Color refRadiance = radiance[refIndex];
                Vec3 refNormal = normals[refIndex];
                float refDepth = depth[refIndex];

                // Widen color and normal sigmas based on combined variance of both pixels.
                float combinedVariance = MathF.Min(centerVariance + variance[refIndex], _varianceMax);
                float effectiveSigmaColorSqr2 = _sigmaColorSqr2 + _varianceScale * combinedVariance;
                float effectiveSigmaNormalSqr2 = _sigmaNormalSqr2 + _varianceScale * combinedVariance;

                float spatialDist = kernelX * kernelX + kernelY * kernelY;
                float weight = MathF.Exp(-spatialDist / _sigmaSpaceSqr2);

                Color radianceDelta = centerRadiance - refRadiance;
                weight *= MathF.Exp(-(radianceDelta.R * radianceDelta.R + radianceDelta.G * radianceDelta.G + radianceDelta.B * radianceDelta.B) / effectiveSigmaColorSqr2);

                if (hasCenterNormal && refNormal.MagnitudeSqr() > 0f)
                {
                    Vec3 normalDelta = centerNormal - refNormal;
                    weight *= MathF.Exp(-normalDelta.MagnitudeSqr() / effectiveSigmaNormalSqr2);
                }

                if (hasCenterDepth && !float.IsInfinity(refDepth))
                {
                    float depthDelta = centerDepth - refDepth;
                    weight *= MathF.Exp(-(depthDelta * depthDelta) / _sigmaDepthSqr2);
                }

                weightSum += weight;
                radianceSum += refRadiance * weight;

                ++counterData[(int)Counters.Type.ComposeFilterSample];
            }
        }

        Debug.Assert(weightSum > 0f);
        return radianceSum / weightSum;
    }

    private Pixel Tonemap(Color radiance)
    {
        Color x = radiance * Exposure;
        switch (Tonemapper)
        {
            case Tonemapper.Reinhard:
                return (x / (x + new Color(1f))).ToPixel();
            case Tonemapper.LinearSmooth:
                // Linear with shoulder region.
                // By user SteveM in comment section on https://mynameismjp.wordpress.com.
                // https://mynameismjp.wordpress.com/2010/04/30/a-closer-look-at-tone-mapping/#comment-118287
                const float a = 1.8f; // Mid.
                const float b = 1.4f; // Toe.
                const float c = 0.5f; // Shoulder.
                const float d = 1.5f; // Mid.
                return ((x * (a * x + new Color(b))) / (x * (a * x + new Color(c)) + new Color(d))).ToPixel();
            default:
                throw new InvalidOperationException($"Unknown tonemapper: {Tonemapper}");
        }
    }
}
