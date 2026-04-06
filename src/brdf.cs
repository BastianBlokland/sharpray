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

    // Henyey-Greenstein phase function PDF.
    // cosTheta: dot product of the incoming and scattered directions.
    // https://en.wikipedia.org/wiki/Henyey%E2%80%93Greenstein_phase_function
    public static float HgPdf(float cosTheta, float g)
    {
        Debug.Assert(g >= -1f && g <= 1f);
        float gSqr = g * g;
        float d = 1f + gSqr - 2f * g * cosTheta;
        return (1f - gSqr) / (4f * MathF.PI * d * MathF.Sqrt(d));
    }

    // Henyey-Greenstein phase function for scattering media.
    // https://en.wikipedia.org/wiki/Henyey%E2%80%93Greenstein_phase_function
    public static Vec3 HgScatterDir(Vec3 forward, float g, ref Rng rng)
    {
        Debug.Assert(forward.IsUnit);
        Debug.Assert(g >= -1f && g <= 1f);

        Vec2 u = Vec2.Rand(ref rng);

        float cosTheta;
        if (MathF.Abs(g) < 1e-3f)
        {
            cosTheta = 1f - 2f * u.X; // Isotropic.
        }
        else
        {
            float s = (1f - g * g) / (1f - g + 2f * g * u.X);
            cosTheta = (1f + g * g - s * s) / (2f * g);
        }
        cosTheta = float.Clamp(cosTheta, -1f, 1f);

        float sinTheta = MathF.Sqrt(1f - cosTheta * cosTheta);
        float phi = 2f * MathF.PI * u.Y;

        Vec3 tangent = forward.Perp();
        Vec3 bitangent = Vec3.Cross(forward, tangent);

        return cosTheta * forward + sinTheta * MathF.Cos(phi) * tangent + sinTheta * MathF.Sin(phi) * bitangent;
    }
}
