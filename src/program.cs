using System;

Timestamp timeStart = Timestamp.Now();

Console.WriteLine("Performing setup");

const uint width = 512;
const uint height = 512;
const uint blockSize = 16;
const uint samples = 128;
const uint bounces = 8;
const uint saveInterval = 10;
const float denoiseSigmaSpace = 4.0f;
const float denoiseSigmaColor = 0.15f;
const float denoiseSigmaNormal = 0.5f;
const String outputPath = "output.bmp";
const String normalsPath = "normals.bmp";

Sky sky = new Sky(
    new Color(0.35f, 0.45f, 0.75f),
    new Color(0.85f, 0.8f, 0.8f),
    new Color(0.45f, 0.38f, 0.26f),
    new Vec3(0.4f, 0.5f, 1f).Normalize(),
    new Color(4f, 3.5f, 2.5f),
    float.DegreesToRadians(2.6f));

Scene scene = new Scene(sky);

scene.AddObject(new Object(
    new Transform(new Vec3(-2f, 0.5f, 9f)),
    new Material(new Color(0.1f, 0.8f, 0.7f), 0.8f),
    new Sphere(Vec3.Zero, 1.5f)));

scene.AddObject(new Object(
    new Transform(new Vec3(2f, 0.5f, 9f)),
    new Material(new Color(1f, 0.75f, 0.1f), 0.1f),
    new Sphere(Vec3.Zero, 1.5f)));

scene.AddObject(new Object(
    new Transform(new Vec3(-2.5f, 0f, 6f)),
    new Material(new Color(1f, 0.2f, 0.2f), 1.0f),
    new Sphere(Vec3.Zero, 1f)));

scene.AddObject(new Object(
    new Transform(new Vec3(0f, 0f, 6f)),
    new Material(new Color(0.2f, 1f, 0.2f), 0.5f),
    new Sphere(Vec3.Zero, 1f)));

scene.AddObject(new Object(
    new Transform(new Vec3(2.5f, 0f, 6f)),
    new Material(new Color(0.2f, 0.2f, 1f), 0.0f),
    new Sphere(Vec3.Zero, 1f)));

scene.AddObject(new Object(
    new Transform(new Vec3(-1.25f, -0.4f, 3.5f)),
    new Material(new Color(1f, 0.5f, 0.1f), 0.75f),
    new Sphere(Vec3.Zero, 0.6f)));

scene.AddObject(new Object(
    new Transform(new Vec3(1.25f, -0.4f, 3.5f)),
    new Material(new Color(0.6f, 0.2f, 1f), 0.25f),
    new Sphere(Vec3.Zero, 0.6f)));

scene.AddObject(new Object(
    new Transform(new Vec3(-0.5f, -0.65f, 1.5f)),
    new Material(new Color(0.9f, 0.9f, 0.9f), 0.9f),
    new Sphere(Vec3.Zero, 0.35f)));

scene.AddObject(new Object(
    new Transform(new Vec3(0.5f, -0.65f, 1.5f)),
    new Material(new Color(1f, 0.4f, 0.6f), 0.05f),
    new Sphere(Vec3.Zero, 0.35f)));

scene.AddObject(new Object(
    Transform.Identity(),
    new Material(new Color(0.1f, 0.1f, 0.1f), 1.0f),
    new AABox(new Vec3(-10f, -1.2f, -2f), new Vec3(10f, -1f, 20f))));

View view = new View(new Transform(new Vec3(0f, 0.5f, -1f)), float.DegreesToRadians(75f));

Renderer renderer = new Renderer(scene, view, width, height, blockSize, samples, bounces);
Compositor compositor = new Compositor(denoiseSigmaSpace, denoiseSigmaColor, denoiseSigmaNormal);

Console.WriteLine("Starting render");

(uint Step, uint Total) progress;
do
{
    progress = renderer.Tick();

    // Save intermediate results for previewing purposes.
    if (progress.Step % saveInterval == 0)
        compositor.Preview(renderer.Radiance, width, height).Save(outputPath);

    Console.WriteLine($"Rendering [{progress.Step,3} / {progress.Total}]");
} while (progress.Step != progress.Total);

if (normalsPath != "")
{
    Image normalsImage = new Image(width, height);
    for (uint i = 0; i < width * height; ++i)
    {
        normalsImage.Pixels[i] = new Pixel(
            (byte)((renderer.Normals[i].X * 0.5f + 0.5f) * 255f),
            (byte)((renderer.Normals[i].Y * 0.5f + 0.5f) * 255f),
            (byte)((renderer.Normals[i].Z * 0.5f + 0.5f) * 255f));
    }
    normalsImage.Save(normalsPath);
}

Console.WriteLine("Compositing");

compositor.Compose(renderer.Radiance, renderer.Normals, width, height).Save(outputPath);

double timeElapsed = (Timestamp.Now() - timeStart).Seconds;
Console.WriteLine($"Finished (time: {timeElapsed:F1} s): {outputPath}");
