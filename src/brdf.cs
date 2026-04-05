using System;

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
        float d = nDotH * nDotH * (alpha2 - 1f) + 1f;
        return alpha2 / (MathF.PI * d * d);
    }

    // Fraction of microfacets visible from a given direction (Smith shadowing-masking).
    public static float SmithG1(float nDotX, float roughnessSqr)
    {
        float k = roughnessSqr * roughnessSqr / 2f;
        return nDotX / (nDotX * (1f - k) + k);
    }
}
