using System;
using System.IO;

void FlushToConsole(FormatWriter fmt)
{
    Console.Write(fmt.ToString());
    fmt.Clear();
}

void FlushToOverlay(FormatWriter fmt, Overlay overlay, Vec2i screenPos)
{
    overlay.AddText(fmt.ToString(), screenPos, Color.White);
    fmt.Clear();
}

FormatWriter fmt = new FormatWriter();

fmt.WriteLine("[SharpRay]");
fmt.WriteLine("> Performing setup");
FlushToConsole(fmt);

const uint width = 512;
const uint height = 512;
const uint blockSize = 32;
const uint minSamples = 64;
const uint maxSamples = 4096;
const float varianceThreshold = 0.1f;
const uint bounces = 4;
const float indirectClamp = 5f;
const Tonemapper tonemapper = Tonemapper.Reinhard;
const float exposure = 1.0f;
const float denoiseSigmaSpace = 6.0f;
const float denoiseSigmaColor = 0.05f;
const float denoiseSigmaNormal = 0.05f;
const float denoiseSigmaDepth = 1.0f;
const bool dumpScene = true;
const bool outputImage = true, outputPreview = true, outputNormal = true;
const bool outputUv = true, outputDepth = true, outputSamples = true;
const uint previewInterval = 100;

Counters counters = new Counters();
var timerTotal = counters.TimeScope(Counters.Type.TimeTotal);

String outputPath = Path.GetFullPath("output");
Directory.CreateDirectory(outputPath);

Overlay overlay = new Overlay();

Scene scene = new Scene();
using (counters.TimeScope(Counters.Type.TimeSetup))
{
    // Sky.
    Texture skyTexture = Texture.FromHdr(ImageHdr.Load("assets/qwantani_late_afternoon.hdr"));
    scene.Sky = new SkyTexture(skyTexture);

    // scene.Sky = new SkyProcedural(
    //     new Color(0.08f, 0.17f, 0.70f),
    //     new Color(0.50f, 0.65f, 0.90f),
    //     new Color(0.12f, 0.09f, 0.07f),
    //     new Vec3(0.5f, 1f, -0.5f).Normalize(),
    //     new Color(100000f, 90000f, 65000f), // ~5500K.
    //     float.DegreesToRadians(0.53f));

    // Floor.
    Texture floorColor = Texture.FromSrgb(Image.Load("assets/cobblestone/cobblestone_diff.tga"));
    Texture floorRough = Texture.FromLinear(Image.Load("assets/cobblestone/cobblestone_rough.tga"));
    Texture floorNormal = Texture.FromLinear(Image.Load("assets/cobblestone/cobblestone_nor.tga"));
    floorColor.Tiling = floorRough.Tiling = floorNormal.Tiling = new Vec2(6f, 1.5f);
    scene.AddObject(new Object(
        "floor",
        Transform.Identity(),
        new Material(Color.White, 1.0f, ColorTexture: floorColor, RoughnessTexture: floorRough, NormalTexture: floorNormal),
        new AABox(new Vec3(-30f, -0.2f, -2f), new Vec3(30f, 0f, 15f))));

    // Dragon.
    {
        Quat rot = Quat.AngleAxis(float.DegreesToRadians(-40f), Vec3.Up);
        ObjLoader.Load("assets/dragon.obj", scene, new ObjConfig
        {
            Transform = new Transform(new Vec3(1f, 1.7f, 4f), rot, new Vec3(6f, 6f, 6f)),
            Material = new Material(new Color(0.2f, 0.7f, 0.2f), 0.25f, 0.0f),
        }, counters);
    }

    // Spheres.
    {
        Color ballColor = new Color(1f, 0.35f, 0.05f);
        float radius = 0.75f;
        Vec3 center = new Vec3(1f, radius, 4f);
        float orbitRadius = 3.5f;
        int sphereCount = 10;
        for (int i = 0; i < sphereCount; ++i)
        {
            float angle = i * (MathF.PI * 2f / sphereCount);
            float roughness = 1.0f - (float)i / (sphereCount - 1);
            Vec3 pos = center + new Vec3(MathF.Cos(angle) * orbitRadius, 0f, MathF.Sin(angle) * orbitRadius);
            Material mat = new Material(ballColor, roughness, 1.0f);
            IShape shape = new Sphere(pos, radius);
            scene.AddObject(new Object($"sphere_{i}", Transform.Identity(), mat, shape));
        }
    }

    scene.Build(counters);
}

// scene.OverlayInfo(overlay);
// scene.OverlayWireframe(overlay);
// scene.OverlayBounds(overlay, 4);

if (dumpScene)
{
    scene.DescribeIndented("> Scene", fmt);
    fmt.Separate();
    FlushToConsole(fmt);
}

View view = new View(
    new Transform(
        new Vec3(1f, 5f, -2f),
        Quat.AngleAxis(float.DegreesToRadians(35f), Vec3.Right)),
    float.DegreesToRadians(75f));

Renderer renderer = new Renderer(
    scene, view,
    width, height,
    blockSize, minSamples, maxSamples, varianceThreshold, bounces, indirectClamp,
    counters);

Compositor compositor = new Compositor(
    tonemapper, exposure,
    denoiseSigmaSpace, denoiseSigmaColor, denoiseSigmaNormal, denoiseSigmaDepth,
    counters);

Image imageOut = new Image(width, height);

fmt.WriteLine("> Starting render");
FlushToConsole(fmt);

using (var timerRender = counters.TimeScope(Counters.Type.TimeRender))
{
    (uint Step, uint Total) progress;
    do
    {
        progress = renderer.Tick();

        // Preview intermediate results.
        if (outputPreview && progress.Step % previewInterval == 0)
        {
            compositor.Preview(renderer, overlay, imageOut);
            imageOut.Save(Path.Combine(outputPath, "preview.bmp"));
        }

        Timestamp? estTotal = null;
        if (progress.Step > 0)
        {
            estTotal = timerRender.Elapsed * progress.Total / progress.Step;
        }

        fmt.Write($"> Rendering [{progress.Step,4} / {progress.Total}]");
        fmt.Write($" {timerRender.Elapsed,8} / {(estTotal?.ToString() ?? "?"),-8}");
        fmt.EndLine();
        FlushToConsole(fmt);
    } while (progress.Step != progress.Total);
}

if (outputPreview)
{
    // Output final 'preview' (non-denoised) output.
    compositor.Preview(renderer, overlay, imageOut);
    imageOut.Save(Path.Combine(outputPath, "preview.bmp"));
}

if (outputNormal)
{
    for (uint i = 0; i != (width * height); ++i)
    {
        imageOut.Pixels[i] = new Pixel(
            (byte)((renderer.Normals[i].X * 0.5f + 0.5f) * 255f),
            (byte)((renderer.Normals[i].Y * 0.5f + 0.5f) * 255f),
            (byte)((renderer.Normals[i].Z * 0.5f + 0.5f) * 255f));
    }
    imageOut.Save(Path.Combine(outputPath, "normal.bmp"));
}

if (outputDepth)
{
    for (uint i = 0; i != (width * height); ++i)
    {
        const float depthMaxInv = 1f / 25f;
        float depth = renderer.Depth[i];
        imageOut.Pixels[i] = float.IsInfinity(depth)
            ? Pixel.Red
            : new Pixel((byte)(Math.Clamp(depth * depthMaxInv, 0f, 1f) * 255f));
    }
    imageOut.Save(Path.Combine(outputPath, "depth.bmp"));
}

if (outputSamples)
{
    float logRange = MathF.Log2(maxSamples / (float)minSamples);
    for (uint i = 0; i != (width * height); ++i)
    {
        float frac = MathF.Log2(renderer.Samples[i] / (float)minSamples) / logRange;
        imageOut.Pixels[i] = new Pixel((byte)(frac * 255f));
    }
    imageOut.Save(Path.Combine(outputPath, "samples.bmp"));
}

if (outputUv)
{
    for (uint i = 0; i < width * height; ++i)
    {
        Vec2 uv = renderer.Uv[i];
        imageOut.Pixels[i] = new Pixel(
            (byte)(MathF.Abs(uv.X % 1f) * 255f),
            (byte)(MathF.Abs(uv.Y % 1f) * 255f),
        0);
    }
    imageOut.Save(Path.Combine(outputPath, "uv.bmp"));
}

counters.Describe(fmt);
FlushToOverlay(fmt, overlay, new Vec2i(8, 8));

if (outputImage)
{
    fmt.WriteLine("> Compositing");
    FlushToConsole(fmt);
    using (counters.TimeScope(Counters.Type.TimeCompose))
    {
        compositor.Preview(renderer, overlay, imageOut);
        imageOut.Save(Path.Combine(outputPath, "final.bmp"));
    }
}

timerTotal.Dispose();

counters.DescribeIndented("> Counters", fmt);
fmt.Separate();
fmt.WriteLine($"> Finished: {outputPath}");
FlushToConsole(fmt);
