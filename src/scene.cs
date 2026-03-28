
using System;
using System.Collections.Generic;

struct TraceResult
{
    public RayHit? Hit;
    public Color Radiance;

    public TraceResult(RayHit? hit, Color radiance)
    {
        Hit = hit;
        Radiance = radiance;
    }
}

struct Material
{
    public Color Color;

    public Material(Color color)
    {
        Color = color;
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

    public void AddObject(Object obj) => _objects.Add(obj);

    public TraceResult Trace(Ray ray, ref Rng rng)
    {
        float closestDist = float.MaxValue;
        Material? closestMaterial = null;
        foreach (Object obj in _objects)
        {
            if (obj.Intersect(ray) is RayHit hit && hit.Dist < closestDist)
            {
                closestDist = hit.Dist;
                closestMaterial = obj.Material;
            }
        }
        if (closestMaterial is Material m)
        {
            return new TraceResult(null, m.Color);
        }
        return new TraceResult(null, SkyRadiance(ray));
    }

    private static Color SkyRadiance(Ray ray)
    {
        const float bias = 0.0001f;
        float topBlend = 1f - MathF.Pow(MathF.Min(1f, 1f + bias - ray.Dir.Y), 4f);
        float bottomBlend = 1f - MathF.Pow(MathF.Min(1f, 1f + bias + ray.Dir.Y), 40f);
        float middleBlend = 1f - topBlend - bottomBlend;
        return _skyRadianceTop * topBlend + _skyRadianceMiddle * middleBlend + _skyRadianceBottom * bottomBlend;
    }
}
