
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

    public Color Radiance(Ray ray)
    {
        const float bias = 0.0001f;
        float topBlend = 1f - MathF.Pow(MathF.Min(1f, 1f + bias - ray.Dir.Y), 4f);
        float bottomBlend = 1f - MathF.Pow(MathF.Min(1f, 1f + bias + ray.Dir.Y), 40f);
        float middleBlend = 1f - topBlend - bottomBlend;
        Color sky = RadianceTop * topBlend + RadianceMiddle * middleBlend + RadianceBottom * bottomBlend;

        float sunDot = Vec3.Dot(ray.Dir, SunDir);
        float sunBlend = MathF.Max(0f, (sunDot - SunCosAngle) / (1f - SunCosAngle));
        return sky + SunRadiance * sunBlend;
    }
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
        Color radiance = Color.Black, color = Color.White;
        for (uint i = 0; i != (bounces + 1); ++i)
        {
            Fragment frag = Trace(ray);
            radiance += frag.Radiance * color;

            if (frag.Hit is RayHit hit)
            {
                ray.Origin = ray[hit.Dist] + hit.Norm * 1e-4f;
                ray.Dir = (hit.Norm + Vec3.RandOnSphere(ref rng)).Normalize(); // Cosine distruction.

                if (frag.Material is Material material)
                {
                    color *= material.Color;
                }
            }
            else
            {
                break; // Bounced out of the scene.
            }
        }
        return radiance;
    }
}
