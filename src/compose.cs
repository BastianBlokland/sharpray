using System;
using System.Diagnostics;
using System.Threading.Tasks;

enum Tonemapper { Reinhard, LinearSmooth }

class Compositor
{
    private Tonemapper _tonemapper;
    private float _exposure;
    private int _radius;
    private float _sigmaPixelsSqr2;
    private float _sigmaNormalSqr2;
    private float _sigmaDepthSqr2;
    private float _varianceScale;
    private float _varianceMax;
    private float _luminanceScale;
    private float _luminanceExponent;
    private Counters _counters;

    public Compositor(
        Tonemapper tonemapper,
        float exposure,
        float sigmaPixels, // Blur radius in pixels; higher = smoother.
        float sigmaNormal, // Normal similarity threshold; lower = respects geometry boundaries more.
        float sigmaDepth, // Depth similarity threshold in world units; lower = sharper depth edges.
        float varianceScale,
        float varianceMax,
        float luminanceScale,
        float luminanceExponent,
        Counters counters)
    {
        _tonemapper = tonemapper;
        _exposure = exposure;
        _radius = (int)MathF.Ceiling(sigmaPixels * 2f);
        _sigmaPixelsSqr2 = sigmaPixels * sigmaPixels * 2f;
        _sigmaNormalSqr2 = sigmaNormal * sigmaNormal * 2f;
        _sigmaDepthSqr2 = sigmaDepth * sigmaDepth * 2f;
        _varianceScale = varianceScale;
        _varianceMax = varianceMax;
        _luminanceScale = luminanceScale;
        _luminanceExponent = luminanceExponent;
        _counters = counters;
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
                    rend.Size,
                    new Vec2i(x, y));
                result.Pixels[y * rend.Width + x] = Tonemap(filtered);
            }
            _counters.Flush();
        });

        overlay?.Draw(result, rend.View, rend.Depth, _counters);
    }

    private Color Filter(
        Color[] radiance,
        Vec3[] normals,
        float[] depth,
        float[] variance,
        Vec2i size,
        Vec2i coord)
    {
        // Joint bilateral filter with normal and depth guidance.
        // https://en.wikipedia.org/wiki/Bilateral_filter

        long[] counterData = _counters.GetLocalData();

        int centerIndex = coord.Y * size.X + coord.X;
        Color centerRadiance = radiance[centerIndex];
        Vec3 centerNormal = normals[centerIndex];
        float centerDepth = depth[centerIndex];
        bool hasCenterNormal = centerNormal.MagnitudeSqr() > 0f;
        bool hasCenterDepth = !float.IsInfinity(centerDepth);

        float luminance = centerRadiance.Luminance;
        float luminanceBoost = 1f + MathF.Pow(luminance, _luminanceExponent) * _luminanceScale;
        float varianceWeight = MathF.Min(variance[centerIndex] * luminanceBoost, _varianceMax);
        float effectiveSigmaNormalSqr2 = _sigmaNormalSqr2 + _varianceScale * varianceWeight;
        float effectiveSigmaDepthSqr2 = _sigmaDepthSqr2 + _varianceScale * varianceWeight;

        float weightSum = 0f;
        Color radianceSum = Color.Black;

        for (int kernelY = -_radius; kernelY <= _radius; ++kernelY)
        {
            for (int kernelX = -_radius; kernelX <= _radius; ++kernelX)
            {
                if (kernelX == 0 && kernelY == 0)
                {
                    // Center pixel always contributes fully.
                    weightSum += 1f;
                    radianceSum += centerRadiance;
                    continue;
                }

                int refX = coord.X + kernelX;
                int refY = coord.Y + kernelY;
                if (refX < 0 || refX >= size.X || refY < 0 || refY >= size.Y)
                    continue; // Outside bounds.

                int refIndex = refY * size.X + refX;
                Vec3 refNormal = normals[refIndex];
                float refDepth = depth[refIndex];
                bool refHasNormal = refNormal.MagnitudeSqr() > 0f;
                bool refHasDepth = !float.IsInfinity(refDepth);

                // Reject neighbors that differ where not the same information is available.
                if (hasCenterNormal != refHasNormal || hasCenterDepth != refHasDepth)
                    continue;

                float kernelDist = kernelX * kernelX + kernelY * kernelY;
                float weight = varianceWeight * MathF.Exp(-kernelDist / _sigmaPixelsSqr2);

                if (hasCenterNormal)
                {
                    Vec3 normalDelta = centerNormal - refNormal;
                    weight *= MathF.Exp(-normalDelta.MagnitudeSqr() / effectiveSigmaNormalSqr2);
                }
                if (hasCenterDepth)
                {
                    float depthDelta = centerDepth - refDepth;
                    weight *= MathF.Exp(-(depthDelta * depthDelta) / effectiveSigmaDepthSqr2);
                }

                weightSum += weight;
                radianceSum += radiance[refIndex] * weight;

                ++counterData[(int)Counters.Type.ComposeFilterSample];
            }
        }

        Debug.Assert(weightSum > 0f);
        return radianceSum / weightSum;
    }

    private Pixel Tonemap(Color radiance)
    {
        Color x = radiance * _exposure;
        switch (_tonemapper)
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
                throw new InvalidOperationException($"Unknown tonemapper: {_tonemapper}");
        }
    }
}
