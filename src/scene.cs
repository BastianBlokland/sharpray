
using System;
using System.Diagnostics;
using System.Collections.Generic;

readonly record struct Surface(
    Color Radiance,
    Color Color,
    float Roughness,
    float Metallic,
    Vec3 Normal,
    Vec3 NormalRaw, // Uneffect by normal-mapping.
    Vec4 Tangent,
    Vec2 Uv)
{
    public Color BaseReflectivity => Color.Lerp(new Color(0.04f), Color, Metallic);

    public Vec3 TransformDir(Vec3 d)
    {
        Debug.Assert(d.IsUnit, "Dir must be normalized");
        Vec3 tan = Tangent.Xyz;
        Vec3 bitan = Tangent.W * Vec3.Cross(Normal, tan);
        return d.X * tan + d.Y * bitan + d.Z * Normal;
    }

    public float SpecularProbability(Vec3 viewDir)
    {
        Debug.Assert(viewDir.IsUnit, "Dir must be normalized");
        float nDotV = MathF.Max(1e-4f, Vec3.Dot(Normal, viewDir));
        float fresnel = Brdf.Fresnel(nDotV, BaseReflectivity).Luminance;
        return float.Clamp(MathF.Max(fresnel, Metallic), 0.001f, 0.999f);
    }

    public Vec3 SpecularDir(Vec3 incomingDir, ref Rng rng)
    {
        Debug.Assert(incomingDir.IsUnit, "Dir must be normalized");
        float roughnessSqr = MathF.Max(Roughness * Roughness, 1e-4f);
        Vec3 halfVecLocal = Brdf.GgxSampleLocal(roughnessSqr, Vec2.Rand(ref rng));
        Vec3 halfVecWorld = TransformDir(halfVecLocal);
        return Vec3.Reflect(incomingDir, halfVecWorld);
    }

    // Fraction of light energy that gets reflected from lightDir toward viewDir.
    public Color Eval(Vec3 viewDir, Vec3 lightDir)
    {
        Debug.Assert(viewDir.IsUnit && lightDir.IsUnit, "Directions must be normalized");

        float nDotL = Vec3.Dot(Normal, lightDir);
        if (nDotL <= 0f)
            return Color.Black; // Behind the surface.

        float nDotV = MathF.Max(1e-4f, Vec3.Dot(Normal, viewDir));
        float roughnessSqr = MathF.Max(Roughness * Roughness, 1e-4f);

        Vec3 halfVec = (viewDir + lightDir).NormalizeOr(Normal);
        float nDotH = Vec3.Dot(Normal, halfVec);
        float hDotV = Vec3.Dot(halfVec, viewDir);

        // Specular: D*F*G / (4*nDotV*nDotL) * nDotL = D*F*G / (4*nDotV).
        Color f = Brdf.Fresnel(hDotV, BaseReflectivity);
        float g = Brdf.SmithG1(nDotV, roughnessSqr) * Brdf.SmithG1(nDotL, roughnessSqr);
        Color specular = Brdf.GgxD(nDotH, roughnessSqr) * f * g / (4f * nDotV);

        // Diffuse: Lambert weighted by (1 - F) to avoid double-counting specular energy.
        // Metals have no diffuse, so scaled by (1 - Metallic).
        Color diffuse = (Color.White - Brdf.Fresnel(nDotV, BaseReflectivity)) * Color * (1f - Metallic) * (nDotL / MathF.PI);

        return specular + diffuse;
    }

    public SampleDir Scatter(Vec3 incomingDir, ref Rng rng)
    {
        Vec3 viewDir = -incomingDir;

        Vec3 scatterDir;
        if (rng.NextFloat() < SpecularProbability(viewDir))
        {
            // Specular scatter.
            scatterDir = SpecularDir(incomingDir, ref rng);
        }
        else
        {
            // Diffuse scatter.
            scatterDir = (Normal + Vec3.RandOnSphere(ref rng)).NormalizeOr(Normal);
        }

        // Clamp scatter ray to stay above the geometric surface.
        if (Vec3.Dot(scatterDir, NormalRaw) <= 0f)
        {
            scatterDir = (scatterDir - 2f * Vec3.Dot(scatterDir, NormalRaw) * NormalRaw).NormalizeOr(NormalRaw);
        }

        return new SampleDir(scatterDir, Pdf(viewDir, scatterDir));
    }

    // Probability Density Function, likelihood of the light direction being chosen.
    public float Pdf(Vec3 viewDir, Vec3 lightDir)
    {
        float nDotL = Vec3.Dot(Normal, lightDir);
        if (nDotL <= 0f)
            return 0f; // Behind the surface.

        float specProbability = SpecularProbability(viewDir);

        Vec3 halfVec = (viewDir + lightDir).NormalizeOr(Normal);
        float nDotH = MathF.Max(0f, Vec3.Dot(Normal, halfVec));
        float hDotV = MathF.Max(1e-6f, Vec3.Dot(halfVec, viewDir));

        float roughnessSqr = MathF.Max(Roughness * Roughness, 1e-4f);
        float pdfSpec = Brdf.GgxD(nDotH, roughnessSqr) * nDotH / (4f * hDotV);
        float pdfDiff = nDotL / MathF.PI;

        return specProbability * pdfSpec + (1f - specProbability) * pdfDiff;
    }
}

readonly record struct Fragment(
    Color Radiance,
    Vec3? Normal,
    Vec2? Uv,
    float? Depth);

readonly record struct SampleDir(
    Vec3 Dir,
    float Pdf // Probability Density Function, likelihood of the direction being chosen.
);

readonly record struct Material(
    Color Color,
    float Roughness,
    float Metallic = 0f,
    Color Radiance = default,
    Texture? ColorTexture = null,
    Texture? RoughnessTexture = null,
    Texture? MetallicTexture = null,
    Texture? NormalTexture = null) : IDescribable
{
    public Color SampleColor(Vec2 uv) =>
        (ColorTexture?.Sample(uv) ?? Color.White) * Color;

    public float SampleRoughness(Vec2 uv) =>
        (RoughnessTexture?.Sample(uv).R ?? 1.0f) * Roughness;

    public float SampleMetallic(Vec2 uv) =>
        (MetallicTexture?.Sample(uv).R ?? 1.0f) * Metallic;

    public Vec3 SampleNormal(Vec2 uv, Vec3 geoNorm, Vec4 geoTan)
    {
        if (NormalTexture == null)
            return geoNorm;
        Vec3 tan = geoTan.Xyz;
        Vec3 bitan = geoTan.W * Vec3.Cross(geoNorm, tan);
        Vec3 norm = NormalTexture.SampleNormal(uv);
        return (norm.X * tan + norm.Y * bitan + norm.Z * geoNorm).NormalizeOr(geoNorm);
    }

    public void Describe(FormatWriter fmt)
    {
        fmt.WriteLine($"color={Color}");
        ColorTexture?.DescribeIndented(fmt);
        fmt.WriteLine($"roughness={Roughness:G3}");
        RoughnessTexture?.DescribeIndented(fmt);
        fmt.WriteLine($"metallic={Metallic:G3}");
        MetallicTexture?.DescribeIndented(fmt);
        fmt.WriteLine($"radiance={Radiance}");
        NormalTexture?.DescribeIndented("normal", fmt);
    }
}

readonly struct Object : IShape, IDescribable
{
    public readonly string Name;
    public readonly Transform Trans;
    public readonly Material Material;
    public readonly IShape Shape;

    private readonly Box _boundsRotated;
    private readonly AABox _bounds;

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

    public void Describe(FormatWriter fmt)
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

            Material.DescribeIndented("material", fmt);

            fmt.WriteLine("bounds");
            fmt.IndentPush();
            fmt.WriteLine($"min={_bounds.Min}");
            fmt.WriteLine($"max={_bounds.Max}");
            fmt.IndentPop();

            if (Shape is Mesh mesh)
                mesh.DescribeIndented("mesh", fmt);
        }
        fmt.IndentPop();
    }

    public void OverlayWireframe(Overlay overlay, Color color)
    {
        if (Shape is Mesh mesh)
            mesh.OverlayWireframe(overlay, Trans, color);
    }

    public void OverlayBounds(Overlay overlay, Color color, int maxDepth = int.MaxValue)
    {
        if (Shape is Mesh mesh)
            mesh.OverlayBounds(overlay, Trans, maxDepth);
        overlay.AddLineBox(_boundsRotated, color);
    }
}

interface ISky : IDescribable
{
    Color Radiance(Vec3 dir);

    // Compute a sample-direction toward the light.
    SampleDir? LightDir(Vec3 dir);
    SampleDir? LightDirRand(ref Rng rng);
}

readonly struct SunProcedural : IDescribable
{
    private readonly Vec3 _dir;
    private readonly float _angle;
    private readonly Color _radiance;
    private readonly float _angleCos;
    private readonly Quat _rot;
    private readonly float _pdfDenom; // π * (1 - cosθ_max), normalization for importance-sampled PDF.

    public SunProcedural(Vec3 dir, float angle, Color radiance)
    {
        _dir = dir;
        _angle = angle;
        _radiance = radiance;
        _angleCos = MathF.Cos(angle);
        _rot = Quat.Look(dir, Vec3.Up);
        _pdfDenom = MathF.PI * (1f - _angleCos);
    }

    public Color Radiance(Vec3 dir)
    {
        float blend = MathF.Max(0f, (Vec3.Dot(dir, _dir) - _angleCos) / (1f - _angleCos));
        return _radiance * blend;
    }

    public SampleDir? LightDir(Vec3 dir)
    {
        float cosTheta = Vec3.Dot(dir, _dir);
        if (cosTheta < _angleCos)
            return null;
        float blend = (cosTheta - _angleCos) / (1f - _angleCos);
        return new SampleDir(dir, blend / _pdfDenom);
    }

    public SampleDir LightDirRand(ref Rng rng)
    {
        // Importance sample proportional to blend.
        float phi = MathF.PI * 2f * rng.NextFloat();
        float blend = MathF.Sqrt(rng.NextFloat());
        float z = _angleCos + (1f - _angleCos) * blend;
        float sinT = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
        Vec3 dir = _rot * new Vec3(MathF.Cos(phi) * sinT, MathF.Sin(phi) * sinT, z);
        return new SampleDir(dir, blend / _pdfDenom);
    }

    public void Describe(FormatWriter fmt)
    {
        fmt.WriteLine($"dir={_dir}");
        fmt.WriteLine($"angle={float.RadiansToDegrees(_angle):G3}deg");
        fmt.WriteLine($"radiance={_radiance} lum={_radiance.Luminance:G4}");
    }
}

class SkyProcedural : ISky
{
    public Color RadianceTop;
    public Color RadianceMiddle;
    public Color RadianceBottom;
    public SunProcedural Sun;

    public SkyProcedural(
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
        Sun = new SunProcedural(sunDir, sunAngle, sunRadiance);
    }

    public Color Radiance(Vec3 dir)
    {
        const float bias = 0.0001f;
        float topBlend = 1f - MathF.Pow(MathF.Min(1f, 1f + bias - dir.Y), 4f);
        float bottomBlend = 1f - MathF.Pow(MathF.Min(1f, 1f + bias + dir.Y), 40f);
        float middleBlend = 1f - topBlend - bottomBlend;
        Color ambient = RadianceTop * topBlend + RadianceMiddle * middleBlend + RadianceBottom * bottomBlend;

        return ambient + Sun.Radiance(dir);
    }

    public SampleDir? LightDirRand(ref Rng rng) => Sun.LightDirRand(ref rng);
    public SampleDir? LightDir(Vec3 dir) => Sun.LightDir(dir);

    public void Describe(FormatWriter fmt)
    {
        fmt.WriteLine($"type=SkyProcedural");
        fmt.WriteLine($"top={RadianceTop} lum={RadianceTop.Luminance:G3}");
        fmt.WriteLine($"middle={RadianceMiddle} lum={RadianceMiddle.Luminance:G3}");
        fmt.WriteLine($"bottom={RadianceBottom} lum={RadianceBottom.Luminance:G3}");
        Sun.DescribeIndented("sun", fmt);
    }
}

class SkyTexture : ISky
{
    private readonly Texture _texture;
    private readonly Cdf2 _cdf;
    private readonly float _pdfScale;

    public SkyTexture(Texture texture)
    {
        float Weight(Vec2i pos)
        {
            // sin(theta) for equirectangular area distortion: texels near poles cover less solid angle.
            return texture.Get(pos).Luminance * MathF.Sin(MathF.PI * (pos.Y + 0.5f) / texture.Size.Y);
        }

        _texture = texture;
        _cdf = new Cdf2(texture.Size, Weight);
        _pdfScale = texture.Size.X * texture.Size.Y / (_cdf.TotalWeight * 2f * MathF.PI * MathF.PI);
    }

    public Color Radiance(Vec3 dir) => _texture.Sample(dir.EquirectUv());

    public SampleDir? LightDirRand(ref Rng rng)
    {
        if (_cdf.TotalWeight <= 0f)
            return null;
        Vec2i texel = _cdf.SampleRand(ref rng);
        Vec3 dir = Vec3.FromEquirectUv((texel + 0.5f) / _texture.Size.ToFloat());
        float pdf = _texture.Get(texel).Luminance * _pdfScale;
        return pdf > 0f ? new SampleDir(dir, pdf) : null;
    }

    public SampleDir? LightDir(Vec3 dir)
    {
        if (_cdf.TotalWeight <= 0f)
            return null;
        Vec2 uv = dir.EquirectUv();
        Vec2i coord = (uv * _texture.Size.ToFloat()).ToInt() % _texture.Size;
        float pdf = _texture.Get(coord).Luminance * _pdfScale;
        return pdf > 0f ? new SampleDir(dir, pdf) : null;
    }

    public void Describe(FormatWriter fmt)
    {
        fmt.WriteLine("type=SkyTexture");
        _texture.Describe(fmt);
    }
}

class Scene : IDescribable
{
    public ISky? Sky { get; set; }

    private List<Object> _objects = new List<Object>();
    private readonly object _lock = new object();
    private bool _built;
    private Bvh<Object, ShapeHit>? _bvh;

    public void Build(Counters? counters = null)
    {
        lock (_lock)
        {
            if (_built)
                return;
            _built = true;
        }
        Debug.Assert(Sky != null, "Sky missing");

        using (counters?.TimeScope(Counters.Type.TimeSceneBvhBuild))
        {
            const float sahCostIntersect = 10f; // High as object tests are expensive.
            const int splitBinCount = 32; // Evaluate many splits for the Scene bvh.
            _bvh = new Bvh<Object, ShapeHit>(_objects, sahCostIntersect: sahCostIntersect, splitBinCount: splitBinCount);
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
        return _bvh!.IntersectAny(ray, counters);
    }

    public (Surface, float Dist)? Trace(Ray ray, Counters counters)
    {
        if (!_built)
            throw new InvalidOperationException("Scene not built");
        counters.Bump(Counters.Type.SceneTrace);

        if (_bvh!.Intersect(ray, counters) is (ShapeHit hit, int idx))
        {
            Material mat = _objects[idx].Material;
            Color color = mat.SampleColor(hit.Uv);
            float roughness = mat.SampleRoughness(hit.Uv);
            float metallic = mat.SampleMetallic(hit.Uv);
            Vec3 normal = mat.SampleNormal(hit.Uv, hit.Norm, hit.Tan);

            // Re-orthogonalize tangent.
            Vec3 tangentDir = (hit.Tan.Xyz - Vec3.Dot(hit.Tan.Xyz, normal) * normal).NormalizeOr(hit.Tan.Xyz);
            Vec4 tangent = new Vec4(tangentDir, hit.Tan.W);

            Surface surf = new Surface(mat.Radiance, color, roughness, metallic, normal, hit.Norm, tangent, hit.Uv);
            return (surf, hit.Dist);
        }

        return null;
    }

    public Fragment Sample(Ray ray, ref Rng rng, uint bounces, float indirectClamp, Counters counters)
    {
        if (!_built)
            throw new InvalidOperationException("Scene not built");

        counters.Bump(Counters.Type.Sample);

        Color radiance = Color.Black, energy = Color.White;
        Vec3? normal = null;
        Vec2? uv = null;
        float? depth = null;
        SampleDir? lastScatter = null;

        for (uint i = 0; i != (bounces + 1); ++i)
        {
            counters.Bump(Counters.Type.SampleBounce);

            if (Trace(ray, counters) is (Surface surf, float dist))
            {
                counters.Bump(Counters.Type.SampleHit);

                Vec3 viewDir = -ray.Dir;
                Vec3 hitPos = ray[dist] + surf.NormalRaw * MathF.Max(1e-3f, dist * 1e-3f);

                // Accumulate radiance.
                radiance += surf.Radiance * energy;

                if (i == 0)
                {
                    // Save surface definition for the primary ray.
                    normal = surf.Normal;
                    uv = surf.Uv;
                    depth = dist;
                }

                // Direct sky illumination.
                radiance += SampleSkyDirect(surf, hitPos, viewDir, ref rng, counters) * energy;

                // Russian roulette: terminate low-energy paths, compensate survivors.
                if (i >= 3)
                {
                    float survive = MathF.Min(1f, MathF.Max(energy.R, MathF.Max(energy.G, energy.B)));
                    if (rng.NextFloat() >= survive)
                    {
                        counters.Bump(Counters.Type.SampleTerminate);
                        break;
                    }
                    energy /= survive;
                }

                // Compute a scatter ray and accumulate its weight.
                SampleDir scatter = surf.Scatter(ray.Dir, ref rng);
                energy *= surf.Eval(viewDir, scatter.Dir) / MathF.Max(scatter.Pdf, 1e-6f);
                energy = energy.ClampLuminance(indirectClamp); // Combat fireflies.
                Debug.Assert(energy.IsFinite);

                lastScatter = scatter;
                ray = new Ray(hitPos, scatter.Dir);
            }
            else
            {
                counters.Bump(Counters.Type.SampleMiss);

                // Sample the sky radiance, use MIS (Multiple Importance Sampling) avoid double counting
                // the sky radiance we already added during the scatter.
                float misWeight;
                if (lastScatter is SampleDir scatter)
                    misWeight = Brdf.PowerHeuristic(scatter.Pdf, Sky?.LightDir(ray.Dir)?.Pdf ?? 0f);
                else
                    misWeight = 1f;

                radiance += (Sky?.Radiance(ray.Dir) ?? Color.Black) * misWeight * energy;
                break;
            }
        }
        Debug.Assert(radiance.IsFinite);
        return new Fragment(radiance, normal, uv, depth);
    }

    // Sample the sky radiance contribution at the surface.
    private Color SampleSkyDirect(Surface surf, Vec3 hitPos, Vec3 viewDir, ref Rng rng, Counters counters)
    {
        if (Sky?.LightDirRand(ref rng) is not SampleDir light)
            return Color.Black; // Sky has no light.
        if (light.Pdf <= 0f)
            return Color.Black; // Zero probability direction (rare due to rng).
        if (Vec3.Dot(surf.Normal, light.Dir) <= 0f)
            return Color.Black; // Light is behind the surface.
        if (Occluded(new Ray(hitPos, light.Dir), counters))
            return Color.Black; // Shadowed.

        Color surfReflectance = surf.Eval(viewDir, light.Dir);
        float surfPdf = surf.Pdf(viewDir, light.Dir);

        // Compute the weight by combining the light dir probability and the surface probability
        // using MIS (Multiple Importance Sampling).
        float misWeight = Brdf.PowerHeuristic(light.Pdf, surfPdf);
        return Sky.Radiance(light.Dir) * surfReflectance * misWeight / light.Pdf;
    }

    public void Describe(FormatWriter fmt)
    {
        if (!_built)
            throw new InvalidOperationException("Scene not built");

        Sky?.DescribeIndented("sky", fmt);
        fmt.Separate();

        fmt.WriteLine("bvh");
        fmt.IndentPush();
        {
            BvhStats bvhStats = _bvh!.GetStats();
            fmt.WriteLine($"objects={new FormatNum(_objects.Count)}");
            fmt.WriteLine($"nodes={new FormatNum(bvhStats.NodeCount)}");
            fmt.WriteLine($"depth={bvhStats.DepthMax}");
            fmt.WriteLine($"leafCount={new FormatNum(bvhStats.LeafCount)}");
            fmt.WriteLine($"leafSize=({bvhStats.LeafSizeMin}/{bvhStats.LeafSizeAvg:F1}/{bvhStats.LeafSizeMax})");
            fmt.WriteLine($"leafDepth={bvhStats.LeafDepthAvg:F1}");
            fmt.WriteLine($"sah={bvhStats.SahCost:F1}");
        }
        fmt.IndentPop();

        foreach (Object obj in _objects)
        {
            fmt.Separate();
            obj.Describe(fmt);
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
            obj.Describe(fmt);
            overlay.AddText(fmt.ToString(), obj.Bounds().Center, Color.ForIndex(i));
        }
    }

    public void OverlayWireframe(Overlay overlay)
    {
        if (!_built)
            throw new InvalidOperationException("Scene not built");

        for (int i = 0; i != _objects.Count; ++i)
            _objects[i].OverlayWireframe(overlay, Color.ForIndex(i));
    }

    public void OverlayBounds(Overlay overlay, int maxDepth = int.MaxValue)
    {
        if (!_built)
            throw new InvalidOperationException("Scene not built");

        _bvh?.OverlayBounds(overlay, Transform.Identity(), maxDepth);

        for (int i = 0; i != _objects.Count; ++i)
            _objects[i].OverlayBounds(overlay, Color.ForIndex(i), maxDepth);
    }
}
