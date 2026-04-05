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

const uint width = 2048;
const uint height = 2048;
const uint blockSize = 128;
const uint samples = 1;
const uint bounces = 0;
const Tonemapper tonemapper = Tonemapper.Reinhard;
const float exposure = 2.0f;
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

Scene scene = new Scene();
using (counters.TimeScope(Counters.Type.TimeSetup))
{
    // Sky.
    // Texture skyTexture = Texture.FromHdr(ImageHdr.Load("assets/qwantani_late_afternoon.hdr"));
    // scene.Sky = new SkyTexture(skyTexture);
    scene.Sky = new SkyProcedural(
        new Color(0.08f, 0.17f, 0.70f),   // zenith: deep blue
        new Color(0.50f, 0.65f, 0.90f),   // horizon: light blue-white
        new Color(0.12f, 0.09f, 0.07f),   // below horizon: dark warm
        new Vec3(0.5f, 1f, -0.5f).Normalize(),
        new Color(100000f, 90000f, 65000f), // ~5500K, sun/sky illuminance ratio ~5:1
        float.DegreesToRadians(0.53f));     // real solar angular diameter

    // Single test sphere.
    scene.AddObject(new Object(
        "sphere",
        Transform.Identity(),
        new Material(new Color(0.8f, 0.4f, 0.1f), 0.3f, 0.0f),
        new Sphere(new Vec3(0f, 0f, 4f), 1.5f)));

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

View view = new View(
    new Transform(new Vec3(0f, 0f, -2f), Quat.Identity()),
    float.DegreesToRadians(60f));

Renderer renderer = new Renderer(scene, view, width, height, blockSize, samples, bounces, counters);
Compositor compositor = new Compositor(tonemapper, exposure, denoiseSigmaSpace, denoiseSigmaColor, denoiseSigmaNormal, denoiseSigmaDepth, counters);
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
        compositor.Preview(renderer, overlay, imageOut);
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
