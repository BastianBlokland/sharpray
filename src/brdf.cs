using System;
using System.Diagnostics;

/**
 * BRDF (Bidirectional Reflectance Distribution Function) utilities.
 */
static class Brdf
{
    // Schlick approximation of the Fresnel equations.
    // https://en.wikipedia.org/wiki/Schlick%27s_approximation
    public static Color Fresnel(float nDotV, Color baseReflectivity)
    {
        float x = 1f - nDotV;
        float x2 = x * x;
        float x5 = x2 * x2 * x;
        return baseReflectivity + (Color.White - baseReflectivity) * x5;
    }

    // GGX normal distribution function.
    public static float GgxD(float nDotH, float roughnessSqr)
    {
        float alpha2 = roughnessSqr * roughnessSqr;
        float nDotHSqr = nDotH * nDotH;
        float d = (1f - nDotHSqr) + nDotHSqr * alpha2;
        return alpha2 / (MathF.PI * d * d);
    }

    // Fraction of microfacets visible from a given direction (Smith shadowing-masking).
    public static float SmithG1(float nDotX, float roughnessSqr)
    {
        float k = roughnessSqr / 2f;
        return nDotX / (nDotX * (1f - k) + k);
    }

    // Importance-samples the GGX distribution, returning a half vector in local (z-up) tangent space.
    public static Vec3 GgxSampleLocal(float roughnessSqr, Vec2 u)
    {
        float cosTheta = MathF.Sqrt((1f - u.X) / (u.X * (roughnessSqr * roughnessSqr - 1f) + 1f));
        float sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - cosTheta * cosTheta));
        float phi = 2f * MathF.PI * u.Y;
        return new Vec3(sinTheta * MathF.Cos(phi), sinTheta * MathF.Sin(phi), cosTheta);
    }

    // MIS (Multiple Importance Sampling) power heuristic.
    public static float PowerHeuristic(float pdf1, float pdf2)
    {
        Debug.Assert(float.IsFinite(pdf1) && float.IsFinite(pdf2));
        float p1 = pdf1 * pdf1;
        float p2 = pdf2 * pdf2;
        return p1 + p2 > 0f ? p1 / (p1 + p2) : 0f;
    }
}
