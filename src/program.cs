using System;

Timestamp timeStart = Timestamp.Now();

Console.WriteLine("Performing setup");

const uint width = 256;
const uint height = 256;
const uint blockSize = 16;
const uint samples = 32;
const uint bounces = 128;
const uint saveInterval = 10;
const String outputPath = "output.bmp";

Scene scene = new Scene(Sky.Default());
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
