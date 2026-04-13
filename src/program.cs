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

string configPath = args.Length > 0 ? args[0] : "config.json";
if (!File.Exists(configPath))
{
    Console.Error.WriteLine($"[SharpRay] Error: Config file not found: '{configPath}'");
    Console.Error.WriteLine($"           Create a config.json file or pass a config path as the first argument.");
    Environment.Exit(1);
    return;
}

Config config = Config.Load(configPath);
ConfigRender render = config.Render;
ConfigComposite composite = config.Composite;

Counters counters = new Counters();
var timerTotal = counters.TimeScope(Counters.Type.TimeTotal);

String outputPath = Path.GetFullPath("output");
Directory.CreateDirectory(outputPath);

Overlay overlay = new Overlay();

Scene scene = new Scene();
using (counters.TimeScope(Counters.Type.TimeSetup))
{
    scene.SetSky(ConfigConvert.ToSky(config.Scene.Sky));

    if (config.Scene.Fog != null)
        scene.SetFog(ConfigConvert.ToFog(config.Scene.Fog));

    foreach (ConfigObject obj in config.Scene.Objects)
    {
        Transform trans = ConfigConvert.ToTransform(obj.Transform);
        Material mat = ConfigConvert.ToMaterial(obj.Material);

        if (obj.Shape is ConfigShapeObj objShape)
        {
            ObjLoader.Load(objShape.Path, scene, new ObjConfig
            {
                Transform = trans,
                Material = mat,
            }, counters);
        }
        else if (ConfigConvert.ToShape(obj.Shape) is IShape shape)
        {
            scene.AddObject(new Object(obj.Name, trans, mat, shape));
        }
    }

    scene.Build(counters);
}

if (render.DumpScene)
{
    scene.DescribeIndented("> Scene", fmt);
    fmt.Separate();
    FlushToConsole(fmt);
}

View view = ConfigConvert.ToView(config.Scene.Camera);

Renderer renderer = new Renderer(
    scene, view,
    render.Width, render.Height,
    render.BlockSize, render.MinSamples, render.MaxSamples, render.VarianceThreshold,
    render.Bounces, render.IndirectClamp,
    counters);

Compositor compositor = new Compositor(
    composite.Tonemapper, composite.Exposure,
    composite.DenoiseRadius, composite.DenoiseStrength, composite.DenoiseStrengthMax,
    composite.DenoiseLuminanceBoost, composite.DenoiseLuminanceLimit,
    composite.DenoiseNormalLimit, composite.DenoiseDepthLimit,
    composite.DenoiseFogRadius, composite.DenoiseFogStrength, counters);

Image imageOut = new Image(render.Width, render.Height);

fmt.WriteLine("> Starting render");
FlushToConsole(fmt);

using (var timerRender = counters.TimeScope(Counters.Type.TimeRender))
{
    (uint Step, uint Total) progress;
    do
    {
        progress = renderer.Tick();

        // Preview intermediate results.
        if (render.OutputPreview && progress.Step % render.PreviewInterval == 0)
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

if (render.OutputPreview)
{
    // Output final 'preview' (non-denoised) output.
    compositor.Preview(renderer, overlay, imageOut);
    imageOut.Save(Path.Combine(outputPath, "preview.bmp"));
}

if (render.OutputNormal)
{
    for (uint i = 0; i != (render.Width * render.Height); ++i)
    {
        imageOut.Pixels[i] = new Pixel(
            (byte)((renderer.Normals[i].X * 0.5f + 0.5f) * 255f),
            (byte)((renderer.Normals[i].Y * 0.5f + 0.5f) * 255f),
            (byte)((renderer.Normals[i].Z * 0.5f + 0.5f) * 255f));
    }
    imageOut.Save(Path.Combine(outputPath, "normal.bmp"));
}

if (render.OutputDepth)
{
    for (uint i = 0; i != (render.Width * render.Height); ++i)
    {
        const float depthMaxInv = 1f / 25f;
        float depth = renderer.Depth[i];
        imageOut.Pixels[i] = float.IsInfinity(depth)
            ? Pixel.Red
            : new Pixel((byte)(Math.Clamp(depth * depthMaxInv, 0f, 1f) * 255f));
    }
    imageOut.Save(Path.Combine(outputPath, "depth.bmp"));
}

if (render.OutputVariance)
{
    for (uint i = 0; i != (render.Width * render.Height); ++i)
    {
        float t = renderer.Variance[i] / (render.VarianceThreshold * 2f);
        byte v = (byte)(Math.Clamp(t, 0f, 1f) * 255f);
        imageOut.Pixels[i] = new Pixel(v);
    }
    imageOut.Save(Path.Combine(outputPath, "variance.bmp"));
}

if (render.OutputSamples)
{
    float logRange = MathF.Log2(render.MaxSamples / (float)render.MinSamples);
    for (uint i = 0; i != (render.Width * render.Height); ++i)
    {
        float frac = MathF.Log2(renderer.Samples[i] / (float)render.MinSamples) / logRange;
        imageOut.Pixels[i] = new Pixel((byte)(frac * 255f));
    }
    imageOut.Save(Path.Combine(outputPath, "samples.bmp"));
}

if (render.OutputUv)
{
    for (uint i = 0; i < render.Width * render.Height; ++i)
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

if (render.OutputImage)
{
    fmt.WriteLine("> Compositing");
    FlushToConsole(fmt);
    using (counters.TimeScope(Counters.Type.TimeCompose))
    {
        compositor.Compose(renderer, overlay, imageOut);
        imageOut.Save(Path.Combine(outputPath, "final.bmp"));
    }
}

timerTotal.Dispose();

counters.DescribeIndented("> Counters", fmt);
fmt.Separate();
fmt.WriteLine($"> Finished: {outputPath}");
FlushToConsole(fmt);
