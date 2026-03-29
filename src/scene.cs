
using System;
using System.Collections.Generic;

struct Surface
{
    public RayHit? Hit;
    public Material? Material;
    public Color Radiance;

    public Surface(RayHit? hit, Material? material, Color radiance)
    {
        Hit = hit;
        Material = material;
        Radiance = radiance;
    }
}

struct Material
{
    public Color Color;
    public float Roughness;
    public float Metallic;
    public Color Radiance;

    public Material(Color color, float roughness, float metallic = 0f, Color radiance = default)
    {
        Color = color;
        Roughness = roughness;
        Metallic = metallic;
        Radiance = radiance;
    }
}

struct Object
{
    public Transform Trans;
    public Material Material;
    public IShape Shape;

    public Object(Transform trans, Material material, IShape shape)
    {
        Trans = trans;
        Material = material;
        Shape = shape;
    }

    public RayHit? Intersect(Ray ray) => Shape.Intersect(ray, Trans);
}

struct Sky
{
    public Color RadianceTop;
    public Color RadianceMiddle;
    public Color RadianceBottom;
    public Vec3 SunDir;
    public Color SunRadiance;
    public float SunAngle;
    public float SunAngleCos;

    public Sky(
        Color radianceTop,
        Color radianceMiddle,
        Color radianceBottom,
        Vec3 sunDir,
        Color sunRadiance,
        float sunAngle)
    {
        RadianceTop = radianceTop;
        RadianceMiddle = radianceMiddle;
        RadianceBottom = radianceBottom;
        SunDir = sunDir;
        SunRadiance = sunRadiance;
        SunAngle = sunAngle;
        SunAngleCos = MathF.Cos(sunAngle);
    }

    public Color AmbientRadianceRay(Ray ray)
    {
        const float bias = 0.0001f;
        float topBlend = 1f - MathF.Pow(MathF.Min(1f, 1f + bias - ray.Dir.Y), 4f);
        float bottomBlend = 1f - MathF.Pow(MathF.Min(1f, 1f + bias + ray.Dir.Y), 40f);
        float middleBlend = 1f - topBlend - bottomBlend;
        return RadianceTop * topBlend + RadianceMiddle * middleBlend + RadianceBottom * bottomBlend;
    }

    public Color SunRadianceRay(Ray ray)
    {
        float sunDot = Vec3.Dot(ray.Dir, SunDir);
        float sunBlend = MathF.Max(0f, (sunDot - SunAngleCos) / (1f - SunAngleCos));
        return SunRadiance * sunBlend;
    }

    public Vec3 SunSampleDir(ref Rng rng)
    {
        return Quat.Look(SunDir, new Vec3(0, 1, 0)) * Vec3.RandInCone(ref rng, SunAngle);
    }

    public Color RadianceRay(Ray ray) => AmbientRadianceRay(ray) + SunRadianceRay(ray);
}

class Scene
{
    private List<Object> _objects = new List<Object>();
    private Sky _sky;
    private bool _locked;

    public Scene(Sky sky)
    {
        _sky = sky;
    }

    public void Lock() => _locked = true;

    public void AddObject(Object obj)
    {
        if (_locked)
            throw new InvalidOperationException("Scene locked");
        _objects.Add(obj);
    }

    private bool Occluded(Ray ray)
    {
        foreach (Object obj in _objects)
        {
            if (obj.Intersect(ray) is RayHit)
                return true;
        }
        return false;
    }

    public Surface Trace(Ray ray)
    {
        RayHit? closestHit = null;
        Material? closestMaterial = null;
        foreach (Object obj in _objects)
        {
            if (obj.Intersect(ray) is RayHit hit && (closestHit is null || hit.Dist < closestHit.Value.Dist))
            {
                closestHit = hit;
                closestMaterial = obj.Material;
            }
        }

        if (closestHit is RayHit h)
            return new Surface(h, closestMaterial, closestMaterial!.Value.Radiance);

        return new Surface(null, null, _sky.AmbientRadianceRay(ray));
    }

    public (Color Radiance, Vec3? Normal) Sample(Ray ray, ref Rng rng, uint bounces)
    {
        Color radiance = Color.Black, energy = Color.White;
        Vec3? normal = null;

        for (uint i = 0; i != (bounces + 1); ++i)
        {
            bool isPrimary = i == 0;
            Surface surf = Trace(ray);

            // Accumulate radiance.
            radiance += surf.Radiance * energy;

            // Absorb some of the light frequencies.
            float roughness = 1.0f;
            if (surf.Material is Material material)
            {
                Color specularColor = Color.Lerp(Color.White, material.Color, material.Metallic);
                energy *= Color.Lerp(material.Color, specularColor, 1f - roughness);
                roughness = material.Roughness;
            }

            if (surf.Hit is RayHit hit)
            {
                if (isPrimary)
                    normal = hit.Norm;

                Vec3 hitPos = ray[hit.Dist] + hit.Norm * 1e-4f;

                // Direct sun contribution.
                Vec3 sunDir = _sky.SunSampleDir(ref rng);
                float sunCosTheta = Vec3.Dot(hit.Norm, sunDir);
                if (sunCosTheta > 0f && roughness > 0.05f)
                {
                    Ray shadowRay = new Ray(hitPos, sunDir);
                    if (!Occluded(shadowRay))
                        radiance += _sky.SunRadiance * energy * sunCosTheta;
                }

                // Russian roulette: terminate low-energy paths, compensate survivors.
                if (i >= 3)
                {
                    float survive = MathF.Max(energy.R, MathF.Max(energy.G, energy.B));
                    if (rng.NextFloat() >= survive)
                        break;
                    energy /= survive;
                }

                // Compute scatter ray.
                Vec3 scatterDirDiffuse = (hit.Norm + Vec3.RandOnSphere(ref rng)).NormalizeOr(hit.Norm); // Cosine-weighted distribution.
                Vec3 scatterDirSpecular = Vec3.Reflect(ray.Dir, hit.Norm);
                Vec3 scatterDir = Vec3.Lerp(scatterDirSpecular, scatterDirDiffuse, roughness).NormalizeOr(hit.Norm);

                ray = new Ray(hitPos, scatterDir);
            }
            else
            {
                // Add sun contibution for primary rays.
                if (isPrimary)
                {
                    radiance += _sky.SunRadianceRay(ray) * energy;
                }
                break;
            }
        }
        return (radiance, normal);
    }
}
