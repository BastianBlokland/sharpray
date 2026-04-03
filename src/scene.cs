
using System;
using System.Collections.Generic;

record struct Surface(Color Radiance, ShapeHit? Hit = null, Material? Material = null);

record struct Fragment(Color Radiance, Vec3? Normal, Vec2? Surface, float? Depth);

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
    public ShapeHit? Intersect(Ray ray) => Shape.Intersect(ray, Trans);
    public bool IntersectAny(Ray ray) => Shape.IntersectAny(ray, Trans);
    public void Intersect(ReadOnlySpan<Ray> rays, Span<ShapeHit?> hits) => Shape.Intersect(rays, hits, Trans);
    public void IntersectAny(ReadOnlySpan<Ray> rays, Span<bool> hits) => Shape.IntersectAny(rays, hits, Trans);

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
    public Sky Sky => _sky;
    private Sky _sky;
    private bool _built;
    private Bvh<Object, ShapeHit>? _bvh;
    private Counters _counters = null!;

    public Scene(Sky sky)
    {
        _sky = sky;
    }

    public void Build(Counters counters)
    {
        lock (_lock)
        {
            if (_built)
                return;
            _built = true;
            _counters = counters;
        }
        using (counters.TimeScope(Counters.Type.TimeSceneBvhBuild))
        {
            const float sahCostIntersect = 10f; // High as object tests are expensive.
            const int splitBinCount = 32; // Evaluate many splits for the Scene bvh.
            _bvh = new Bvh<Object, ShapeHit>(_objects, sahCostIntersect: sahCostIntersect, splitBinCount: splitBinCount);
        }
        BvhStats stats = _bvh.GetStats();
        _counters.Bump(Counters.Type.SceneObject, _objects.Count);
        _counters.Bump(Counters.Type.SceneBvhNodes, stats.NodeCount);
        _counters.Bump(Counters.Type.SceneBvhDepth, stats.DepthMax);
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

    public bool Occluded(Ray ray)
    {
        if (!_built)
            throw new InvalidOperationException("Scene not built");
        _counters.Bump(Counters.Type.SceneOcclude);
        return _bvh!.IntersectAny(ray, _counters);
    }

    public void Occluded(ReadOnlySpan<Ray> rays, Span<bool> results)
    {
        if (!_built)
            throw new InvalidOperationException("Scene not built");
        _counters.Bump(Counters.Type.SceneOcclude, rays.Length);
        _bvh!.IntersectAny(rays, results, _counters);
    }

    public Surface Trace(Ray ray)
    {
        if (!_built)
            throw new InvalidOperationException("Scene not built");
        _counters.Bump(Counters.Type.SceneTrace);

        if (_bvh!.Intersect(ray, _counters) is (ShapeHit hit, int idx))
        {
            Material mat = _objects[idx].Material;
            return new Surface(mat.Radiance, hit, mat);
        }

        return new Surface(_sky.AmbientRadianceRay(ray));
    }

    public void Trace(ReadOnlySpan<Ray> rays, Span<Surface> results)
    {
        if (!_built)
            throw new InvalidOperationException("Scene not built");
        _counters.Bump(Counters.Type.SceneTrace, rays.Length);

        Span<(ShapeHit Hit, int Index)?> hits = stackalloc (ShapeHit, int)?[rays.Length];
        _bvh!.Intersect(rays, hits, _counters);

        for (int i = 0; i < rays.Length; ++i)
        {
            if (hits[i] is (ShapeHit hit, int idx))
            {
                results[i] = new Surface(_objects[idx].Material.Radiance, hit, _objects[idx].Material);
            }
            else
            {
                results[i] = new Surface(_sky.AmbientRadianceRay(rays[i]));
            }
        }
    }

    public Fragment Sample(Ray ray, ref Rng rng, uint bounces)
    {
        if (!_built)
            throw new InvalidOperationException("Scene not built");

        _counters.Bump(Counters.Type.Sample);

        Color radiance = Color.Black, energy = Color.White;
        Vec3? normal = null;
        Vec2? surface = null;
        float? depth = null;

        for (uint i = 0; i != (bounces + 1); ++i)
        {
            _counters.Bump(Counters.Type.SampleBounce);

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

            if (surf.Hit is ShapeHit hit)
            {
                _counters.Bump(Counters.Type.SampleHit);

                if (isPrimary)
                {
                    normal = hit.Norm;
                    surface = hit.Surface;
                    depth = hit.Dist;
                }

                // Invert the normal for backface hits.
                Vec3 shadingNorm = Vec3.Dot(hit.Norm, ray.Dir) > 0f ? -hit.Norm : hit.Norm;
                Vec3 hitPos = ray[hit.Dist] + shadingNorm * 1e-4f;

                // Direct sun contribution.
                Vec3 sunDir = _sky.SunSampleDir(ref rng);
                float sunCosTheta = Vec3.Dot(shadingNorm, sunDir);
                if (sunCosTheta > 0f && roughness > 0.05f)
                {
                    Ray shadowRay = new Ray(hitPos, sunDir);
                    if (Occluded(shadowRay))
                        _counters.Bump(Counters.Type.ShadowRayOccluded);
                    else
                        radiance += _sky.SunRadiance * energy * sunCosTheta;
                }
                else
                    _counters.Bump(Counters.Type.ShadowRaySkipped);

                // Russian roulette: terminate low-energy paths, compensate survivors.
                if (i >= 3)
                {
                    float survive = MathF.Max(energy.R, MathF.Max(energy.G, energy.B));
                    if (rng.NextFloat() >= survive)
                    {
                        _counters.Bump(Counters.Type.SampleTerminate);
                        break;
                    }
                    energy /= survive;
                }

                // Compute scatter ray.
                Vec3 scatterDirDiffuse = (shadingNorm + Vec3.RandOnSphere(ref rng)).NormalizeOr(shadingNorm); // Cosine-weighted distribution.
                Vec3 scatterDirSpecular = Vec3.Reflect(ray.Dir, shadingNorm);
                Vec3 scatterDir = Vec3.Lerp(scatterDirSpecular, scatterDirDiffuse, roughness).NormalizeOr(shadingNorm);

                ray = new Ray(hitPos, scatterDir);
            }
            else
            {
                _counters.Bump(Counters.Type.SampleMiss);

                // Add sun contibution for primary rays.
                if (isPrimary)
                {
                    radiance += _sky.SunRadianceRay(ray) * energy;
                }
                break;
            }
        }
        return new Fragment(radiance, normal, surface, depth);
    }

    public void Describe(ref FormatWriter fmt)
    {
        if (!_built)
            throw new InvalidOperationException("Scene not built");

        BvhStats bvhStats = _bvh!.GetStats();
        fmt.WriteLine($"objects={new FormatNum(_objects.Count)}");
        fmt.WriteLine($"nodes={new FormatNum(bvhStats.NodeCount)}");
        fmt.WriteLine($"depth={bvhStats.DepthMax}");
        fmt.WriteLine($"leafCount={new FormatNum(bvhStats.LeafCount)}");
        fmt.WriteLine($"leafSize=({bvhStats.LeafSizeMin}/{bvhStats.LeafSizeAvg:F1}/{bvhStats.LeafSizeMax})");
        fmt.WriteLine($"leafDepth={bvhStats.LeafDepthAvg:F1}");
        fmt.WriteLine($"sah={bvhStats.SahCost:F1}");

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
