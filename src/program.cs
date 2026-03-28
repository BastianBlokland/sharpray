using System;

Console.WriteLine("Performing setup");

const uint width = 128;
const uint height = 128;
const String outputPath = "output.bmp";

Scene scene = new Scene();
View view = new View();
Renderer renderer = new Renderer(scene, view, width, height);

Console.WriteLine("Starting render");
(uint Step, uint Total) progress;
do
{
    progress = renderer.Tick();
    Console.WriteLine($"Rendering [{progress.Step} / {progress.Total}]");
} while (progress.Step != progress.Total);

renderer.Result.Save(outputPath);
Console.WriteLine($"Finished: {outputPath}");
