
using System;
using System.Collections.Generic;

struct TraceResult
{
    public RayHit? Hit;
    public Material? Material;
    public Color Radiance;

    public TraceResult(RayHit? hit, Material? material, Color radiance)
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

class Object
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

class Scene
{
    private List<Object> _objects = new List<Object>();

    static readonly Color _skyRadianceTop = new Color(0.4f, 0.5f, 0.8f);
    static readonly Color _skyRadianceMiddle = new Color(1.0f, 0.9f, 0.9f);
    static readonly Color _skyRadianceBottom = new Color(0.5f, 0.425f, 0.275f);

    static readonly Vec3 _sunDir = new Vec3(0.4f, 0.5f, 1f).Normalize();
    static readonly Color _sunRadiance = new Color(4f, 3.5f, 2.5f);
    const float _sunCosAngle = 0.999f; // ~2.6 degrees.

    public void AddObject(Object obj) => _objects.Add(obj);

    public TraceResult Trace(Ray ray, ref Rng rng)
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
            return new TraceResult(h, closestMaterial, closestMaterial!.Value.Radiance);

        return new TraceResult(null, null, SkyRadiance(ray));
    }

    private static Color SkyRadiance(Ray ray)
    {
        const float bias = 0.0001f;
        float topBlend = 1f - MathF.Pow(MathF.Min(1f, 1f + bias - ray.Dir.Y), 4f);
        float bottomBlend = 1f - MathF.Pow(MathF.Min(1f, 1f + bias + ray.Dir.Y), 40f);
        float middleBlend = 1f - topBlend - bottomBlend;
        Color sky = _skyRadianceTop * topBlend + _skyRadianceMiddle * middleBlend + _skyRadianceBottom * bottomBlend;

        float sunDot = Vec3.Dot(ray.Dir, _sunDir);
        float sunBlend = MathF.Max(0f, (sunDot - _sunCosAngle) / (1f - _sunCosAngle));

        return sky + _sunRadiance * sunBlend;
    }
}
