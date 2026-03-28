using System;

Console.WriteLine("Performing setup");

const uint width = 128;
const uint height = 128;
const uint blockSize = 16;
const String outputPath = "output.bmp";

Scene scene = new Scene();
scene.AddObject(new Object(
    new Transform(new Vec3(0f, 0f, 5f)),
    new Material(new Color(1f, 0.2f, 0.2f), new Color(1f, 0.2f, 0.2f)),
    new Sphere(Vec3.Zero, 1f)));

scene.AddObject(new Object(
    new Transform(new Vec3(2.5f, 0f, 5f)),
    new Material(new Color(0.2f, 1f, 0.2f), new Color(0.2f, 1f, 0.2f)),
    new Sphere(Vec3.Zero, 1f)));

scene.AddObject(new Object(
    new Transform(new Vec3(-2.5f, 0f, 5f)),
    new Material(new Color(0.2f, 0.2f, 1f), new Color(0.2f, 0.2f, 1f)),
    new Sphere(Vec3.Zero, 1f)));

View view = new View(new Transform(new Vec3(0f, 0.5f, -1f)), float.DegreesToRadians(75f));

Renderer renderer = new Renderer(scene, view, width, height, blockSize);

Console.WriteLine("Starting render");
(uint Step, uint Total) progress;
do
{
    progress = renderer.Tick();
    Console.WriteLine($"Rendering [{progress.Step} / {progress.Total}]");
} while (progress.Step != progress.Total);

renderer.Result.Save(outputPath);
Console.WriteLine($"Finished: {outputPath}");
