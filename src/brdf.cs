using System;
using System.Diagnostics;

/**
 * BRDF (Bidirectional Reflectance Distribution Function) utilities.
 */
static class Brdf
{
    // Base reflectivity (F0) for the Fresnel equations.
    // For dielectrics derived from IOR via Schlick, for metals tinted by albedo.
    // https://en.wikipedia.org/wiki/Fresnel_equations
    public static Color BaseReflectivity(float ior, Color albedo, float metallic)
    {
        float f0 = MathF.Pow((ior - 1f) / (ior + 1f), 2f); // Fresnel at normal incidence.
        return Color.Lerp(new Color(f0), albedo, metallic);
    }

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
    // rDotL: dot product of the incoming ray direction and the scattered direction.
    // https://en.wikipedia.org/wiki/Henyey%E2%80%93Greenstein_phase_function
    public static float HgPdf(float rDotL, float g)
    {
        Debug.Assert(g > -1f && g < 1f);
        float gSqr = g * g;
        float d = 1f + gSqr - 2f * g * rDotL;
        return (1f - gSqr) / (4f * MathF.PI * d * MathF.Sqrt(d));
    }

    // Snell's law refraction.
    // https://en.wikipedia.org/wiki/Snell%27s_law
    // iorRatio: ratio of current IOR to next IOR.
    public static Vec3? Refract(Vec3 incomingDir, Vec3 normal, float iorRatio)
    {
        Debug.Assert(incomingDir.IsUnit && normal.IsUnit, "Dirs must be normalized");
        float cosI = -Vec3.Dot(incomingDir, normal);
        float sin2T = iorRatio * iorRatio * (1f - cosI * cosI); // sinSqr transmitted via Snell's law.
        if (sin2T >= 1f)
            return null; // Reflect back inside.
        float cosT = MathF.Sqrt(1f - sin2T);
        return iorRatio * incomingDir + (iorRatio * cosI - cosT) * normal;
    }

    // Beer-Lambert law: transmittance of light through an absorbing medium.
    // https://en.wikipedia.org/wiki/Beer%E2%80%93Lambert_law
    public static Color BeerLawTransmittance(Color albedo, float distance) => new Color(
        MathF.Pow(MathF.Max(albedo.R, 0f), distance),
        MathF.Pow(MathF.Max(albedo.G, 0f), distance),
        MathF.Pow(MathF.Max(albedo.B, 0f), distance));

    // Henyey-Greenstein phase function for scattering media.
    // https://en.wikipedia.org/wiki/Henyey%E2%80%93Greenstein_phase_function
    public static Vec3 HgScatterDir(Vec3 forward, float g, ref Rng rng)
    {
        Debug.Assert(forward.IsUnit);
        Debug.Assert(g > -1f && g < 1f);

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
