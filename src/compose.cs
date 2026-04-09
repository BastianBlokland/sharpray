using System;
using System.Diagnostics;
using System.Threading.Tasks;

enum Tonemapper { Reinhard, LinearSmooth }

class Compositor
{
    private Tonemapper _tonemapper;
    private float _exposure;
    private float _denoiseRadius;
    private float _denoiseStrength;
    private float _denoiseStrengthMax;
    private float _denoiseLuminanceBoost;
    private float _denoiseLuminanceLimitSqr2;
    private float _denoiseNormalLimitSqr2;
    private float _denoiseDepthLimitSqr2;
    private Counters _counters;

    public Compositor(
        Tonemapper tonemapper,
        float exposure,
        float denoiseRadius,        // Blur radius as a fraction of screen size.
        float denoiseStrength,      // How much to blur (driven by variance).
        float denoiseStrengthMax,   // Upper cap on blur strength.
        float denoiseLuminanceBoost, // Extra blur strength for bright pixels.
        float denoiseLuminanceLimit, // Suppresses neighbors much brighter than center (firefly suppression).
        float denoiseNormalLimit,   // Rejects neighbors with differing normals.
        float denoiseDepthLimit,    // Rejects neighbors with differing depth.
        Counters counters)
    {
        _tonemapper = tonemapper;
        _exposure = exposure;
        _denoiseRadius = denoiseRadius;
        _denoiseStrength = denoiseStrength;
        _denoiseStrengthMax = denoiseStrengthMax;
        _denoiseLuminanceBoost = denoiseLuminanceBoost;
        _denoiseLuminanceLimitSqr2 = denoiseLuminanceLimit * denoiseLuminanceLimit * 2f;
        _denoiseNormalLimitSqr2 = denoiseNormalLimit * denoiseNormalLimit * 2f;
        _denoiseDepthLimitSqr2 = denoiseDepthLimit * denoiseDepthLimit * 2f;
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

        float radiusPixels = _denoiseRadius * MathF.Sqrt((float)(size.X * size.Y));
        float radiusPixelsSqr2 = radiusPixels * radiusPixels * 2f;
        int kernelRadius = (int)MathF.Ceiling(radiusPixels * 3f);

        int centerIndex = coord.Y * size.X + coord.X;
        Color centerRadiance = radiance[centerIndex];
        Vec3 centerNormal = normals[centerIndex];
        float centerDepth = depth[centerIndex];
        bool hasCenterNormal = centerNormal.MagnitudeSqr() > 0f;
        bool hasCenterDepth = !float.IsInfinity(centerDepth);

        float luminance = centerRadiance.Luminance;
        float luminanceBoost = 1f + luminance * _denoiseLuminanceBoost;
        float denoiseWeight = MathF.Min(variance[centerIndex] * luminanceBoost * _denoiseStrength, _denoiseStrengthMax);

        if (denoiseWeight < 1e-4f)
            return centerRadiance;

        float weightSum = 0f;
        Color radianceSum = Color.Black;

        for (int kernelY = -kernelRadius; kernelY <= kernelRadius; ++kernelY)
        {
            for (int kernelX = -kernelRadius; kernelX <= kernelRadius; ++kernelX)
            {
                if (kernelX == 0 && kernelY == 0)
                {
                    // Center pixel always contributes fully.
                    weightSum += 1f;
                    radianceSum += centerRadiance;
                    continue;
                }

                int neighborX = coord.X + kernelX;
                int neighborY = coord.Y + kernelY;
                if (neighborX < 0 || neighborX >= size.X || neighborY < 0 || neighborY >= size.Y)
                    continue; // Outside bounds.

                int neighborIndex = neighborY * size.X + neighborX;
                Color neighborRadiance = radiance[neighborIndex];
                Vec3 neighborNormal = normals[neighborIndex];
                float neighborDepth = depth[neighborIndex];
                bool neighborHasNormal = neighborNormal.MagnitudeSqr() > 0f;
                bool neighborHasDepth = !float.IsInfinity(neighborDepth);

                // Reject neighbors that differ where not the same information is available.
                if (hasCenterNormal != neighborHasNormal || hasCenterDepth != neighborHasDepth)
                    continue;

                float kernelDist = kernelX * kernelX + kernelY * kernelY;
                float weight = denoiseWeight * MathF.Exp(-kernelDist / radiusPixelsSqr2);

                if (hasCenterNormal)
                {
                    Vec3 normalDelta = centerNormal - neighborNormal;
                    weight *= MathF.Exp(-normalDelta.MagnitudeSqr() / _denoiseNormalLimitSqr2);
                }
                if (hasCenterDepth)
                {
                    float depthDelta = centerDepth - neighborDepth;
                    weight *= MathF.Exp(-(depthDelta * depthDelta) / _denoiseDepthLimitSqr2);
                }

                // Suppress neighbors that are much brighter than the center (firefly rejection).
                float luminanceDelta = MathF.Max(0f, neighborRadiance.Luminance - luminance);
                weight *= MathF.Exp(-(luminanceDelta * luminanceDelta) / _denoiseLuminanceLimitSqr2);

                weightSum += weight;
                radianceSum += neighborRadiance * weight;

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
