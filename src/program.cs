using System;
using System.IO;

Timestamp timeStart = Timestamp.Now();

Console.WriteLine("Performing setup");

const uint width = 512;
const uint height = 512;
const uint blockSize = 16;
const uint samples = 64;
const uint bounces = 8;
const float denoiseSigmaSpace = 4.0f;
const float denoiseSigmaColor = 0.15f;
const float denoiseSigmaNormal = 0.5f;
const float denoiseSigmaDepth = 1.0f;
const String outputPath = "output";
const bool outputImage = true, outputPreview = true, outputNormal = true, outputDepth = true;
const uint previewInterval = 50;

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

scene.AddObject(new Object(
    new Transform(new Vec3(-3.5f, 0.5f, 9f)),
    new Material(new Color(0.1f, 0.8f, 0.7f), 0.8f),
    new Sphere(Vec3.Zero, 1.5f)));

scene.AddObject(new Object(
    new Transform(new Vec3(3.5f, 0.5f, 9f)),
    new Material(new Color(1f, 0.75f, 0.1f), 0.1f, 1f),
    new Sphere(Vec3.Zero, 1.5f)));

scene.AddObject(new Object(
    new Transform(new Vec3(-3.5f, 0f, 6f)),
    new Material(new Color(1f, 0.2f, 0.2f), 1.0f),
    new Sphere(Vec3.Zero, 1f)));

scene.AddObject(new Object(
    new Transform(new Vec3(3.5f, 0f, 6f)),
    new Material(new Color(0.2f, 0.2f, 1f), 0.0f, 1f),
    new Sphere(Vec3.Zero, 1f)));

scene.AddObject(new Object(
    new Transform(new Vec3(-2.5f, -0.4f, 3.5f)),
    new Material(new Color(1f, 0.5f, 0.1f), 0.75f),
    new Sphere(Vec3.Zero, 0.6f)));

scene.AddObject(new Object(
    new Transform(new Vec3(2.5f, -0.4f, 3.5f)),
    new Material(new Color(0.6f, 0.2f, 1f), 0.25f),
    new Sphere(Vec3.Zero, 0.6f)));

scene.AddObject(new Object(
    new Transform(new Vec3(-0.5f, -0.65f, 2.5f)),
    new Material(new Color(0.9f, 0.9f, 0.9f), 0.9f),
    new Sphere(Vec3.Zero, 0.525f)));

scene.AddObject(new Object(
    new Transform(new Vec3(0.5f, -0.65f, 2.5f)),
    new Material(new Color(1f, 0.4f, 0.6f), 0.05f),
    new Sphere(Vec3.Zero, 0.525f)));

scene.AddObject(new Object(
    Transform.Identity(),
    new Material(new Color(0.1f, 0.1f, 0.1f), 1.0f),
    new AABox(new Vec3(-10f, -1.2f, -2f), new Vec3(10f, -1f, 20f))));

{
    Quat rot = Quat.AngleAxis(float.DegreesToRadians(200f), Vec3.Up);
    Vec3 scale = new Vec3(3f, 3f, 3f);
    Mesh mesh = ObjLoader.Load("assets/suzanne.obj");
    const float floorY = -1f;
    float meshBottomY = (mesh.Bounds().Center.Y - mesh.Bounds().Min.Y) * scale.Y;
    Vec3 desiredPos = new Vec3(0f, floorY + meshBottomY, 7.5f);
    Transform trans = new Transform(desiredPos - rot * (mesh.Bounds().Center * scale), rot, scale);
    scene.AddObject(new Object(trans, new Material(new Color(0.9f, 0.5f, 0.2f), 0.75f), mesh));
    mesh.OverlayWireframe(overlay, trans, Color.Gray);
}

scene.OverlayBounds(overlay);

View view = new View(new Transform(new Vec3(0f, 0.5f, -1f)), float.DegreesToRadians(75f));
Renderer renderer = new Renderer(scene, view, width, height, blockSize, samples, bounces);
Compositor compositor = new Compositor(denoiseSigmaSpace, denoiseSigmaColor, denoiseSigmaNormal, denoiseSigmaDepth);

Console.WriteLine("Starting render");

(uint Step, uint Total) progress;
do
{
    progress = renderer.Tick();

    // Preview intermediate results.
    if (outputImage && outputPreview && progress.Step % previewInterval == 0)
    {
        Image preview = compositor.Preview(renderer.Radiance, renderer.Normals, renderer.Depth, width, height, view, overlay);
        preview.Save(Path.Combine(outputPath, "image.bmp"));
    }

    Console.WriteLine($"Rendering [{progress.Step,3} / {progress.Total}]");
} while (progress.Step != progress.Total);

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

if (outputImage)
{
    Console.WriteLine("Compositing");
    Image image = compositor.Compose(renderer.Radiance, renderer.Normals, renderer.Depth, width, height, view, overlay);
    image.Save(Path.Combine(outputPath, "image.bmp"));
}

double timeElapsed = (Timestamp.Now() - timeStart).Seconds;
Console.WriteLine($"Finished (time: {timeElapsed:F1} s): {outputPath}");
