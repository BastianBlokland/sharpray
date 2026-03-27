using System;
using System.IO;

Console.WriteLine("Rendering..");

Image img = new Image(128, 128);
Array.Fill(img.Pixels, new Pixel(255, 0, 0));
img.Save("output.bmp");

Console.WriteLine("Finished: output.bmp");
