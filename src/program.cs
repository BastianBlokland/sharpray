using System;

Timestamp timeStart = Timestamp.Now();

Console.WriteLine("Performing setup");

const uint width = 512;
const uint height = 512;
const uint blockSize = 16;
const uint samples = 512;
const uint bounces = 4;
const uint saveInterval = 10;
const String outputPath = "output.bmp";

Sky sky = new Sky(
    new Color(0.4f, 0.5f, 0.8f),
    new Color(1.0f, 0.9f, 0.9f),
    new Color(0.5f, 0.425f, 0.275f),
    new Vec3(0.4f, 0.5f, 1f).Normalize(),
    new Color(4f, 3.5f, 2.5f),
    MathF.Cos(float.DegreesToRadians(2.6f)));

Scene scene = new Scene(sky);

scene.AddObject(new Object(
    new Transform(new Vec3(0f, 0f, 5f)),
    new Material(new Color(1f, 0.2f, 0.2f)),
    new Sphere(Vec3.Zero, 1f)));

scene.AddObject(new Object(
    new Transform(new Vec3(2.5f, 0f, 5f)),
    new Material(new Color(0.2f, 1f, 0.2f)),
    new Sphere(Vec3.Zero, 1f)));

scene.AddObject(new Object(
    new Transform(new Vec3(-2.5f, 0f, 5f)),
    new Material(new Color(0.2f, 0.2f, 1f)),
    new Sphere(Vec3.Zero, 1f)));

scene.AddObject(new Object(
    Transform.Identity(),
    new Material(new Color(0.2f, 0.2f, 0.2f)),
    new AABox(new Vec3(-10f, -1.2f, -2f), new Vec3(10f, -1f, 20f))));

View view = new View(new Transform(new Vec3(0f, 0.5f, -1f)), float.DegreesToRadians(75f));

Renderer renderer = new Renderer(scene, view, width, height, blockSize, samples, bounces);

Console.WriteLine("Starting render");

(uint Step, uint Total) progress;
do
{
    progress = renderer.Tick();

    // Save intermediate results for previewing purposes.
    if (progress.Step % saveInterval == 0)
        renderer.Result.Save(outputPath);

    Console.WriteLine($"Rendering [{progress.Step,3} / {progress.Total}]");
} while (progress.Step != progress.Total);

renderer.Result.Save(outputPath);

double timeElapsed = (Timestamp.Now() - timeStart).Seconds;
Console.WriteLine($"Finished (time: {timeElapsed:F1} s): {outputPath}");
