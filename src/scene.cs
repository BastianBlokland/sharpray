
using System;
using System.Diagnostics;
using System.Collections.Generic;

record struct Surface(
    Color Radiance,
    ShapeHit? Hit = null,
    Color Color = default,
    float Roughness = 1f,
    float Metallic = 0f,
    Vec3 Normal = default,
    Vec4 Tangent = default)
{
    public Vec3 TransformDir(Vec3 d)
    {
        Debug.Assert(d.IsUnit, "Dir must be normalized");
        Vec3 tan = Tangent.Xyz;
        Vec3 bitan = Tangent.W * Vec3.Cross(Normal, tan);
        return d.X * tan + d.Y * bitan + d.Z * Normal;
    }
}

record struct Fragment(
    Color Radiance,
    Vec3? Normal,
    Vec2? Uv,
    float? Depth);

record struct Material(
    Color Color,
    float Roughness,
    float Metallic = 0f,
    Color Radiance = default,
    Texture? ColorTexture = null,
    Texture? RoughnessTexture = null,
    Texture? NormalTexture = null)
{
    public Color SampleColor(Vec2 uv)
    {
        return (ColorTexture?.Sample(uv) ?? Color.White) * Color;
    }

    public float SampleRoughness(Vec2 uv)
    {
        return (RoughnessTexture?.Sample(uv).R ?? 1.0f) * Roughness;
    }

    public Vec3 SampleNormal(Vec2 uv, Vec3 geoNorm, Vec4 geoTan)
    {
        if (NormalTexture == null)
            return geoNorm;
        Vec3 tan = geoTan.Xyz;
        Vec3 bitan = geoTan.W * Vec3.Cross(geoNorm, tan);
        Vec3 norm = NormalTexture.SampleNormal(uv);
        return (norm.X * tan + norm.Y * bitan + norm.Z * geoNorm).NormalizeOr(geoNorm);
    }

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

record struct LightDir(
    Vec3 Dir,
    float Pdf // Probability Density Function, likelihood of the direction being chosen.
);

interface ISky
{
    Color Radiance(Vec3 dir);

    // Compute a LightDir including its pdf (Probability Density Function).
    LightDir? LightDir(Vec3 dir);
    LightDir? LightDirRand(ref Rng rng);
}

readonly struct SunProcedural
{
    private readonly Vec3 _dir;
    private readonly float _angle;
    private readonly Color _radiance;
    private readonly float _angleCos;
    private readonly Quat _rot;

    public SunProcedural(Vec3 dir, float angle, Color radiance)
    {
        _dir = dir;
        _angle = angle;
        _radiance = radiance;
        _angleCos = MathF.Cos(angle);
        _rot = Quat.Look(dir, Vec3.Up);
    }

    public Color Radiance(Vec3 dir)
    {
        float blend = MathF.Max(0f, (Vec3.Dot(dir, _dir) - _angleCos) / (1f - _angleCos));
        return _radiance * blend;
    }

    public LightDir? LightDir(Vec3 dir)
    {
        if (Vec3.Dot(dir, _dir) < _angleCos)
            return null;
        float pdf = 1f / (2f * MathF.PI * (1f - _angleCos));
        return new LightDir(dir, pdf);
    }

    public LightDir LightDirRand(ref Rng rng)
    {
        Vec3 dir = _rot * Vec3.RandInCone(ref rng, _angle);
        float pdf = 1f / (2f * MathF.PI * (1f - _angleCos));
        return new LightDir(dir, pdf);
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

    public LightDir? LightDirRand(ref Rng rng) => Sun.LightDirRand(ref rng);
    public LightDir? LightDir(Vec3 dir) => Sun.LightDir(dir);
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

    public LightDir? LightDirRand(ref Rng rng)
    {
        if (_cdf.TotalWeight <= 0f)
            return null;
        Vec2i texel = _cdf.SampleRand(ref rng);
        Vec3 dir = Vec3.FromEquirectUv((texel + 0.5f) / _texture.Size.ToFloat());
        float pdf = _texture.Get(texel).Luminance * _pdfScale;
        return pdf > 0f ? new LightDir(dir, pdf) : null;
    }

    public LightDir? LightDir(Vec3 dir)
    {
        if (_cdf.TotalWeight <= 0f)
            return null;
        Vec2 uv = dir.EquirectUv();
        Vec2i coord = (uv * _texture.Size.ToFloat()).ToInt() % _texture.Size;
        float pdf = _texture.Get(coord).Luminance * _pdfScale;
        return pdf > 0f ? new LightDir(dir, pdf) : null;
    }
}

class Scene
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

    public Surface Trace(Ray ray, Counters counters)
    {
        if (!_built)
            throw new InvalidOperationException("Scene not built");
        counters.Bump(Counters.Type.SceneTrace);

        if (_bvh!.Intersect(ray, counters) is (ShapeHit hit, int idx))
        {
            Material mat = _objects[idx].Material;
            Color color = mat.SampleColor(hit.Uv);
            float roughness = mat.SampleRoughness(hit.Uv);
            float metallic = mat.Metallic;
            Vec3 normal = mat.SampleNormal(hit.Uv, hit.Norm, hit.Tan);

            // Re-orthogonalize tangent.
            Vec3 tangentDir = (hit.Tan.Xyz - Vec3.Dot(hit.Tan.Xyz, normal) * normal).NormalizeOr(hit.Tan.Xyz);
            Vec4 tangent = new Vec4(tangentDir, hit.Tan.W);

            return new Surface(mat.Radiance, hit, color, roughness, metallic, normal, tangent);
        }

        return new Surface(Sky!.Radiance(ray.Dir));
    }

    public Fragment Sample(Ray ray, ref Rng rng, uint bounces, Counters counters)
    {
        if (!_built)
            throw new InvalidOperationException("Scene not built");

        counters.Bump(Counters.Type.Sample);

        Color radiance = Color.Black, energy = Color.White;
        float scatterPdf = 0f; // PDF of the current ray direction; 0 = primary ray (no BSDF sample).
        Vec3? normal = null;
        Vec2? uv = null;
        float? depth = null;

        for (uint i = 0; i != (bounces + 1); ++i)
        {
            counters.Bump(Counters.Type.SampleBounce);

            bool isPrimary = i == 0;
            Surface surf = Trace(ray, counters);

            if (surf.Hit is ShapeHit hit)
            {
                counters.Bump(Counters.Type.SampleHit);

                // Emissive surface contribution (no MIS for area lights yet).
                radiance += surf.Radiance * energy;

                float rawNDotV = Vec3.Dot(surf.Normal, -ray.Dir);
                float nDotV = MathF.Max(1e-4f, rawNDotV);
                Color baseReflectivity = Color.Lerp(new Color(0.04f), surf.Color, surf.Metallic);
                Color fresnel = FresnelSchlick(nDotV, baseReflectivity);
                Color diffuseColor = (Color.White - fresnel) * surf.Color;
                float roughnessSqr = MathF.Max(surf.Roughness * surf.Roughness, 1e-4f);
                float specProbability = float.Clamp(fresnel.Luminance, 0.001f, 0.999f);

                if (isPrimary)
                {
                    normal = surf.Normal;
                    uv = hit.Uv;
                    depth = hit.Dist;
                }
                Vec3 hitPos = ray[hit.Dist] + hit.Norm * 1e-4f;

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

                // NEE (Next Event Estimation): sample sky light and add MIS-weighted contribution.
                // Skip when shading normal faces away from viewer (e.g. back-facing normal map):
                // clamped nDotV would give non-physical 1/(4*nDotV) amplification in the BRDF.
                if (rawNDotV > 0f && Sky!.LightDirRand(ref rng) is LightDir lightSample)
                {
                    Vec3 l = lightSample.Dir;
                    float nDotL_nee = Vec3.Dot(surf.Normal, l);
                    if (nDotL_nee > 0f && Vec3.Dot(hit.Norm, l) > 0f && !Occluded(new Ray(hitPos, l), counters))
                    {
                        counters.Bump(Counters.Type.SampleNee);

                        // Evaluate full BSDF × cosTheta at light direction.
                        Vec3 h_nee = (l - ray.Dir).NormalizeOr(surf.Normal); // normalize(l + view)
                        float nDotH_nee = float.Clamp(Vec3.Dot(surf.Normal, h_nee), 1e-6f, 1f);
                        float hDotV_nee = MathF.Max(1e-6f, Vec3.Dot(h_nee, -ray.Dir));
                        float d_nee = GgxD(nDotH_nee, roughnessSqr);
                        float g_nee = SmithG1(nDotV, roughnessSqr) * SmithG1(nDotL_nee, roughnessSqr);
                        Color f_nee = FresnelSchlick(hDotV_nee, baseReflectivity);
                        Color specBrdf = f_nee * (d_nee * g_nee / (4f * nDotV));
                        Color diffBrdf = (Color.White - FresnelSchlick(nDotL_nee, baseReflectivity)) * surf.Color * (nDotL_nee / MathF.PI);
                        Color bsdfTimesCos = specBrdf + diffBrdf;

                        // MIS: power heuristic combining light PDF and BSDF mixture PDF.
                        float bsdfPdf_nee = specProbability * d_nee * nDotH_nee / (4f * hDotV_nee)
                                          + (1f - specProbability) * nDotL_nee / MathF.PI;
                        float misWeight_nee = PowerHeuristic(lightSample.Pdf, bsdfPdf_nee);

                        Color neeContrib = Sky.Radiance(l) * bsdfTimesCos * (misWeight_nee / lightSample.Pdf) * energy;
                        Debug.Assert(neeContrib.IsFinite);
                        radiance += neeContrib;
                    }
                }

                // BSDF scatter: pick specular or diffuse, track scatter PDF for MIS on next miss.
                Vec3 scatterDir;
                if (rng.NextFloat() < specProbability)
                {
                    // Specular: GGX importance sampling.
                    Vec3 halfVec = GgxSpecularHalfVector(surf, roughnessSqr, ref rng);
                    scatterDir = Vec3.Reflect(ray.Dir, halfVec);

                    float nDotL = MathF.Max(0f, Vec3.Dot(surf.Normal, scatterDir));
                    float nDotH = float.Clamp(Vec3.Dot(surf.Normal, halfVec), 1e-6f, 1f);
                    float hDotV = MathF.Max(1e-6f, Vec3.Dot(halfVec, -ray.Dir));

                    // BRDF weight: G * F * hDotV / (nDotV * nDotH) divided by specProbability.
                    Color f = FresnelSchlick(hDotV, baseReflectivity);
                    float g = SmithG1(nDotV, roughnessSqr) * SmithG1(nDotL, roughnessSqr);
                    energy *= f * (g * hDotV / (nDotV * nDotH)) / specProbability;
                    Debug.Assert(energy.IsFinite);

                    // Mixture PDF: specProb × D × nDotH / (4 × hDotV).
                    scatterPdf = specProbability * GgxD(nDotH, roughnessSqr) * nDotH / (4f * hDotV);
                }
                else
                {
                    // Diffuse (cosine-weighted Lambert).
                    scatterDir = (surf.Normal + Vec3.RandOnSphere(ref rng)).NormalizeOr(surf.Normal);
                    energy *= diffuseColor / (1f - specProbability);
                    Debug.Assert(energy.IsFinite);

                    // Mixture PDF: (1 - specProb) × nDotL / π.
                    float nDotL = MathF.Max(0f, Vec3.Dot(surf.Normal, scatterDir));
                    scatterPdf = (1f - specProbability) * nDotL / MathF.PI;
                }

                // Clamp scatter ray to stay above the geometric surface.
                if (Vec3.Dot(scatterDir, hit.Norm) <= 0f)
                    scatterDir = (scatterDir - 2f * Vec3.Dot(scatterDir, hit.Norm) * hit.Norm).NormalizeOr(hit.Norm);

                ray = new Ray(hitPos, scatterDir);
            }
            else
            {
                counters.Bump(Counters.Type.SampleMiss);

                // Sky hit: MIS-weight the BSDF contribution against the NEE technique.
                // Primary rays hitting the sky directly have no prior BSDF sample, so no MIS.
                if (scatterPdf > 0f)
                {
                    float lightPdf = Sky!.LightDir(ray.Dir)?.Pdf ?? 0f;
                    float misWeight = PowerHeuristic(scatterPdf, lightPdf);
                    Color skyContrib = surf.Radiance * energy * misWeight;
                    radiance += skyContrib;
                }
                else
                {
                    radiance += surf.Radiance * energy;
                }
                break;
            }
        }
        Debug.Assert(radiance.IsFinite);
        return new Fragment(radiance, normal, uv, depth);
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

    // Compute a GGX half-vector for given roughness.
    // https://www.pbr-book.org/3ed-2018/Reflection_Models/Microfacet_Models
    private static Vec3 GgxSpecularHalfVector(Surface surf, float roughnessSqr, ref Rng rng)
    {
        float u1 = rng.NextFloat();
        float u2 = rng.NextFloat();
        float cosTheta = MathF.Sqrt((1f - u1) / (u1 * (roughnessSqr * roughnessSqr - 1f) + 1f));
        float sinTheta = MathF.Sqrt(MathF.Max(0f, 1f - cosTheta * cosTheta));
        float phi = 2f * MathF.PI * u2;
        Vec3 dir = new Vec3(sinTheta * MathF.Cos(phi), sinTheta * MathF.Sin(phi), cosTheta);
        return surf.TransformDir(dir);
    }

    // GGX normal distribution function.
    // https://www.pbr-book.org/3ed-2018/Reflection_Models/Microfacet_Models
    private static float GgxD(float nDotH, float roughnessSqr)
    {
        float alphaSqr = roughnessSqr * roughnessSqr;
        float cos2 = nDotH * nDotH;
        // Rearranged as (1-cos²) + cos²·α² to avoid float cancellation when nDotH≈1, α≈0.
        float denom = (1f - cos2) + cos2 * alphaSqr;
        return alphaSqr / (MathF.PI * denom * denom);
    }

    // MIS power heuristic with exponent 2: pA² / (pA² + pB²).
    // https://www.pbr-book.org/3ed-2018/Monte_Carlo_Integration/Importance_Sampling#PowerHeuristic
    private static float PowerHeuristic(float pA, float pB)
    {
        float pA2 = pA * pA;
        float pB2 = pB * pB;
        return pA2 / (pA2 + pB2);
    }

    // Fraction of microfacets visible from a given direction.
    // https://www.pbr-book.org/3ed-2018/Reflection_Models/Microfacet_Models
    // k = alpha/2 (IBL form, Karis 2013), where alpha = roughnessSqr = r².
    private static float SmithG1(float nDotX, float alpha)
    {
        float k = alpha / 2f;
        return nDotX / (nDotX * (1f - k) + k);
    }

    // Schlick approximation of the Fresnel equations.
    // https://en.wikipedia.org/wiki/Schlick%27s_approximation
    private static Color FresnelSchlick(float nDotV, Color baseReflectivity)
    {
        float x = 1f - nDotV;
        float x2 = x * x;
        float x5 = x2 * x2 * x;
        return baseReflectivity + (Color.White - baseReflectivity) * x5;
    }
}
