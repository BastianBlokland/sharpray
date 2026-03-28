
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

    public static Sky Default() => new Sky(
        new Color(0.4f, 0.5f, 0.8f),
        new Color(1.0f, 0.9f, 0.9f),
        new Color(0.5f, 0.425f, 0.275f),
        new Vec3(0.4f, 0.5f, 1f).Normalize(),
        new Color(4f, 3.5f, 2.5f),
        MathF.Cos(float.DegreesToRadians(2.6f)));
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

    public Fragment Trace(Ray ray, ref Rng rng)
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
}
