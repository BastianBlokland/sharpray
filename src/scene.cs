
using System;

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
    public Material Mat;
    public IShape Shape;

    public Object(Transform trans, Material mat, IShape shape)
    {
        Trans = trans;
        Mat = mat;
        Shape = shape;
    }

    public RayHit? Intersect(Ray ray)
    {
        var (localRay, localRayScale) = Trans.TransformRayInv(ray);
        RayHit? hit = Shape.Intersect(localRay);
        if (hit is RayHit h)
        {
            Vec3 worldNorm = (Trans.Rot * (h.Norm / Trans.Scale)).Normalize();
            return new RayHit(h.Dist / localRayScale, worldNorm);
        }
        return null;
    }
}

class Scene
{
    public Scene()
    {
    }

    static readonly Color _skyRadianceTop = new Color(0.4f, 0.5f, 0.8f);
    static readonly Color _skyRadianceMiddle = new Color(1.0f, 0.9f, 0.9f);
    static readonly Color _skyRadianceBottom = new Color(0.5f, 0.425f, 0.275f);

    public TraceResult Trace(Ray ray, ref Rng rng)
    {
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
