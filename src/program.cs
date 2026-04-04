using System;
using System.IO;

void FlushToConsole(ref FormatWriter fmt)
{
    Console.Write(fmt.ToString());
    fmt.Clear();
}

void FlushToOverlay(ref FormatWriter fmt, Overlay overlay, Vec2i screenPos)
{
    overlay.AddText(fmt.ToString(), screenPos, Color.White);
    fmt.Clear();
}

FormatWriter fmt = new FormatWriter();

fmt.WriteLine("[SharpRay]");
fmt.WriteLine("> Performing setup");
FlushToConsole(ref fmt);

const uint width = 512;
const uint height = 512;
const uint blockSize = 32;
const uint samples = 128;
const uint bounces = 16;
const float denoiseSigmaSpace = 4.0f;
const float denoiseSigmaColor = 0.025f;
const float denoiseSigmaNormal = 0.05f;
const float denoiseSigmaDepth = 1.0f;
const bool dumpScene = true;
const bool outputImage = true, outputPreview = true, outputNormal = true, outputUv = true, outputDepth = true;
const uint previewInterval = 100;

Counters counters = new Counters();
var timerTotal = counters.TimeScope(Counters.Type.TimeTotal);

String outputPath = Path.GetFullPath("output");
Directory.CreateDirectory(outputPath);

Overlay overlay = new Overlay();

Sky sky = new Sky(
    new Color(0.35f, 0.45f, 0.75f) * 0.75f,
    new Color(0.85f, 0.8f, 0.8f) * 0.75f,
    new Color(0.45f, 0.38f, 0.26f) * 0.75f,
    new Vec3(0.4f, 0.3f, 1f).Normalize(),
    new Color(4f, 3.5f, 2.5f),
    float.DegreesToRadians(2.6f));

View view = new View(
    new Transform(
        new Vec3(1f, 5f, -2f),
        Quat.AngleAxis(float.DegreesToRadians(35f), Vec3.Right)),
    float.DegreesToRadians(75f));

Scene scene = new Scene(sky);
using (counters.TimeScope(Counters.Type.TimeSetup))
{
    // Floor.
    Texture floorColor = Texture.FromSrgb(Image.Load("assets/cobblestone/cobblestone_diff.tga"));
    Texture floorRough = Texture.FromLinear(Image.Load("assets/cobblestone/cobblestone_rough.tga"));
    Texture floorNormal = Texture.FromNormal(Image.Load("assets/cobblestone/cobblestone_nor.tga"));
    floorColor.Tiling = floorRough.Tiling = floorNormal.Tiling = new Vec2(6f, 1.5f);
    scene.AddObject(new Object(
        "floor",
        Transform.Identity(),
        new Material(Color.White, 1.0f, ColorTexture: floorColor, RoughnessTexture: floorRough, NormalTexture: floorNormal),
        new AABox(new Vec3(-30f, -0.2f, -2f), new Vec3(30f, 0f, 15f))));

    // Dragon.
    {
        Quat rot = Quat.AngleAxis(float.DegreesToRadians(-40f), Vec3.Up);
        Vec3 scale = new Vec3(6f, 6f, 6f);
        Transform trans = new Transform(new Vec3(1f, 1.7f, 4f), rot, scale);
        Material mat = new Material(new Color(0.2f, 0.7f, 0.2f), 0.25f, 0.0f);
        ObjLoader.Load("assets/dragon.obj", scene, trans, mat, counters);
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
            Material mat = new Material(ballColor, roughness, 0.5f);
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
    fmt.WriteLine("> Scene");
    fmt.IndentPush();
    scene.Describe(ref fmt);
    fmt.Separate();
    FlushToConsole(ref fmt);
}

Renderer renderer = new Renderer(scene, view, width, height, blockSize, samples, bounces, counters);
Compositor compositor = new Compositor(denoiseSigmaSpace, denoiseSigmaColor, denoiseSigmaNormal, denoiseSigmaDepth, counters);
Image imageOut = new Image(width, height);

fmt.WriteLine("> Starting render");
FlushToConsole(ref fmt);

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
        FlushToConsole(ref fmt);
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
    for (uint i = 0; i < width * height; ++i)
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
    for (uint i = 0; i < width * height; ++i)
    {
        const float depthMaxInv = 1f / 25f;
        float depth = renderer.Depth[i];
        imageOut.Pixels[i] = float.IsInfinity(depth)
            ? Pixel.Red
            : new Pixel((byte)(Math.Clamp(depth * depthMaxInv, 0f, 1f) * 255f));
    }
    imageOut.Save(Path.Combine(outputPath, "depth.bmp"));
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

counters.Dump(ref fmt);
FlushToOverlay(ref fmt, overlay, new Vec2i(8, 8));

if (outputImage)
{
    fmt.WriteLine("> Compositing");
    FlushToConsole(ref fmt);
    using (counters.TimeScope(Counters.Type.TimeCompose))
    {
        compositor.Compose(renderer, overlay, imageOut);
        imageOut.Save(Path.Combine(outputPath, "final.bmp"));
    }
}

timerTotal.Dispose();

fmt.WriteLine("> Counters");
fmt.Separate();
fmt.IndentPush();
counters.Dump(ref fmt);
fmt.IndentPop();
fmt.Separate();
fmt.WriteLine($"> Finished: {outputPath}");
FlushToConsole(ref fmt);
