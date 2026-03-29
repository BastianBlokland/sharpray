using System;
using System.IO;

Console.WriteLine("[SharpRay]");
Console.WriteLine("> Performing setup");

const uint width = 512;
const uint height = 512;
const uint blockSize = 32;
const uint samples = 64;
const uint bounces = 8;
const float denoiseSigmaSpace = 4.0f;
const float denoiseSigmaColor = 0.15f;
const float denoiseSigmaNormal = 0.5f;
const float denoiseSigmaDepth = 1.0f;
const bool outputImage = true, outputPreview = true, outputNormal = true, outputDepth = true;
const uint previewInterval = 100;

Counters counters = new Counters();
var timerTotal = counters.Scope(Timer.Total);

String outputPath = Path.GetFullPath("output");
Directory.CreateDirectory(outputPath);

Overlay overlay = new Overlay();

Sky sky = new Sky(
    new Color(0.35f, 0.45f, 0.75f),
    new Color(0.85f, 0.8f, 0.8f),
    new Color(0.45f, 0.38f, 0.26f),
    new Vec3(0.4f, 0.5f, 1f).Normalize(),
    new Color(4f, 3.5f, 2.5f),
    float.DegreesToRadians(2.6f));

Scene scene = new Scene(sky);
using (counters.Scope(Timer.Setup))
{
    // Floor.
    scene.AddObject(new Object(
        Transform.Identity(),
        new Material(new Color(0.1f, 0.1f, 0.1f), 1.0f),
        new AABox(new Vec3(-10f, -1.2f, -2f), new Vec3(10f, -1f, 20f))));

    // Bunny.
    {
        Quat rot = Quat.AngleAxis(float.DegreesToRadians(200f), Vec3.Up);
        Vec3 scale = new Vec3(32f, 32f, 32f);
        Mesh mesh = ObjLoader.Load("assets/bunny.obj");
        const float floorY = -1f;
        float meshBottomY = (mesh.Bounds().Center.Y - mesh.Bounds().Min.Y) * scale.Y;
        Vec3 desiredPos = new Vec3(-1, floorY + meshBottomY, 4f);
        Transform trans = new Transform(desiredPos - rot * (mesh.Bounds().Center * scale), rot, scale);
        scene.AddObject(new Object(trans, new Material(new Color(0.72f, 0.45f, 0.2f), 0.35f, 1.0f), mesh));
    }

    scene.Lock();
}

View view = new View(new Transform(new Vec3(0f, 0.5f, -1f)), float.DegreesToRadians(75f));
Renderer renderer = new Renderer(scene, view, width, height, blockSize, samples, bounces, counters);
Compositor compositor = new Compositor(denoiseSigmaSpace, denoiseSigmaColor, denoiseSigmaNormal, denoiseSigmaDepth);

Console.WriteLine("> Starting render");

(uint Step, uint Total) progress;

using (counters.Scope(Timer.Render))
{
    do
    {
        progress = renderer.Tick();

        // Preview intermediate results.
        if (outputPreview && progress.Step % previewInterval == 0)
        {
            compositor.Preview(renderer, overlay).Save(Path.Combine(outputPath, "preview.bmp"));
        }

        Console.WriteLine($"> Rendering [{progress.Step,4} / {progress.Total}]");
    } while (progress.Step != progress.Total);
}

if (outputPreview)
{
    // Output final 'preview' (non-denoised) output.
    compositor.Preview(renderer, overlay).Save(Path.Combine(outputPath, "preview.bmp"));
}

if (outputNormal)
{
    Image normalsImage = new Image(width, height);
    for (uint i = 0; i < width * height; ++i)
    {
        normalsImage.Pixels[i] = new Pixel(
            (byte)((renderer.Normals[i].X * 0.5f + 0.5f) * 255f),
            (byte)((renderer.Normals[i].Y * 0.5f + 0.5f) * 255f),
            (byte)((renderer.Normals[i].Z * 0.5f + 0.5f) * 255f));
    }
    normalsImage.Save(Path.Combine(outputPath, "normal.bmp"));
}

if (outputDepth)
{
    Image depthImage = new Image(width, height);
    for (uint i = 0; i < width * height; ++i)
    {
        const float depthMaxInv = 1f / 25f;
        float depth = renderer.Depth[i];
        depthImage.Pixels[i] = float.IsInfinity(depth)
            ? Pixel.Red
            : new Pixel((byte)(Math.Clamp(depth * depthMaxInv, 0f, 1f) * 255f));
    }
    depthImage.Save(Path.Combine(outputPath, "depth.bmp"));
}

overlay.AddText(counters.Dump(), new Vec2i(8, 8), Color.White);

if (outputImage)
{
    Console.WriteLine("> Compositing");
    using (counters.Scope(Timer.Composite))
    {
        compositor.Compose(renderer, overlay).Save(Path.Combine(outputPath, "final.bmp"));
    }
}

timerTotal.Dispose();

Console.WriteLine(string.Empty);
Console.WriteLine(counters.Dump());
Console.WriteLine(string.Empty);
Console.WriteLine($"Finished: {outputPath}");
