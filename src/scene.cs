
using System;
using System.Diagnostics;
using System.Collections.Generic;

readonly record struct SampleLight(Vec3 Dir, float Pdf);

readonly record struct SampleScatter(
    Vec3 Dir,
    Color EnergyWeight,
    float? Pdf = null, // Probability Density Function, likelihood of the scatter being chosen.
    bool Refracted = false
);

readonly record struct Surface(
    Color Radiance,
    Color Albedo,
    float Roughness,
    float Metallic,
    float Transparency,
    float Ior, // Index of refraction, 1.0 = no bending, 1.5 glass, 2.42 diamond.
    Vec3 Normal,
    Vec3 NormalGeo,
    Vec4 Tangent,
    Vec2 Uv)
{
    // Clamp dir to the same side of NormalGeo as sideHint.
    private Vec3 ClampToGeoSide(Vec3 dir, Vec3 sideHint)
    {
        float dot = Vec3.Dot(dir, NormalGeo);
        if (dot * Vec3.Dot(sideHint, NormalGeo) <= 0f)
            dir = (dir - 2f * dot * NormalGeo).NormalizeOr(sideHint);
        return dir;
    }

    private float SpecularProbability(Vec3 viewDir)
    {
        Debug.Assert(viewDir.IsUnit);
        float nDotV = MathF.Max(1e-4f, Vec3.Dot(Normal, viewDir));
        float fresnel = Brdf.Fresnel(nDotV, Brdf.BaseReflectivity(Ior, Albedo, Metallic)).Luminance;
        return float.Clamp(MathF.Max(fresnel, Metallic), 0.001f, 0.999f);
    }

    private Vec3 SpecularDir(Vec3 incomingDir, ref Rng rng)
    {
        Debug.Assert(incomingDir.IsUnit);
        float roughnessSqr = MathF.Max(Roughness * Roughness, 1e-4f);
        Vec3 halfVecLocal = Brdf.GgxSampleLocal(roughnessSqr, Vec2.Rand(ref rng));
        Vec3 halfVecWorld = Vec3.FromTangentSpace(halfVecLocal, Tangent, Normal);
        return Vec3.Reflect(incomingDir, halfVecWorld);
    }

    // Fraction of light energy that gets reflected from lightDir toward viewDir.
    public Color OpaqueEval(Vec3 viewDir, Vec3 lightDir)
    {
        Debug.Assert(viewDir.IsUnit && lightDir.IsUnit);

        float nDotL = Vec3.Dot(Normal, lightDir);
        if (nDotL <= 0f)
            return Color.Black; // Behind the surface.

        float nDotV = MathF.Max(1e-4f, Vec3.Dot(Normal, viewDir));
        float roughnessSqr = MathF.Max(Roughness * Roughness, 1e-4f);
        Color baseReflectivity = Brdf.BaseReflectivity(Ior, Albedo, Metallic);

        Vec3 halfVec = (viewDir + lightDir).NormalizeOr(Normal);
        float nDotH = Vec3.Dot(Normal, halfVec);
        float hDotV = Vec3.Dot(halfVec, viewDir);

        // Specular: D*F*G / (4*nDotV*nDotL) * nDotL = D*F*G / (4*nDotV).
        Color f = Brdf.Fresnel(hDotV, baseReflectivity);
        float g = Brdf.SmithG1(nDotV, roughnessSqr) * Brdf.SmithG1(nDotL, roughnessSqr);
        Color specular = Brdf.GgxD(nDotH, roughnessSqr) * f * g / (4f * nDotV);

        // Diffuse: Lambert weighted by (1 - F) to avoid double-counting specular energy.
        // Metals have no diffuse, so scaled by (1 - Metallic).
        Color diffuse = (Color.White - Brdf.Fresnel(nDotV, baseReflectivity)) * Albedo * (1f - Metallic) * (nDotL / MathF.PI);

        return (specular + diffuse) * (1f - Transparency);
    }

    // Probability Density Function, likelihood of the light direction being chosen.
    public float OpaquePdf(Vec3 viewDir, Vec3 lightDir) =>
        OpaquePdf(viewDir, lightDir, SpecularProbability(viewDir));

    private float OpaquePdf(Vec3 viewDir, Vec3 lightDir, float specProbability)
    {
        float nDotL = Vec3.Dot(Normal, lightDir);
        if (nDotL <= 0f)
            return 0f; // Behind the surface.

        Vec3 halfVec = (viewDir + lightDir).NormalizeOr(Normal);
        float nDotH = MathF.Max(0f, Vec3.Dot(Normal, halfVec));
        float hDotV = MathF.Max(1e-6f, Vec3.Dot(halfVec, viewDir));

        float roughnessSqr = MathF.Max(Roughness * Roughness, 1e-4f);
        float pdfSpec = Brdf.GgxD(nDotH, roughnessSqr) * nDotH / (4f * hDotV);
        float pdfDiff = nDotL / MathF.PI;

        return (specProbability * pdfSpec + (1f - specProbability) * pdfDiff) * (1f - Transparency);
    }

    private SampleScatter OpaqueScatter(Vec3 incomingDir, Vec3 viewDir, ref Rng rng)
    {
        float specProbability = SpecularProbability(viewDir);

        Vec3 scatterDir;
        if (rng.NextFloat() < specProbability)
            scatterDir = SpecularDir(incomingDir, ref rng); // Specular scatter.
        else
            scatterDir = (Normal + Vec3.RandOnSphere(ref rng)).NormalizeOr(Normal); // Diffuse scatter.

        scatterDir = ClampToGeoSide(scatterDir, NormalGeo);

        float pdf = OpaquePdf(viewDir, scatterDir, specProbability);
        return new SampleScatter(scatterDir, OpaqueEval(viewDir, scatterDir) / MathF.Max(pdf, 1e-6f), pdf);
    }

    private SampleScatter TransparentScatter(Vec3 incomingDir, Vec3 viewDir, ref Rng rng)
    {
        bool entering = Vec3.Dot(incomingDir, NormalGeo) < 0f;
        Vec3 refractNormal = entering ? NormalGeo : -NormalGeo;
        float iorRatio = entering ? 1f / Ior : Ior;

        // Sample a GGX half-vector facing the incoming ray's side.
        float roughnessSqr = MathF.Max(Roughness * Roughness, 1e-6f);
        Vec3 halfVec = Vec3.FromTangentSpace(Brdf.GgxSampleLocal(roughnessSqr, Vec2.Rand(ref rng)), Tangent, Normal);
        if (!entering)
            halfVec = -halfVec;

        // Fall back to geo normal if the half-vec faces away from the incoming ray.
        if (Vec3.Dot(incomingDir, halfVec) > 0f)
            halfVec = refractNormal;

        // Fresnel using the GGX half-vector.
        float hDotV = MathF.Max(1e-4f, Vec3.Dot(halfVec, viewDir));
        float fresnel = Brdf.Fresnel(hDotV, Brdf.BaseReflectivity(Ior, Albedo, Metallic)).Luminance;

        if (rng.NextFloat() >= fresnel && Brdf.Refract(incomingDir, halfVec, iorRatio) is Vec3 refractDir)
            return new SampleScatter(ClampToGeoSide(refractDir, -refractNormal), Color.White, Refracted: true);

        return new SampleScatter(ClampToGeoSide(Vec3.Reflect(incomingDir, halfVec), refractNormal), Color.White);
    }

    public SampleScatter Scatter(Vec3 incomingDir, Vec3 viewDir, ref Rng rng)
    {
        if (rng.NextFloat() < Transparency)
            return TransparentScatter(incomingDir, viewDir, ref rng);
        return OpaqueScatter(incomingDir, viewDir, ref rng);
    }
}

readonly record struct Fragment(
    Color Radiance,
    Color RadianceFog, // Fog scatter bounce radiance for the primary ray.
    Vec3? Normal,
    Vec2? Uv,
    float? Depth);

readonly record struct Material(
    Color Albedo,
    float Roughness,
    float Metallic = 0f,
    float Transparency = 0f, // 1.0 is fully transparent.
    float Ior = 1.5f, // Index of refraction, 1.0 = no bending, 1.5 glass, 2.42 diamond.
    Color Radiance = default,
    Texture? ColorTexture = null,
    Texture? RoughnessTexture = null,
    Texture? MetallicTexture = null,
    Texture? NormalTexture = null) : IDescribable
{
    public Color SampleAlbedo(Vec2 uv) =>
        (ColorTexture?.Sample(uv) ?? Color.White) * Albedo;

    public float SampleRoughness(Vec2 uv) =>
        (RoughnessTexture?.Sample(uv).R ?? 1.0f) * Roughness;

    public float SampleMetallic(Vec2 uv) =>
        (MetallicTexture?.Sample(uv).R ?? 1.0f) * Metallic;

    public Vec3 SampleNormal(Vec2 uv, Vec3 geoNorm, Vec4 geoTan)
    {
        if (NormalTexture == null)
            return geoNorm;
        return Vec3.FromTangentSpace(NormalTexture.SampleNormal(uv), geoTan, geoNorm).NormalizeOr(geoNorm);
    }

    public void Describe(FormatWriter fmt)
    {
        fmt.WriteLine($"albedo={Albedo}");
        ColorTexture?.DescribeIndented(fmt);
        fmt.WriteLine($"roughness={Roughness:G3}");
        RoughnessTexture?.DescribeIndented(fmt);
        fmt.WriteLine($"metallic={Metallic:G3}");
        MetallicTexture?.DescribeIndented(fmt);
        fmt.WriteLine($"transparency={Transparency:G3}");
        fmt.WriteLine($"ior={Ior:G3}");
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
    SampleLight? LightDir(Vec3 dir);
    SampleLight? LightDirRand(ref Rng rng);
}

readonly struct SunProcedural : IDescribable
{
    private readonly Vec3 _dir;
    private readonly float _angle;
    private readonly Color _radiance;
    private readonly float _angleCos;
    private readonly Quat _rot;
    private readonly float _pdfDenom; // pi * (1 - cos(theta_max)), normalization for importance-sampled PDF.

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

    public SampleLight? LightDir(Vec3 dir)
    {
        float cosTheta = Vec3.Dot(dir, _dir);
        if (cosTheta < _angleCos)
            return null;
        float blend = (cosTheta - _angleCos) / (1f - _angleCos);
        return new SampleLight(dir, blend / _pdfDenom);
    }

    public SampleLight LightDirRand(ref Rng rng)
    {
        // Importance sample proportional to blend.
        float phi = MathF.PI * 2f * rng.NextFloat();
        float blend = MathF.Sqrt(rng.NextFloat());
        float z = _angleCos + (1f - _angleCos) * blend;
        float sinT = MathF.Sqrt(MathF.Max(0f, 1f - z * z));
        Vec3 dir = _rot * new Vec3(MathF.Cos(phi) * sinT, MathF.Sin(phi) * sinT, z);
        return new SampleLight(dir, blend / _pdfDenom);
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

    public SampleLight? LightDirRand(ref Rng rng) => Sun.LightDirRand(ref rng);
    public SampleLight? LightDir(Vec3 dir) => Sun.LightDir(dir);

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
    private readonly float _multiplier;
    private readonly float _rotYaw;
    private readonly Quat _rot;
    private readonly Quat _rotInv;

    public SkyTexture(Texture texture, float multiplier = 1f, float rotYaw = 0f)
    {
        float Weight(Vec2i pos)
        {
            // sin(theta) for equirectangular area distortion: texels near poles cover less solid angle.
            return texture.Get(pos).Luminance * MathF.Sin(MathF.PI * (pos.Y + 0.5f) / texture.Size.Y);
        }

        _texture = texture;
        _cdf = new Cdf2(texture.Size, Weight);
        _pdfScale = texture.Size.X * texture.Size.Y / (_cdf.TotalWeight * 2f * MathF.PI * MathF.PI);
        _multiplier = multiplier;
        _rotYaw = rotYaw;
        _rot = Quat.AngleAxis(rotYaw, Vec3.Up);
        _rotInv = _rot.Inverse();
    }

    public Color Radiance(Vec3 dir) => _texture.Sample((_rotInv * dir).EquirectUv()) * _multiplier;

    public SampleLight? LightDirRand(ref Rng rng)
    {
        if (_cdf.TotalWeight <= 0f)
            return null;
        Vec2i texel = _cdf.SampleRand(ref rng);
        Vec3 dir = _rot * Vec3.FromEquirectUv((texel + 0.5f) / _texture.Size.ToFloat());
        float pdf = _texture.Get(texel).Luminance * _pdfScale;
        return pdf > 0f ? new SampleLight(dir, pdf) : null;
    }

    public SampleLight? LightDir(Vec3 dir)
    {
        if (_cdf.TotalWeight <= 0f)
            return null;
        Vec2 uv = (_rotInv * dir).EquirectUv();
        Vec2i coord = (uv * _texture.Size.ToFloat()).ToInt() % _texture.Size;
        float pdf = _texture.Get(coord).Luminance * _pdfScale;
        return pdf > 0f ? new SampleLight(dir, pdf) : null;
    }

    public void Describe(FormatWriter fmt)
    {
        fmt.WriteLine("type=SkyTexture");
        fmt.WriteLine($"multiplier={_multiplier:G3}");
        fmt.WriteLine($"yaw={float.RadiansToDegrees(_rotYaw):G3}deg");
        _texture.Describe(fmt);
    }
}

// Participating medium (exponential height density).
readonly record struct Fog(
    float Density,
    Color Color,
    float Anisotropy, // [-1 to 1] exclusive. Positive for forward-scattering (fog / dust), negative for back-scattering (snow).
    float HeightFalloff) // Exponential density falloff with height.
    : IDescribable
{
    public float DensityAtHeight(float y) => Density * MathF.Exp(-HeightFalloff * y);

    public float Transmittance(RaySegment seg)
    {
        return MathF.Exp(-OpticalDepth(seg)); // Beer-Lambert law.
    }

    public float OpticalDepth(RaySegment seg)
    {
        Debug.Assert(HeightFalloff > 0f);

        float heightDelta = seg.Ray.Dir.Y;
        float heightStart = seg.Ray.Origin.Y + seg.Start * heightDelta;
        float heightEnd = seg.Ray.Origin.Y + seg.End * heightDelta;

        float densityStart = DensityAtHeight(heightStart);
        float densityEnd = DensityAtHeight(heightEnd);

        if (MathF.Abs(heightDelta) < 1e-6f)
        {
            return densityStart * seg.Length; // Horizontal ray; constant density.
        }
        return (densityStart - densityEnd) / (HeightFalloff * heightDelta);
    }

    public float? ScatterDistance(RaySegment seg, ref Rng rng)
    {
        float heightDelta = seg.Ray.Dir.Y;
        float heightOrigin = seg.Ray.Origin.Y;
        float heightStart = heightOrigin + seg.Start * heightDelta;

        float u = rng.NextExponential(); // Optical depth budget.

        // Find out how far into the fog.
        float t;
        if (MathF.Abs(heightDelta) < 1e-6f)
        {
            float densityStart = DensityAtHeight(heightStart);
            if (densityStart <= 0f)
                return null; // Ray escapes.
            t = seg.Start + u / densityStart;
        }
        else
        {
            float rhs = MathF.Exp(-HeightFalloff * heightStart) - u * HeightFalloff * heightDelta / Density;
            if (rhs <= 0f)
                return null; // Ray escapes.
            float yt = -MathF.Log(rhs) / HeightFalloff;
            t = (yt - heightOrigin) / heightDelta;
        }
        return t < seg.End ? t : null;
    }

    public void Describe(FormatWriter fmt)
    {
        fmt.WriteLine($"density={Density:G3}");
        fmt.WriteLine($"color={Color}");
        fmt.WriteLine($"anisotropy={Anisotropy:G3}");
        fmt.WriteLine($"heightFalloff={HeightFalloff:G3}");
    }
}

class Scene : IDescribable
{
    private ISky? _sky;
    private Fog? _fog;

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
        if (_sky == null)
            throw new InvalidOperationException("Scene sky is required");

        using (counters?.TimeScope(Counters.Type.TimeSceneBvhBuild))
        {
            const float sahCostIntersect = 10f; // High as object tests are expensive.
            const int splitBinCount = 32; // Evaluate many splits for the Scene bvh.
            _bvh = new Bvh<Object, ShapeHit>(_objects, sahCostIntersect: sahCostIntersect, splitBinCount: splitBinCount);
        }
        if (counters != null)
        {
            BvhStats stats = _bvh!.GetStats();
            counters.Bump(Counters.Type.SceneObject, _objects.Count);
            counters.Bump(Counters.Type.SceneBvhNodes, stats.NodeCount);
            counters.Bump(Counters.Type.SceneBvhDepth, stats.DepthMax);
        }
    }

    public void SetSky(ISky sky)
    {
        lock (_lock)
        {
            if (_built)
                throw new InvalidOperationException("Scene already built");
            _sky = sky;
        }
    }

    public void SetFog(Fog fog)
    {
        lock (_lock)
        {
            if (_built)
                throw new InvalidOperationException("Scene already built");
            _fog = fog;
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

    // Returns the color transmittance along a shadow ray, tracing through transparent surfaces.
    private Color ShadowTransmittance(Ray ray, Counters counters)
    {
        Color transmittance = Color.White;
        do
        {
            counters.Bump(Counters.Type.SceneOcclude);
            if (_bvh!.Intersect(ray, counters) is not (ShapeHit hit, int idx))
                return transmittance; // Sky reached.

            Material mat = _objects[idx].Material;
            if (mat.Transparency <= 0f)
                return Color.Black; // Opaque surface reached.

            transmittance *= mat.SampleAlbedo(hit.Uv) * mat.Transparency;
            ray = new Ray(ray[hit.Dist + MathF.Max(1e-4f, hit.Dist * 1e-4f)], ray.Dir);

        } while (transmittance.MaxComponent > 1e-4f);

        return transmittance;
    }

    public (Surface, float Dist)? Trace(Ray ray, Counters counters)
    {
        if (!_built)
            throw new InvalidOperationException("Scene not built");
        counters.Bump(Counters.Type.SceneTrace);

        if (_bvh!.Intersect(ray, counters) is (ShapeHit hit, int idx))
        {
            Material mat = _objects[idx].Material;
            Color albedo = mat.SampleAlbedo(hit.Uv);
            float roughness = mat.SampleRoughness(hit.Uv);
            float metallic = mat.SampleMetallic(hit.Uv);
            Vec3 normal = mat.SampleNormal(hit.Uv, hit.Norm, hit.Tan);

            // Re-orthogonalize tangent.
            Vec3 tangentDir = (hit.Tan.Xyz - Vec3.Dot(hit.Tan.Xyz, normal) * normal).NormalizeOr(hit.Tan.Xyz);
            Vec4 tangent = new Vec4(tangentDir, hit.Tan.W);

            Surface surf = new Surface(mat.Radiance, albedo, roughness, metallic, mat.Transparency, mat.Ior, normal, hit.NormGeo, tangent, hit.Uv);
            return (surf, hit.Dist);
        }

        return null;
    }

    public Fragment Sample(Ray ray, ref Rng rng, uint bounces, float indirectClamp, Counters counters)
    {
        if (!_built)
            throw new InvalidOperationException("Scene not built");

        counters.Bump(Counters.Type.Sample);

        Color radiance = Color.Black, radianceFog = Color.Black, energy = Color.White;
        Vec3? normal = null;
        Vec2? uv = null;
        float? depth = null;
        float? scatterPdf = null; // Probability Density Function of the last scatter.
        Color? mediumAlbedo = null; // Non-null while the ray is inside a transparent object.

        for (uint i = 0; i != (bounces + 1); ++i)
        {
            counters.Bump(Counters.Type.SampleBounce);

            bool primaryRay = i == 0;
            (Surface, float Dist)? hit = Trace(ray, counters);

            RaySegment? fogSeg = _fog.HasValue ? new RaySegment(ray, end: hit?.Dist ?? float.PositiveInfinity) : null;
            if (fogSeg is RaySegment seg && _fog!.Value.ScatterDistance(seg, ref rng) is float scatterDist)
            {
                // Scatter on the fog.
                Fog fog = _fog!.Value;
                counters.Bump(Counters.Type.SampleFogScatter);

                energy *= fog.Color;

                Color scatterRadiance = SampleSkyDirectFog(ray[scatterDist], ray.Dir, ref rng, counters) * energy;
                if (primaryRay)
                    radianceFog += scatterRadiance;
                else
                    radiance += scatterRadiance;

                if (RussianRoulette(ref energy, i, ref rng, counters))
                    break;

                Vec3 scatterDir = Brdf.HgScatterDir(ray.Dir, fog.Anisotropy, ref rng);

                scatterPdf = Brdf.HgPdf(Vec3.Dot(ray.Dir, scatterDir), fog.Anisotropy);
                ray = new Ray(ray[scatterDist], scatterDir);
            }
            else if (hit is (Surface surf, float dist))
            {
                // Reached a surface.
                counters.Bump(Counters.Type.SampleHit);

                if (fogSeg.HasValue)
                    counters.Bump(Counters.Type.SampleFogEscape);

                Vec3 viewDir = -ray.Dir;
                float distBias = MathF.Max(1e-4f, dist * 1e-4f);
                Vec3 hitPos = ray[dist - distBias];

                // Beer-Lambert absorption: attenuate by the distance traveled through the medium.
                if (mediumAlbedo is Color albedo)
                    energy *= Brdf.BeerLawTransmittance(albedo, dist);

                // Accumulate the surface radiance.
                radiance += surf.Radiance * energy;

                if (i == 0)
                {
                    // Save surface definition for the primary surface.
                    normal = surf.Normal;
                    uv = surf.Uv;
                    depth = dist;
                }

                // Direct sky illumination.
                radiance += SampleSkyDirect(surf, hitPos, viewDir, ref rng, counters) * energy;

                if (RussianRoulette(ref energy, i, ref rng, counters))
                    break;

                // Compute a scatter ray and accumulate its weight.
                SampleScatter scatter = surf.Scatter(ray.Dir, viewDir, ref rng);
                energy *= scatter.EnergyWeight;
                energy = energy.ClampLuminance(indirectClamp); // Combat fireflies.
                Debug.Assert(energy.IsFinite);

                bool enterSurface = Vec3.Dot(scatter.Dir, surf.NormalGeo) < 0f;

                if (scatter.Refracted)
                    mediumAlbedo = enterSurface ? surf.Albedo : null;

                scatterPdf = scatter.Pdf;
                ray = new Ray(ray[dist] + scatter.Dir * distBias, scatter.Dir);
            }
            else
            {
                // Reached the sky.
                counters.Bump(Counters.Type.SampleMiss);

                if (fogSeg.HasValue)
                    counters.Bump(Counters.Type.SampleFogEscape);

                // Sample the sky radiance, use MIS (Multiple Importance Sampling) avoid double counting
                // the sky radiance we already added during the scatter.
                float misWeight = scatterPdf is float pdf
                    ? Brdf.PowerHeuristic(pdf, _sky?.LightDir(ray.Dir)?.Pdf ?? 0f)
                    : 1f;

                radiance += (_sky?.Radiance(ray.Dir) ?? Color.Black) * misWeight * energy;
                break;
            }
        }
        Debug.Assert(radiance.IsFinite);
        Debug.Assert(radianceFog.IsFinite);
        return new Fragment(radiance, radianceFog, normal, uv, depth);
    }

    // Russian roulette: terminate low-energy paths, compensate survivors.
    private bool RussianRoulette(ref Color energy, uint bounce, ref Rng rng, Counters counters)
    {
        if (bounce < 3)
            return false;
        float survive = MathF.Min(1f, energy.MaxComponent);
        if (rng.NextFloat() >= survive)
        {
            counters.Bump(Counters.Type.SampleTerminate);
            return true;
        }
        energy /= survive;
        return false;
    }

    private (SampleLight Light, Color Transmittance)? SampleSkyLight(Vec3 pos, ref Rng rng, Counters counters)
    {
        if (_sky?.LightDirRand(ref rng) is not SampleLight light || light.Pdf <= 0f)
            return null;

        Ray shadowRay = new Ray(pos, light.Dir);
        Color transmittance = ShadowTransmittance(shadowRay, counters);
        if (transmittance.MaxComponent <= 0f)
            return null;

        if (_fog is Fog fog)
            transmittance *= fog.Transmittance(new RaySegment(shadowRay));

        return (light, transmittance);
    }

    private Color SampleSkyDirect(Surface surf, Vec3 hitPos, Vec3 viewDir, ref Rng rng, Counters counters)
    {
        if (SampleSkyLight(hitPos, ref rng, counters) is not (var light, var transmittance))
            return Color.Black;

        Vec3 normal = Vec3.Dot(surf.Normal, viewDir) >= 0f ? surf.Normal : -surf.Normal;
        if (Vec3.Dot(normal, light.Dir) <= 0f)
            return Color.Black; // Light is behind the shading normal.
        Vec3 normalGeo = Vec3.Dot(surf.NormalGeo, viewDir) >= 0f ? surf.NormalGeo : -surf.NormalGeo;
        if (Vec3.Dot(normalGeo, light.Dir) <= 0f)
            return Color.Black; // Light is behind the geometric surface.

        Color surfReflectance = surf.OpaqueEval(viewDir, light.Dir);
        float surfPdf = surf.OpaquePdf(viewDir, light.Dir);

        // Compute the weight by combining the light dir probability and the surface probability
        // using MIS (Multiple Importance Sampling).
        float misWeight = Brdf.PowerHeuristic(light.Pdf, surfPdf);

        return _sky!.Radiance(light.Dir) * transmittance * surfReflectance * misWeight / light.Pdf;
    }

    private Color SampleSkyDirectFog(Vec3 pos, Vec3 rayDir, ref Rng rng, Counters counters)
    {
        Debug.Assert(_fog != null);
        if (SampleSkyLight(pos, ref rng, counters) is not (SampleLight light, Color transmittance))
            return Color.Black;

        float rDotL = Vec3.Dot(rayDir, light.Dir);
        float fogPdf = Brdf.HgPdf(rDotL, _fog!.Value.Anisotropy);

        // Compute the weight by combining the light dir probability and the fog probability
        // using MIS (Multiple Importance Sampling).
        float misWeight = Brdf.PowerHeuristic(light.Pdf, fogPdf);

        return _sky!.Radiance(light.Dir) * transmittance * fogPdf * misWeight / light.Pdf;
    }

    public void Describe(FormatWriter fmt)
    {
        if (!_built)
            throw new InvalidOperationException("Scene not built");

        _sky?.DescribeIndented("sky", fmt);
        fmt.Separate();

        if (_fog is Fog fog)
        {
            fog.DescribeIndented("fog", fmt);
            fmt.Separate();
        }

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
