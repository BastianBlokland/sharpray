using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

// Axis-angle rotation step. Multiple steps are composed left-to-right.
class RotationConfig
{
    public float[] Axis { get; init; } = [0f, 1f, 0f];
    public float AngleDeg { get; init; } = 0f;
}

class TransformConfig
{
    public float[] Pos { get; init; } = [0f, 0f, 0f];
    public List<RotationConfig>? Rotation { get; init; }
    public float[]? Scale { get; init; }
}

class TextureConfig
{
    public string Path { get; init; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TextureKind Kind { get; init; } = TextureKind.Srgb;
    public float[]? Tiling { get; init; }
}

enum TextureKind { Srgb, Linear, Hdr }

class MaterialConfig
{
    public float[] Albedo { get; init; } = [1f, 1f, 1f];
    public float Roughness { get; init; } = 0.5f;
    public float Metallic { get; init; } = 0f;
    public float Transparency { get; init; } = 0f;
    public float Ior { get; init; } = 1.5f;
    public float[]? Radiance { get; init; }
    public TextureConfig? ColorTexture { get; init; }
    public TextureConfig? RoughnessTexture { get; init; }
    public TextureConfig? MetallicTexture { get; init; }
    public TextureConfig? NormalTexture { get; init; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SphereShapeConfig), "sphere")]
[JsonDerivedType(typeof(AABoxShapeConfig), "aabox")]
[JsonDerivedType(typeof(BoxShapeConfig), "box")]
[JsonDerivedType(typeof(PlaneShapeConfig), "plane")]
[JsonDerivedType(typeof(ObjShapeConfig), "obj")]
abstract class ShapeConfig { }

class SphereShapeConfig : ShapeConfig
{
    public float[] Center { get; init; } = [0f, 0f, 0f];
    public float Radius { get; init; } = 1f;
}

class AABoxShapeConfig : ShapeConfig
{
    public float[] Min { get; init; } = [-0.5f, -0.5f, -0.5f];
    public float[] Max { get; init; } = [0.5f, 0.5f, 0.5f];
}

class BoxShapeConfig : ShapeConfig
{
    public float[] Size { get; init; } = [1f, 1f, 1f];
}

class PlaneShapeConfig : ShapeConfig
{
    public float[] Normal { get; init; } = [0f, 1f, 0f];
    public float Distance { get; init; } = 0f;
}

class ObjShapeConfig : ShapeConfig
{
    public string Path { get; init; } = "";
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SkyTextureConfig), "texture")]
[JsonDerivedType(typeof(SkyProceduralConfig), "procedural")]
abstract class SkyConfig { }

class SkyTextureConfig : SkyConfig
{
    public string Path { get; init; } = "";
    public float Multiplier { get; init; } = 1f;
    public float RotYawDeg { get; init; } = 0f;
}

class SkyProceduralConfig : SkyConfig
{
    public float[] Top { get; init; } = [0.08f, 0.17f, 0.70f];
    public float[] Middle { get; init; } = [0.50f, 0.65f, 0.90f];
    public float[] Bottom { get; init; } = [0.12f, 0.09f, 0.07f];
    public float[] SunDir { get; init; } = [0.5f, 1f, -0.5f];
    public float[] SunRadiance { get; init; } = [100000f, 90000f, 65000f];
    public float SunAngleDeg { get; init; } = 0.53f;
}

class FogConfig
{
    public float Density { get; init; } = 0.01f;
    public float[] Color { get; init; } = [1f, 1f, 1f];
    public float Anisotropy { get; init; } = 0f;
    public float HeightFalloff { get; init; } = 0f;
}

class CameraConfig
{
    public float[] Pos { get; init; } = [0f, 0f, 0f];
    public List<RotationConfig>? Rotation { get; init; }
    public float FovDeg { get; init; } = 60f;
}

class ObjectConfig
{
    public string Name { get; init; } = "";
    public TransformConfig? Transform { get; init; }
    public MaterialConfig? Material { get; init; }
    public ShapeConfig Shape { get; init; } = null!;
}

class SceneConfig
{
    public SkyConfig Sky { get; init; } = null!;
    public FogConfig? Fog { get; init; }
    public CameraConfig Camera { get; init; } = new CameraConfig();
    public List<ObjectConfig> Objects { get; init; } = [];
}

class RenderConfig
{
    public uint Width { get; init; } = 1280;
    public uint Height { get; init; } = 720;
    public uint BlockSize { get; init; } = 32;
    public uint MinSamples { get; init; } = 64;
    public uint MaxSamples { get; init; } = 2048;
    public float VarianceThreshold { get; init; } = 0.075f;
    public uint Bounces { get; init; } = 8;
    public float IndirectClamp { get; init; } = 10f;
    public bool DumpScene { get; init; } = true;
    public bool OutputImage { get; init; } = true;
    public bool OutputPreview { get; init; } = true;
    public bool OutputNormal { get; init; } = true;
    public bool OutputUv { get; init; } = true;
    public bool OutputDepth { get; init; } = true;
    public bool OutputSamples { get; init; } = true;
    public bool OutputVariance { get; init; } = true;
    public uint PreviewInterval { get; init; } = 100;
}

class CompositeConfig
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Tonemapper Tonemapper { get; init; } = Tonemapper.LinearSmooth;
    public float Exposure { get; init; } = 1.0f;
    public float DenoiseRadius { get; init; } = 0.0075f;
    public float DenoiseStrength { get; init; } = 0.1f;
    public float DenoiseStrengthMax { get; init; } = 1.75f;
    public float DenoiseLuminanceBoost { get; init; } = 0.25f;
    public float DenoiseLuminanceLimit { get; init; } = 2f;
    public float DenoiseNormalLimit { get; init; } = 0.125f;
    public float DenoiseDepthLimit { get; init; } = 0.2f;
    public float DenoiseFogRadius { get; init; } = 0.002f;
    public float DenoiseFogStrength { get; init; } = 0.05f;
}

class Config
{
    public RenderConfig Render { get; init; } = new RenderConfig();
    public CompositeConfig Composite { get; init; } = new CompositeConfig();
    public SceneConfig Scene { get; init; } = null!;

    private static readonly JsonSerializerOptions _options = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static Config Load(string path)
    {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<Config>(json, _options)
            ?? throw new Exception($"Failed to deserialize config from '{path}'");
    }
}

static class ConfigConvert
{
    public static Vec3 ToVec3(float[] v) => new Vec3(v[0], v[1], v[2]);
    public static Color ToColor(float[] v) => new Color(v[0], v[1], v[2]);
    public static Vec2 ToVec2(float[] v) => new Vec2(v[0], v[1]);

    public static Quat ToQuat(List<RotationConfig>? rotations)
    {
        if (rotations == null || rotations.Count == 0)
            return Quat.Identity();
        return rotations.Aggregate(Quat.Identity(), (q, r) =>
            q * Quat.AngleAxis(float.DegreesToRadians(r.AngleDeg), ToVec3(r.Axis).Normalize()));
    }

    public static Transform ToTransform(TransformConfig? t)
    {
        if (t == null) return Transform.Identity();
        Vec3 pos = ToVec3(t.Pos);
        Quat rot = ToQuat(t.Rotation);
        Vec3 scale = t.Scale != null ? ToVec3(t.Scale) : new Vec3(1f, 1f, 1f);
        return new Transform(pos, rot, scale);
    }

    public static Texture ToTexture(TextureConfig t)
    {
        Texture tex = t.Kind switch
        {
            TextureKind.Srgb   => Texture.FromSrgb(Image.Load(t.Path)),
            TextureKind.Linear => Texture.FromLinear(Image.Load(t.Path)),
            TextureKind.Hdr    => Texture.FromHdr(ImageHdr.Load(t.Path)),
            _ => throw new Exception($"Unknown texture kind: {t.Kind}")
        };
        if (t.Tiling != null)
            tex.Tiling = ToVec2(t.Tiling);
        return tex;
    }

    public static Material ToMaterial(MaterialConfig? m)
    {
        if (m == null) return new Material(Color.White, 0.5f);
        return new Material(
            Albedo: ToColor(m.Albedo),
            Roughness: m.Roughness,
            Metallic: m.Metallic,
            Transparency: m.Transparency,
            Ior: m.Ior,
            Radiance: m.Radiance != null ? ToColor(m.Radiance) : default,
            ColorTexture: m.ColorTexture != null ? ToTexture(m.ColorTexture) : null,
            RoughnessTexture: m.RoughnessTexture != null ? ToTexture(m.RoughnessTexture) : null,
            MetallicTexture: m.MetallicTexture != null ? ToTexture(m.MetallicTexture) : null,
            NormalTexture: m.NormalTexture != null ? ToTexture(m.NormalTexture) : null);
    }

    public static ISky ToSky(SkyConfig sky) => sky switch
    {
        SkyTextureConfig t => new SkyTexture(
            Texture.FromHdr(ImageHdr.Load(t.Path)),
            t.Multiplier,
            float.DegreesToRadians(t.RotYawDeg)),
        SkyProceduralConfig p => new SkyProcedural(
            ToColor(p.Top),
            ToColor(p.Middle),
            ToColor(p.Bottom),
            ToVec3(p.SunDir).Normalize(),
            ToColor(p.SunRadiance),
            float.DegreesToRadians(p.SunAngleDeg)),
        _ => throw new Exception($"Unknown sky type: {sky.GetType().Name}")
    };

    public static Fog ToFog(FogConfig f) =>
        new Fog(f.Density, ToColor(f.Color), f.Anisotropy, f.HeightFalloff);

    public static View ToView(CameraConfig c) =>
        new View(
            new Transform(ToVec3(c.Pos), ToQuat(c.Rotation)),
            float.DegreesToRadians(c.FovDeg));

    // Returns null for obj shapes — those must be loaded via ObjLoader.
    public static IShape? ToShape(ShapeConfig shape) => shape switch
    {
        SphereShapeConfig s  => new Sphere(ToVec3(s.Center), s.Radius),
        AABoxShapeConfig b   => new AABox(ToVec3(b.Min), ToVec3(b.Max)),
        BoxShapeConfig b     => Box.FromCenter(Vec3.Zero, ToVec3(b.Size), Quat.Identity()),
        PlaneShapeConfig p   => new Plane(ToVec3(p.Normal).Normalize(), p.Distance),
        ObjShapeConfig       => null,
        _ => throw new Exception($"Unknown shape type: {shape.GetType().Name}")
    };
}
