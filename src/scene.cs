
using System;
using System.Collections.Generic;

struct Fragment
{
    public RayHit? Hit;
    public Material? Material;
    public Color Radiance;

    public Fragment(RayHit? hit, Material? material, Color radiance)
    {
        Hit = hit;
        Material = material;
        Radiance = radiance;
    }
}

struct Material
{
    public Color Color;
    public Color Radiance;

    public Material(Color color, Color radiance = default)
    {
        Color = color;
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
    public float SunCosAngle;

    public Sky(
        Color radianceTop,
        Color radianceMiddle,
        Color radianceBottom,
        Vec3 sunDir,
        Color sunRadiance,
        float sunCosAngle)
    {
        RadianceTop = radianceTop;
        RadianceMiddle = radianceMiddle;
        RadianceBottom = radianceBottom;
        SunDir = sunDir;
        SunRadiance = sunRadiance;
        SunCosAngle = sunCosAngle;
    }

    public Color RadianceAmbient(Ray ray)
    {
        const float bias = 0.0001f;
        float topBlend = 1f - MathF.Pow(MathF.Min(1f, 1f + bias - ray.Dir.Y), 4f);
        float bottomBlend = 1f - MathF.Pow(MathF.Min(1f, 1f + bias + ray.Dir.Y), 40f);
        float middleBlend = 1f - topBlend - bottomBlend;
        return RadianceTop * topBlend + RadianceMiddle * middleBlend + RadianceBottom * bottomBlend;
    }

    public Color RadianceSun(Ray ray)
    {
        float sunDot = Vec3.Dot(ray.Dir, SunDir);
        float sunBlend = MathF.Max(0f, (sunDot - SunCosAngle) / (1f - SunCosAngle));
        return SunRadiance * sunBlend;
    }

    public Color Radiance(Ray ray) => RadianceAmbient(ray) + RadianceSun(ray);
}

class Scene
{
    private List<Object> _objects = new List<Object>();
    private Sky _sky;

    public Scene(Sky sky)
    {
        _sky = sky;
    }

    public void AddObject(Object obj) => _objects.Add(obj);

    public Fragment Trace(Ray ray)
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
            return new Fragment(h, closestMaterial, closestMaterial!.Value.Radiance);

        return new Fragment(null, null, _sky.Radiance(ray));
    }

    public Color Sample(Ray ray, ref Rng rng, uint bounces)
    {
        Color radiance = Color.Black, energy = Color.White;
        for (uint i = 0; i != (bounces + 1); ++i)
        {
            Fragment frag = Trace(ray);

            // Accumulate radiance.
            radiance += frag.Radiance * energy;

            // Absorb some of the light frequencies.
            if (frag.Material is Material material)
            {
                energy *= material.Color;
            }

            // Scatter.
            if (frag.Hit is RayHit hit)
            {
                // Russian roulette: terminate low-energy paths, compensate survivors.
                if (i >= 3)
                {
                    float survive = MathF.Max(energy.R, MathF.Max(energy.G, energy.B));
                    if (rng.NextFloat() >= survive)
                        break;
                    energy = energy / survive;
                }

                // Compute scatter ray.
                Vec3 scatterOrigin = ray[hit.Dist] + hit.Norm * 1e-4f;
                Vec3 scatterDir = (hit.Norm + Vec3.RandOnSphere(ref rng)).NormalizeOr(hit.Norm);  // Cosine-weighted distribution.
                ray = new Ray(scatterOrigin, scatterDir);
            }
            else
            {
                break; // Bounced out of the scene.
            }
        }
        return radiance;
    }
}
