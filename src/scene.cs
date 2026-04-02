
using System;
using System.Collections.Generic;

record struct Surface(Color Radiance, RayHit? Hit = null, Material? Material = null);

record struct Fragment(Color Radiance, Vec3? Normal, float? Depth);

record struct Material(Color Color, float Roughness, float Metallic = 0f, Color Radiance = default)
{
    public void Describe(ref FormatWriter fmt)
    {
        fmt.WriteLine($"color={Color}");
        fmt.WriteLine($"roughness={Roughness:G3}");
        fmt.WriteLine($"metallic={Metallic:G3}");
    }
}

struct Object : IShape
{
    public string Name;
    public Transform Trans;
    public Material Material;
    public IShape Shape;

    private Box _boundsRotated;
    private AABox _bounds;

    public Object(String name, Transform trans, Material material, IShape shape)
    {
        Name = name;
        Trans = trans;
        Material = material;
        Shape = shape;

        _boundsRotated = shape.Bounds().Transform(trans);
        _bounds = _boundsRotated.Bounds();
    }

    public AABox Bounds() => _bounds;
    public bool Overlaps(AABox box) => _boundsRotated.Overlaps(box);
    public RayHit? Intersect(Ray ray) => Shape.Intersect(ray, Trans);

    public void Describe(ref FormatWriter fmt)
    {
        fmt.WriteLine(Name);
        fmt.IndentPush();
        {
            fmt.WriteLine("transform");
            fmt.IndentPush();
            {
                fmt.WriteLine($"pos={Trans.Pos}");
                fmt.WriteLine($"rot={Trans.Rot}");
                fmt.WriteLine($"scale={Trans.Scale}");
            }
            fmt.IndentPop();

            fmt.WriteLine("material");
            fmt.IndentPush();
            Material.Describe(ref fmt);
            fmt.IndentPop();

            fmt.WriteLine("bounds");
            fmt.IndentPush();
            fmt.WriteLine($"min={_bounds.Min}");
            fmt.WriteLine($"max={_bounds.Max}");
            fmt.IndentPop();

            if (Shape is Mesh mesh)
            {
                fmt.WriteLine("mesh");
                fmt.IndentPush();
                mesh.Describe(ref fmt);
                fmt.IndentPop();
            }
        }
        fmt.IndentPop();
    }

    public void OverlayBounds(Overlay overlay, Color color, int maxDepth = int.MaxValue)
    {
        if (Shape is Mesh mesh)
        {
            mesh.OverlayBounds(overlay, Trans, maxDepth);
        }
        overlay.AddLineBox(_boundsRotated, color);
    }
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
    private readonly object _lock = new object();
    private Sky _sky;
    private bool _built;
    private Bvh<Object>? _bvh;

    public Scene(Sky sky)
    {
        _sky = sky;
    }

    public void Build(Counters? counters = null)
    {
        lock (_lock)
        {
            if (_built)
                return;
            _built = true;
        }
        using (counters?.TimeScope(Counters.Type.TimeSceneBvhBuild))
        {
            _bvh = new Bvh<Object>(_objects);
        }
        if (counters != null)
        {
            BvhStats stats = _bvh.GetStats();
            counters.Bump(Counters.Type.SceneObject, _objects.Count);
            counters.Bump(Counters.Type.SceneBvhNodes, stats.NodeCount);
            counters.Bump(Counters.Type.SceneBvhDepth, stats.DepthMax);
        }
    }

    public void AddObject(Object obj)
    {
        lock (_lock)
        {
            if (_built)
                throw new InvalidOperationException("Scene already built");
            _objects.Add(obj);
        }
    }

    public AABox Bounds() => _bvh?.Bounds ?? AABox.Inverted();

    public bool Occluded(Ray ray, Counters counters)
    {
        if (!_built)
            throw new InvalidOperationException("Scene not built");
        counters.Bump(Counters.Type.SceneOcclude);
        return _bvh!.IntersectAny(ray);
    }

    public Surface Trace(Ray ray, Counters counters)
    {
        if (!_built)
            throw new InvalidOperationException("Scene not built");
        counters.Bump(Counters.Type.SceneTrace);

        if (_bvh!.Intersect(ray) is (RayHit hit, int idx))
        {
            Material mat = _objects[idx].Material;
            return new Surface(mat.Radiance, hit, mat);
        }

        return new Surface(_sky.AmbientRadianceRay(ray));
    }

    public Fragment Sample(Ray ray, ref Rng rng, uint bounces, Counters counters)
    {
        if (!_built)
            throw new InvalidOperationException("Scene not built");

        counters.Bump(Counters.Type.Sample);

        Color radiance = Color.Black, energy = Color.White;
        Vec3? normal = null;
        float? depth = null;

        for (uint i = 0; i != (bounces + 1); ++i)
        {
            counters.Bump(Counters.Type.SampleBounce);

            bool isPrimary = i == 0;
            Surface surf = Trace(ray, counters);

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
                counters.Bump(Counters.Type.SampleHit);

                if (isPrimary)
                {
                    normal = hit.Norm;
                    depth = hit.Dist;
                }

                Vec3 hitPos = ray[hit.Dist] + hit.Norm * 1e-4f;

                // Direct sun contribution.
                Vec3 sunDir = _sky.SunSampleDir(ref rng);
                float sunCosTheta = Vec3.Dot(hit.Norm, sunDir);
                if (sunCosTheta > 0f && roughness > 0.05f)
                {
                    Ray shadowRay = new Ray(hitPos, sunDir);
                    if (Occluded(shadowRay, counters))
                        counters.Bump(Counters.Type.ShadowRayOccluded);
                    else
                        radiance += _sky.SunRadiance * energy * sunCosTheta;
                }
                else
                    counters.Bump(Counters.Type.ShadowRaySkipped);

                // Russian roulette: terminate low-energy paths, compensate survivors.
                if (i >= 3)
                {
                    float survive = MathF.Max(energy.R, MathF.Max(energy.G, energy.B));
                    if (rng.NextFloat() >= survive)
                    {
                        counters.Bump(Counters.Type.SampleTerminate);
                        break;
                    }
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
                counters.Bump(Counters.Type.SampleMiss);

                // Add sun contibution for primary rays.
                if (isPrimary)
                {
                    radiance += _sky.SunRadianceRay(ray) * energy;
                }
                break;
            }
        }
        return new Fragment(radiance, normal, depth);
    }

    public void Describe(ref FormatWriter fmt)
    {
        if (!_built)
            throw new InvalidOperationException("Scene not built");

        foreach (Object obj in _objects)
        {
            fmt.Separate();
            obj.Describe(ref fmt);
        }
    }

    public void OverlayInfo(Overlay overlay)
    {
        if (!_built)
            throw new InvalidOperationException("Scene not built");

        FormatWriter fmt = new FormatWriter();
        for (int i = 0; i != _objects.Count; ++i)
        {
            Object obj = _objects[i];
            fmt.Clear();
            obj.Describe(ref fmt);
            overlay.AddText(fmt.ToString(), obj.Bounds().Center, Color.ForIndex(i));
        }
    }

    public void OverlayBounds(Overlay overlay, int maxDepth = int.MaxValue)
    {
        if (!_built)
            throw new InvalidOperationException("Scene not built");

        _bvh?.OverlayBounds(overlay, Transform.Identity(), maxDepth);

        for (int i = 0; i != _objects.Count; ++i)
        {
            _objects[i].OverlayBounds(overlay, Color.ForIndex(i), maxDepth);
        }
    }
}
